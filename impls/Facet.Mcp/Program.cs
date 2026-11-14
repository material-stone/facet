using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Facet.Core;
using Facet.Deployment;
using Facet.Integration;

// Stdio MCP server (newline-delimited JSON-RPC). It holds no data: every tool call is forwarded to the running app (ADR 0069).
string root = LaunchProtocol.DataRoot(args);
var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
using var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
var jsonOptions = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
const string Instructions = "Facet은 사용자의 Windows 데스크톱 비서 앱입니다. 일정·알림 등록, 날씨, 컨디션·수면 기록, 지난 대화 검색은 facet_* 도구로 처리하세요. 시각은 사용자의 현지 시간대(facet_today의 now)를 기준으로 합니다.";

while (await stdin.ReadLineAsync() is { } line) {
    if (line.Trim().Length == 0) continue;
    JsonObject? message;
    try { message = JsonNode.Parse(line) as JsonObject; } catch (JsonException) { Send(Error(null, -32700, "Parse error")); continue; }
    if (message == null) continue;
    var id = message["id"]?.DeepClone(); string method = message["method"]?.ToString() ?? "";
    if (id == null) continue; // Notifications (initialized, cancelled) need no reply.
    try {
        JsonNode result = method switch {
            "initialize" => new JsonObject {
                ["protocolVersion"] = message["params"]?["protocolVersion"]?.ToString() ?? "2025-06-18",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "facet", ["version"] = "1.0.0" },
                ["instructions"] = Instructions },
            "ping" => new JsonObject(),
            "tools/list" => new JsonObject { ["tools"] = new JsonArray(AgentToolCatalog.Tools.Concat(await PluginToolsAsync()).Select(t => (JsonNode)new JsonObject {
                ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = JsonNode.Parse(t.InputSchema) }).ToArray()) },
            "tools/call" => await CallAsync(message["params"] as JsonObject),
            _ => throw new MissingMethodException(method),
        };
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
    }
    catch (MissingMethodException) { Send(Error(id, -32601, "Method not found: " + method)); }
}
return 0;

async Task<JsonNode> CallAsync(JsonObject? p)
{
    var call = new AgentToolCall { Tool = p?["name"]?.ToString() ?? "", Arguments = p?["arguments"]?.DeepClone() as JsonObject };
    AgentToolReply reply;
    try { reply = await ForwardAsync(call); }
    catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or InvalidDataException or JsonException) {
        reply = new(false, "not_running", "Facet 앱에 연결하지 못했습니다. 앱을 실행한 뒤 다시 시도하세요. (" + ex.Message + ")");
    }
    string text = reply.Ok ? reply.Result is JsonValue v && v.TryGetValue<string>(out var s) ? s : reply.Result?.ToJsonString(jsonOptions) ?? "ok" : reply.Code + ": " + reply.Message;
    return new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = !reply.Ok };
}

// Plugin tools exist only in a running app. Listing never starts the app, so they appear when it was already running.
async Task<IReadOnlyList<AgentToolDefinition>> PluginToolsAsync()
{
    try {
        using var pipe = new NamedPipeClientStream(".", AgentToolProtocol.PipeName(root), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(500);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ReminderProtocol.WriteAsync(pipe, new AgentToolCall { Tool = AgentToolProtocol.ListPluginTools }, AgentToolProtocol.RequestLimit, timeout.Token);
        var reply = await ReminderProtocol.ReadAsync<AgentToolReply>(pipe, AgentToolProtocol.ReplyLimit, timeout.Token);
        return reply.Result is JsonArray tools ? tools.Select(t => new AgentToolDefinition(t!["name"]!.ToString(), t["description"]!.ToString(), t["inputSchema"]!.ToString())).ToArray() : [];
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or InvalidDataException or JsonException) { return []; }
}

async Task<AgentToolReply> ForwardAsync(AgentToolCall call)
{
    using var pipe = new NamedPipeClientStream(".", AgentToolProtocol.PipeName(root), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    try { await pipe.ConnectAsync(1500); }
    catch (TimeoutException) {
        // Start the pinned launcher in the tray, like Facet.Cli --start.
        string launcher = Path.Combine(AppContext.BaseDirectory, LaunchProtocol.LauncherName);
        if (!File.Exists(launcher)) launcher = LaunchProtocol.FindLauncher() ?? throw new IOException("고정 실행기를 찾지 못했습니다.");
        // Shell execute avoids inheriting this server's stdio pipes into the long-lived app.
        var start = new ProcessStartInfo(launcher) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--tray"); start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(root);
        using (Process.Start(start)) { }
        await pipe.ConnectAsync(15000);
    }
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
    await ReminderProtocol.WriteAsync(pipe, call, AgentToolProtocol.RequestLimit, timeout.Token);
    return await ReminderProtocol.ReadAsync<AgentToolReply>(pipe, AgentToolProtocol.ReplyLimit, timeout.Token);
}

void Send(JsonObject value) => stdout.WriteLine(value.ToJsonString(jsonOptions));
JsonObject Error(JsonNode? id, int code, string text) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = text } };
