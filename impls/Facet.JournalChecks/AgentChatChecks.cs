using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;
using Facet.ChatPlugin;

internal static partial class Program
{
    private static T ChatAwait<T>(Task<T> task, int seconds = 150)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed.TotalSeconds < seconds) Pump(30);
        if (!task.IsCompleted) throw new TimeoutException("Chat check timed out.");
        return task.GetAwaiter().GetResult();
    }
    private static void ChatAwait(Task task, int seconds = 150) => ChatAwait(AsResult(task), seconds);
    private static async Task<bool> AsResult(Task task) { await task.ConfigureAwait(false); return true; }
    private static void CheckAgentChat(Controller controller, string qa, bool live)
    {
        using var h=new ChatHarness(qa,"chat-launcher").Start();var runtime=h.Runtime;var settings=h.Settings;var host=new LauncherHarness();
        Assert(AgentChat.IsChatInput("\"") && AgentChat.Request("\" 안녕 ") == "안녕" && !AgentChat.IsChatInput("앱"), "quote reveals chat and quote-space parses only the explicit request");
        foreach (var value in new[] { "\"", "\"안녕", "\"  ", " \" 안녕" }) {
            bool rejected = false; try { AgentChat.Request(value); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "invalid chat submission is rejected: " + value);
        }
        string id = Guid.NewGuid().ToString();
        Assert(AgentChat.ContextId("codex://threads/" + id + "?view=review") == id, "saved caller accepts a Codex thread deep link");
        var state = new CodexStreamState { TurnId = "actual" };
        var initial = JsonNode.Parse("""
            {"type":"snapshot","revision":10,"conversationState":{"requests":[],"turnHistory":{"history":{"entitiesByKey":{"bootstrap":{"turnId":"ready","params":{},"status":"completed","items":[{"type":"agentMessage","text":"READY"}]},"one":{"turnId":"actual","params":{},"status":"inProgress","items":[{"type":"agentMessage","phase":"commentary","text":"hidden"},{"type":"agentMessage","text":"안녕"}]}}}}}}
            """)!.AsObject();
        Assert(state.Apply(initial) && state.Read().Answer == "안녕" && state.Read().Running, "snapshot selects the actual turn and filters bootstrap and commentary");
        var patch = JsonNode.Parse("""
            {"type":"patches","baseRevision":10,"revision":11,"patches":[{"op":"replace","path":["turnHistory","history","entitiesByKey","one","items",1,"text"],"value":"안녕하세요 🙂"},{"op":"replace","path":["turnHistory","history","entitiesByKey","one","status"],"value":"completed"}]}
            """)!.AsObject();
        Assert(state.Apply(patch) && state.Read() == ("안녕하세요 🙂", "완료", false), "Immer patches stream Unicode text and completion");
        Assert(!state.Apply(patch), "duplicate or missing stream revisions require resynchronization");
        var parsed = CodexModel.Parse(JsonNode.Parse("""{"model":"test","displayName":"Test","defaultReasoningEffort":"low","supportedReasoningEfforts":[{"reasoningEffort":"low"},{"reasoningEffort":"high"}],"serviceTiers":[{"id":"priority","name":"Fast"}]}""")!.AsObject());
        Assert(parsed.Efforts.SequenceEqual(new[] { "low", "high" }) && parsed.FastTier == "priority", "model capabilities provide efforts and the Fast service tier");
        ChatAwait(CheckChatFramingAsync());

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void SetCapabilities(string name, object value) => typeof(CodexChatService).GetProperty(name, flags)!.SetValue(runtime.Chat, value);
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        using var session = new ChatLauncherMode(runtime, host);
        Assert(session.Matches("\"") && !session.Matches("notepad"), "the mode takes the input only after a quote");
        session.Refresh("\"", false);
        Assert(session.AgentButton.Content as string == "사용 가능한 에이전트 없음 ▾" && session.ModelButton.Visibility == Visibility.Collapsed, "unconnected quote mode hides the model choice");
        Click(session.AgentButton);
        Assert(host.Flyout is StackPanel && host.Above && ((StackPanel)host.Flyout).Children.OfType<Button>().Any(b => b.Content as string == "에이전트 연결 설정 열기"), "agent menu opens above the input with a way to the settings");
        ((StackPanel)host.Flyout!).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(h.Shell.SettingsOpened && host.Flyout == null, "the settings button opens the plugin's own section");
        // A disconnected test service exposes a synthetic catalog; it never starts an agent.
        SetCapabilities("Models", new[] { parsed, parsed with { Id = "other", Name = "Other" } });
        settings.Values[ChatSettings.ModelKey] = parsed.Id; settings.Values[ChatSettings.EffortKey] = "low";
        Click(session.ModelButton);
        var tuning = (StackPanel)host.Flyout!;
        Assert(!host.Above && tuning.Children.OfType<CheckBox>().Any(), "tuning opens below the input");
        Click(tuning.Children.OfType<Button>().Single());
        Click(((StackPanel)host.Flyout!).Children.OfType<Button>().Last());
        Assert(runtime.Settings.CodexChatModel == "other" && ((StackPanel)host.Flyout!).Children.OfType<CheckBox>().Any(), "model list selection returns to tuning and persists just the preference");
        settings.Values[ChatSettings.ClaudeEnabledKey] = "true"; settings.Values[ChatSettings.AgentKey] = "claude"; session.Refresh("\" 안녕", false);
        Assert(session.AgentButton.Content as string == "Claude Code ▾" && session.ModelButton.Content as string == "모델: 기본 ▾", "Claude Code mode shows its own agent and model");
        h.Shell.Hidden = false; ChatAwait(session.SubmitAsync("\" 숨겨지지 않는 요청"));
        Assert(host.InputText == "" && runtime.Chat.Sessions.Count == 0, "an overlay that cannot hide stops the request before any chat starts");
        h.Shell.Hidden = true;

        // The launcher hosts any plugin's mode around its unchanged centered input.
        var view = new QuickLaunchView(controller, new QuickLaunchCatalog("", ".EXE"), _ => throw new Exception("Chat must not launch a shell."));
        var input = (TextBox)view.FindName("Input"); var frame = (Border)((Grid)view.Content).Children[0];
        void Layout() { view.Measure(new(1280, 800)); view.Arrange(new(0, 0, 1280, 800)); view.UpdateLayout(); }
        var flyout = (Border)view.FindName("LauncherFlyout");
        view.Begin(); input.Text = "\""; Layout();
        Assert(Math.Abs(frame.TranslatePoint(new(), view).Y + frame.ActualHeight / 2 - 400) < 1, "the launcher input stays centered");
        var anchor = new Button { Content = "앵커", Width = 80, Height = 30 }; ((Grid)view.Content).Children.Add(anchor); Grid.SetRow(anchor, 0); Layout();
        view.ShowFlyout(anchor, new TextBlock { Text = "메뉴" }, true); Layout();
        Assert(flyout.Visibility == Visibility.Visible, "a plugin can open the launcher flyout");
        Assert(view.DismissFlyoutOutside(input) && flyout.Visibility == Visibility.Collapsed, "outside click closes only the flyout");
        view.ShowFlyout(anchor, new TextBlock { Text = "메뉴" }); Layout();
        Assert(view.HandleKey(Key.Escape, ModifierKeys.None) && flyout.Visibility == Visibility.Collapsed, "Escape closes the flyout first");
        input.Text = "notepad"; view.End();
        SetCapabilities("Models", Array.Empty<CodexModel>());
        if (live) CheckLiveAgentChat(qa);
    }

    private sealed class LauncherHarness : Facet.Plugins.ILauncherHost
    {
        internal FrameworkElement? Flyout; internal bool Above;
        public string InputText { get; set; } = "";
        public void FocusInput() { }
        public void ShowFlyout(FrameworkElement anchor, FrameworkElement content, bool above = false) { Flyout = content; Above = above; }
        public void CloseFlyout() => Flyout = null;
    }
    private static async Task CheckChatFramingAsync()
    {
        string name = "facet-chat-check-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var accepting = server.WaitForConnectionAsync(cancel.Token);
        using var channel = await CodexJsonChannel.PipeAsync(name, cancel.Token); await accepting;
        async Task<JsonObject> Read() { byte[] header = new byte[4]; await server.ReadExactlyAsync(header, cancel.Token); byte[] bytes = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)]; await server.ReadExactlyAsync(bytes, cancel.Token); return JsonNode.Parse(bytes)!.AsObject(); }
        async Task Write(JsonObject value) { byte[] bytes = Encoding.UTF8.GetBytes(value.ToJsonString()); byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length); await server.WriteAsync(header, cancel.Token); foreach (byte b in bytes) await server.WriteAsync(new[] { b }, cancel.Token); await server.FlushAsync(cancel.Token); }
        int discoveries = 0; channel.Message += value => { if (value["type"]?.ToString() == "client-discovery-request") Interlocked.Increment(ref discoveries); };
        var first = channel.RequestEnvelopeAsync("first", new() { ["requestId"] = "first", ["text"] = "한글 🙂" }, cancel.Token);
        var request = await Read();
        await Write(new() { ["type"] = "client-discovery-request", ["requestId"] = "first" });
        var second = channel.RequestEnvelopeAsync("second", new() { ["requestId"] = "second" }, cancel.Token); await Read();
        await Write(new() { ["type"] = "response", ["requestId"] = "second", ["result"] = new JsonObject { ["text"] = "둘" } });
        await Write(new() { ["type"] = "response", ["requestId"] = "first", ["result"] = new JsonObject { ["text"] = "하나🙂" } });
        Assert((await first)["result"]?["text"]?.ToString() == "하나🙂" && (await second)["result"]?["text"]?.ToString() == "둘" && discoveries == 1 && request["text"]?.ToString() == "한글 🙂", "duplex framing handles fragmented UTF8, discovery, and out-of-order responses");
        var pending = channel.RequestEnvelopeAsync("disconnect", new() { ["requestId"] = "disconnect" }, cancel.Token); await Read(); server.Dispose();
        bool disconnected = false; try { await pending; } catch (IOException) { disconnected = true; }
        Assert(disconnected, "pipe disconnect fails pending requests without retry");
    }
    // Live check (--codex-chat-live): a real projectless Codex chat through the plugin, then deleted again.
    private static void CheckLiveAgentChat(string qa)
    {
        using var h = new ChatHarness(qa, "chat-live").Start(); var runtime = h.Runtime;
        h.Settings.Values[ChatSettings.CodexEnabledKey] = "true";
        ChatAwait(runtime.Chat.ConnectAsync());
        Assert(runtime.Chat.IsConnected && runtime.Chat.Models.Count > 0, "live MCP and shared IPC discover models without an inherited pipe");
        var model = runtime.Chat.Models.First(x => x.FastTier != null);
        h.Settings.Values[ChatSettings.ModelKey] = model.Id; h.Settings.Values[ChatSettings.EffortKey] = model.Efforts.First(); h.Settings.Values[ChatSettings.FastKey] = "true";
        var growth = new HashSet<int>(); runtime.Chat.Changed += () => { lock (growth) foreach (var s in runtime.Chat.Sessions) if (s.Answer.Length > 0) growth.Add(s.Answer.Length); };
        string prompt = "This is a Facet launcher integration check. Do not use tools, access files, edit settings, or delegate. Reply in Korean with 18 numbered one-sentence examples of polite greetings. Put CHAT_STREAM_OK at the end.";
        CodexChatSession? session = null;
        try {
            session = ChatAwait(runtime.LaunchChatAsync(prompt, model.Id, model.Efforts.First(), true));
            var notification = runtime.ChatToasts.Single();
            var timer = Stopwatch.StartNew(); while (session.Running && timer.Elapsed.TotalSeconds < 120) Pump(100);
            Assert(!session.Running && session.Answer.Contains("CHAT_STREAM_OK"), "live projectless app chat finishes the actual streamed user request");
            Assert(session.Stream.TurnParameters?["serviceTier"]?.ToString() == "priority" && session.Stream.TurnParameters?["model"]?.ToString() == model.Id, "app owner receives the selected model, effort, and Fast priority tier");
            lock (growth) Assert(growth.Count > 1, "live answer arrives in multiple incremental text updates");
            Assert(h.Store.Text!.Contains(session.Reference.ThreadId) && !h.Store.Text.Contains("CHAT_STREAM_OK"), "the store holds a reference and no answer transcript");
            notification.Refresh(); Assert(notification.Terminal, "live completion marks the retained toast terminal");
            var view = runtime.CreateView(); view.Select(session, false);
            view.Composer.Text = "Do not use tools or modify any files. Reply with Markdown heading FOLLOWUP_OK and say whether your previous answer ended with CHAT_STREAM_OK.";
            ChatAwait(view.SendAsync()); timer.Restart(); while (session.Running && timer.Elapsed.TotalSeconds < 100) Pump(100);
            Assert(session.Answer.Contains("FOLLOWUP_OK") && session.Conversation.Count == 2, "the chat widget sends a follow-up in the same agent-owned conversation");
            using var restored = new CodexChatService(runtime);
            ChatAwait(restored.ConnectAsync()); var saved = restored.Sessions.Single(x => x.Reference.ThreadId == session.Reference.ThreadId); ChatAwait(restored.OpenSessionAsync(saved));
            Assert(saved.Conversation.Count == 2, "a fresh chat service reloads both turns using only saved references");
            notification.DismissButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert(runtime.ChatToasts.Count == 0, "closing the live toast removes it");
            File.WriteAllText(Path.Combine(qa, "chat-live-result.txt"), "PASS projectless Fast chat, multiple streamed updates, and agent-owned history reload.\n");
        } finally {
            // Clean up only the chat created by this check.
            if (session != null) {
                var connected = ChatAwait(CodexAppConnection.ConnectAsync(h.Settings.Get(ChatSettings.ContextKey), Path.Combine(qa, "cleanup-runtime"), CancellationToken.None));
                using var connection = connected.Connection;
                ChatAwait(connection.ToolAsync("set_thread_archived", new() { ["threadId"] = session.Reference.ThreadId, ["hostId"] = "local", ["archived"] = true }, CancellationToken.None));
                var info = new ProcessStartInfo(CodexRuntime.Discover().Cli) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string arg in new[] { "delete", AgentChat.ContextId(session.Reference.ThreadId), "--force" }) info.ArgumentList.Add(arg);
                using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                ChatAwait(process.WaitForExitAsync()); ChatAwait(output); string errors = ChatAwait(error);
                Assert(process.ExitCode == 0, "temporary live integration chat is deleted: " + errors);
            }
        }
    }
}
