namespace AgentFramework.OpenTelemetry.Agents;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using OpenAI.Responses;

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

#pragma warning disable MEAI001

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
public sealed partial class FoundryAgent(AIAgent innerAgent, ILoggerFactory loggerFactory, bool autoApproveHostedMcpRequests = true)
    : AgentBase(new OpenTelemetryAgent(innerAgent, innerAgent.Name ?? innerAgent.Id) { EnableSensitiveData = true })
{
    private readonly AIAgent _innerAgent = innerAgent;
    private readonly ILogger _logger = loggerFactory.CreateLogger<FoundryAgent>();
    private readonly bool _autoApproveHostedMcpRequests = autoApproveHostedMcpRequests;
    private AgentSession? _boundConversationSession;

    public ValueTask<AgentSession> CreateSessionAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        return _innerAgent is ChatClientAgent chatClientAgent
            ? chatClientAgent.CreateSessionAsync(conversationId, cancellationToken)
            : _innerAgent.DeserializeSessionAsync(
                JsonSerializer.SerializeToElement(new { ConversationId = conversationId }),
                null,
                cancellationToken);
    }

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => _boundConversationSession is { } boundConversationSession
            ? ValueTask.FromResult<AgentSession>(boundConversationSession)
            : base.CreateSessionCoreAsync(cancellationToken);

    public async ValueTask<AgentSession> BindConversationSessionAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        _boundConversationSession = await CreateSessionAsync(conversationId, cancellationToken).ConfigureAwait(false);
        return _boundConversationSession;
    }

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using Activity? invokeActivity = this.StartInvokeAgentActivity();
        List<ChatMessage> pendingMessages = [.. messages];

        while (true)
        {
            AgentResponse response = await this.InnerAgent.RunAsync(pendingMessages, session, options, cancellationToken).ConfigureAwait(false);

            var approvalResponses = CreateApprovalResponses(response.Messages).ToArray().AsReadOnly();
            if (!_autoApproveHostedMcpRequests || approvalResponses.Count is 0)
            {
                return response;
            }

            Log.AutoApprovingHostedMcpRequests(_logger, this.AgentNameOrId, approvalResponses.Count, JsonSerializer.Serialize(approvalResponses));
            pendingMessages = [.. approvalResponses.Select(ToChatMessage)];
        }
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using Activity? invokeActivity = this.StartInvokeAgentActivity();
        using ToolTelemetryTracker toolTelemetry = new(this.ActivitySource, this.AgentNameOrId, invokeActivity?.Context);
        List<ChatMessage> pendingMessages = [.. messages];

        while (true)
        {
            List<ToolApprovalResponseContent> approvalResponses = [];

            await foreach (AgentResponseUpdate update in this.InnerAgent
                .RunStreamingAsync(pendingMessages, session, options, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                toolTelemetry.Observe(update);

                if (!_autoApproveHostedMcpRequests)
                {
                    yield return update;
                    continue;
                }

                AgentResponseUpdate? filteredUpdate = FilterApprovalRequests(update, approvalResponses);
                if (filteredUpdate is not null)
                {
                    yield return filteredUpdate;
                }
            }

            if (!_autoApproveHostedMcpRequests || approvalResponses.Count is 0)
            {
                yield break;
            }

            Log.AutoApprovingHostedMcpRequests(_logger, this.AgentNameOrId, approvalResponses.Count, JsonSerializer.Serialize(approvalResponses));
            pendingMessages = [.. approvalResponses.Select(ToChatMessage)];
        }
    }

    private IEnumerable<ToolApprovalResponseContent> CreateApprovalResponses(IEnumerable<ChatMessage> messages)
    {
        HashSet<string> seenRequestIds = [];
        foreach (ChatMessage message in messages)
        {
            foreach (AIContent content in message.Contents)
            {
                if (content is ToolApprovalRequestContent approvalRequest)
                {
                    if (approvalRequest.ToolCall is not McpServerToolCallContent)
                    {
                        _logger.GotNonMCPToolCallContentInCreateApprovalResponsesToolCallContentType(approvalRequest.ToolCall.GetType().Name);
                        continue;
                    }
                    else if (seenRequestIds.Add(approvalRequest.RequestId))
                    {
                        yield return CreateApprovalResponse(approvalRequest);
                    }
                }
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "Unreadable")]
    private static AgentResponseUpdate? FilterApprovalRequests(
        AgentResponseUpdate update,
        List<ToolApprovalResponseContent> approvalResponses)
    {
        HashSet<string> seenRequestIds = [];
        List<AIContent>? filteredContents = null;
        bool removedApprovalRequest = false;

        foreach (AIContent content in update.Contents)
        {
            if (content is ToolApprovalRequestContent { ToolCall: McpServerToolCallContent } approvalRequest)
            {
                removedApprovalRequest = true;
                if (seenRequestIds.Add(approvalRequest.RequestId))
                {
                    approvalResponses.Add(CreateApprovalResponse(approvalRequest));
                }

                continue;
            }

            filteredContents ??= [];
            filteredContents.Add(content);
        }

        if (!removedApprovalRequest)
        {
            return update;
        }

        return filteredContents is not { Count: > 0 }
            ? null
            : new AgentResponseUpdate(update.Role, filteredContents)
                {
                    AdditionalProperties = update.AdditionalProperties,
                    AgentId = update.AgentId,
                    AuthorName = update.AuthorName,
                    CreatedAt = update.CreatedAt,
                    MessageId = update.MessageId,
                    RawRepresentation = update.RawRepresentation,
                    ResponseId = update.ResponseId,
                };
    }

    private static ToolApprovalResponseContent CreateApprovalResponse(ToolApprovalRequestContent approvalRequest)
    {
        var approvalResponse = approvalRequest.CreateResponse(approved: true);
        approvalResponse.Reason = "Auto-approved";
        if (approvalRequest.AdditionalProperties is { Count: > 0 })
        {
            approvalResponse.AdditionalProperties = [];
            foreach ((string key, object? value) in approvalRequest.AdditionalProperties)
            {
                approvalResponse.AdditionalProperties[key] = value;
            }
        }

        return approvalResponse;
    }

    private static ChatMessage ToChatMessage(ToolApprovalResponseContent approvalResponse) => new(ChatRole.Tool, [approvalResponse]);

    private static partial class Log
    {
        [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Auto-approving {ApprovalCount} hosted MCP request(s) for agent {AgentNameOrId}: {ApprovalResponses}")]
        public static partial void AutoApprovingHostedMcpRequests(ILogger logger, string agentNameOrId, int approvalCount, string approvalResponses);
    }
}

#pragma warning restore MEAI001
#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
