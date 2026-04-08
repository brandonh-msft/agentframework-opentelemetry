namespace AgentFramework.OpenTelemetry.Agents;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using OpenAI.Responses;

#pragma warning disable OPENAI001

public sealed class LocalAgent(ChatClientAgent agent)
    : AgentBase(new OpenTelemetryAgent(agent, agent.Name ?? agent.Id) { EnableSensitiveData = true })
{
    public override string? Name => base.Name ?? this.Id;

    public static async Task<LocalAgent> CreateFromYamlAsync(
        ResponsesClient responsesClient,
        string yamlFilePath,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        Action<ChatOptions> configureOptions,
        IEnumerable<AIFunction>? functions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(responsesClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlFilePath);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(configureOptions);

        string yaml = await File.ReadAllTextAsync(yamlFilePath, cancellationToken).ConfigureAwait(false);
        List<AIFunction>? functionList = functions?.ToList();

        IChatClient chatClient = responsesClient
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation(loggerFactory, invocationClient =>
            {
                if (functionList is null)
                {
                    return;
                }

                invocationClient.AdditionalTools ??= [];

                foreach (AIFunction function in functionList)
                {
                    invocationClient.AdditionalTools.Add(function);
                }
            })
            .ConfigureOptions(configureOptions)
            .Build();

        ChatClientPromptAgentFactory agentFactory = new(
            chatClient,
            functions: functionList,
            configuration: configuration,
            loggerFactory: loggerFactory);

        AIAgent agent = await agentFactory.CreateFromYamlAsync(yaml, cancellationToken).ConfigureAwait(false);

        return agent is ChatClientAgent chatClientAgent
            ? new LocalAgent(chatClientAgent)
            : throw new InvalidOperationException($"Declarative agent '{yamlFilePath}' did not produce a {nameof(ChatClientAgent)}.");
    }
}

#pragma warning restore OPENAI001
