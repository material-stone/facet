using System.Net.Http;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Facet.Integration;

namespace Facet;

// Serves AgentToolCatalog calls from Facet.Mcp over a current-user pipe; every call runs on the UI dispatcher.
internal sealed class AgentToolHost : IDisposable
{
    private readonly Controller app;
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private bool disposed;

    internal AgentToolHost(Controller app)
    {
        this.app = app;
        string name = AgentToolProtocol.PipeName(app.Store.Root);
        worker = Task.Run(async () => {
            while (!stop.IsCancellationRequested) {
                try {
                    using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop.Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    AgentToolReply reply;
                    try {
                        var call = await ReminderProtocol.ReadAsync<AgentToolCall>(pipe, AgentToolProtocol.RequestLimit, timeout.Token);
                        reply = await await app.Dispatcher.InvokeAsync(() => HandleAsync(call), DispatcherPriority.Normal, timeout.Token).Task;
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or JsonException or InvalidOperationException or FormatException) {
                        reply = new(false, "invalid_request", ex.Message);
                    }
                    await ReminderProtocol.WriteAsync(pipe, reply, AgentToolProtocol.ReplyLimit, timeout.Token);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { if (!stop.IsCancellationRequested) await Task.Delay(100); }
            }
        });
    }

    internal async Task<AgentToolReply> HandleAsync(AgentToolCall call)
    {
        if (app.Exiting || app.ExitPending) return new(false, "busy", "앱이 종료 중입니다.");
        var args = call.Arguments; var now = DateTimeOffset.Now;
        try {
            JsonNode result = call.Tool switch {
                "facet_today" => Today(now),
                "facet_list_schedules" => AgentToolCatalog.Schedules(app.Data, AgentToolCatalog.Date(args, "date", DateOnly.FromDateTime(now.DateTime))),
                "facet_add_schedule" => app.Change(d => AgentToolCatalog.Schedule(AgentToolCatalog.AddSchedule(d, args, now.DateTime), AgentToolCatalog.Date(args, "date", DateOnly.FromDateTime(now.DateTime)))),
                "facet_delete_schedule" => app.Change(d => { AgentToolCatalog.DeleteSchedule(d, args, now.DateTime); return (JsonNode)"삭제했습니다."; }),
                "facet_add_reminder" => Reminder(args),
                AgentToolProtocol.ListPluginTools => new JsonArray(app.Plugins.AgentTools.Select(t => (JsonNode)new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = t.InputSchema }).ToArray()),
                "facet_weather" => await WeatherAsync(),
                _ => await PluginToolAsync(call),
            };
            return new(true, "ok", "", result);
        }
        catch (ArgumentException ex) { return new(false, "invalid_request", ex.Message); }
        // Plugin code can throw anything; only the plugin tools get this far with other types.
        catch (PluginToolException ex) { return new(false, "failed", ex.Message); }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException) { return new(false, "failed", ex.Message); }
    }

    private JsonObject Today(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        return new() {
            ["now"] = now.ToString("yyyy-MM-dd'T'HH:mm:sszzz"), ["weekday"] = now.ToString("dddd", new System.Globalization.CultureInfo("ko-KR")),
            ["schedules"] = AgentToolCatalog.Schedules(app.Data, today),
            ["agenda"] = new JsonArray(DashboardAgenda.Today(app.Data, now).Select(x => (JsonNode)new JsonObject { ["title"] = x.Title, ["label"] = x.Label, ["at"] = x.At?.ToString("HH:mm") }).ToArray()) };
    }

    private JsonNode Reminder(JsonObject? args)
    {
        var command = new ReminderCommand { Action = "add", Title = AgentToolCatalog.Text(args, "title", 200), Body = AgentToolCatalog.Text(args, "body", 2000),
            At = AgentToolCatalog.Instant(AgentToolCatalog.Text(args, "at", 40), "at"), Key = "agent-" + Guid.NewGuid().ToString("N") };
        var reply = app.HandleReminderCommand(command);
        if (!reply.Ok) throw new ArgumentException(reply.Message);
        return new JsonObject { ["id"] = reply.Item?.Id.ToString(), ["title"] = reply.Item?.Title, ["at"] = reply.Item?.At.ToString("o") };
    }

    private sealed class PluginToolException(string message) : Exception(message);

    private async Task<JsonNode> PluginToolAsync(AgentToolCall call)
    {
        var tool = app.Plugins.AgentTools.FirstOrDefault(t => t.Name == call.Tool) ?? throw new ArgumentException("알 수 없는 도구입니다: " + call.Tool);
        try {
            string result = await tool.Run(call.Arguments?.ToJsonString() ?? "{}");
            return JsonNode.Parse(result) ?? "ok";
        }
        catch (ArgumentException) { throw; }
        catch (Exception ex) { throw new PluginToolException(ex.Message); }
    }

    private async Task<JsonNode> WeatherAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var weather = await new WeatherClient(locate: token => WeatherRegions.ResolveAsync(PluginData.WeatherRegion(app.Data), token)).FetchAsync(timeout.Token);
        return new JsonObject { ["area"] = weather.Location.Area, ["description"] = weather.Description, ["temperature_c"] = weather.Temperature,
            ["humidity"] = weather.Humidity, ["observed_at"] = weather.ObservedAt.ToString("o") };
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true; stop.Cancel();
        _ = worker.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
