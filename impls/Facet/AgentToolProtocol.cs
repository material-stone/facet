using Facet.Deployment;

namespace Facet.Integration;

// Agent tool calls reuse ReminderProtocol framing on a separate current-user pipe (ADR 0069).
internal static class AgentToolProtocol
{
    internal const int RequestLimit = 65536, ReplyLimit = 4 * 1024 * 1024;
    // A reserved call for Facet.Mcp's tools/list; its dots keep it out of the tool name space.
    internal const string ListPluginTools = "facet.list-plugin-tools";
    internal static string PipeName(string root) => "Facet-agent-" + LaunchProtocol.Hash(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)));
}
