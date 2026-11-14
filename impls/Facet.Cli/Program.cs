using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.RegularExpressions;
using Facet.Core;
using Facet.Deployment;
using Facet.Integration;

if (args.Length == 0 || args[0] is "help" or "--help") {
    Console.WriteLine("""
Facet.Cli add --title "제목" --at "2026-10-24T18:00:00+09:00" --key "agent-request-id" [--body "설명"] [--kind schedule|reminder]
Facet.Cli list
Facet.Cli complete --id <guid>
Facet.Cli cancel --id <guid>
공통: --data-dir <경로>  --start (앱이 없으면 고정 실행기로 트레이 시작)
결과: stdout JSON, 오류: stderr JSON. 같은 key는 재시도해도 중복 추가하지 않습니다.
"""); return 0;
}

void Print(ReminderReply reply) => (reply.Ok ? Console.Out : Console.Error).WriteLine(JsonSerializer.Serialize(reply, ReminderProtocol.Json));
try {
    var command = new ReminderCommand { Action = args[0] };
    var options = new Dictionary<string, string>(StringComparer.Ordinal); bool start = false;
    for (int index = 1; index < args.Length; index++) {
        if (args[index] == "--start") { start = true; continue; }
        if (args[index] is not ("--title" or "--body" or "--at" or "--kind" or "--key" or "--id" or "--data-dir") || index + 1 >= args.Length || !options.TryAdd(args[index], args[index + 1]))
            throw new ArgumentException("알 수 없거나 중복된 옵션입니다.");
        index++;
    }
    string Value(string key, string fallback = "") => options.GetValueOrDefault(key, fallback);
    command.Key = Value("--key", Guid.NewGuid().ToString("N")); command.Title = Value("--title"); command.Body = Value("--body"); command.Kind = Value("--kind", "reminder");
    if (command.Action == "add") {
        string at = Value("--at");
        if (!Regex.IsMatch(at, @"(Z|[+-]\d{2}:\d{2})$") || !DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ArgumentException("--at에는 시간대가 있는 ISO 8601 시각이 필요합니다. 예: 2026-10-24T18:00:00+09:00");
        command.At = parsed;
        UserReminders.Apply(new AppData(), command);
    }
    else if (command.Action is "complete" or "cancel") command.Id = Guid.Parse(Value("--id"));
    else if (command.Action != "list") throw new ArgumentException("add/list/complete/cancel 명령만 지원합니다.");

    string root = LaunchProtocol.DataRoot(args);
    using var pipe = new NamedPipeClientStream(".", ReminderProtocol.PipeName(root), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    try { await pipe.ConnectAsync(1200); }
    catch (TimeoutException) when (start) {
        string launcher = Path.Combine(AppContext.BaseDirectory, LaunchProtocol.LauncherName);
        if (!File.Exists(launcher)) launcher = LaunchProtocol.FindLauncher() ?? throw new IOException("고정 실행기를 찾지 못했습니다. 앱을 먼저 실행하세요.");
        var process = new ProcessStartInfo(launcher) { UseShellExecute = false, CreateNoWindow = true };
        process.ArgumentList.Add("--tray"); process.ArgumentList.Add("--data-dir"); process.ArgumentList.Add(root);
        using var child = Process.Start(process);
        await pipe.ConnectAsync(10000);
    }
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    await ReminderProtocol.WriteAsync(pipe, command, 16384, timeout.Token);
    var result = await ReminderProtocol.ReadAsync<ReminderReply>(pipe, 8 * 1024 * 1024, timeout.Token);
    Print(result); return result.Ok ? 0 : 1;
}
catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException) { Print(new(false, "invalid_request", ex.Message)); return 2; }
catch (TimeoutException) { Print(new(false, "not_running", "앱이 실행 중이지 않거나 응답하지 않습니다. 앱을 열거나 --start를 사용하세요.")); return 3; }
catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { Print(new(false, "connection_failed", ex.Message)); return 4; }
