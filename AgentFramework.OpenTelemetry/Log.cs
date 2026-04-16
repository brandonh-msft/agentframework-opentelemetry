
namespace AgentFramework.OpenTelemetry
{
#pragma warning disable CS8019
    using Microsoft.Extensions.Logging;

    using System;
#pragma warning restore CS8019

    static partial class Log
    {

        [LoggerMessage(0, LogLevel.Warning, "Got non-MCP tool call content in CreateApprovalResponses: {ToolCallContentType}")]
        internal static partial void GotNonMCPToolCallContentInCreateApprovalResponsesToolCallContentType(this ILogger logger, string ToolCallContentType);
    }
}
