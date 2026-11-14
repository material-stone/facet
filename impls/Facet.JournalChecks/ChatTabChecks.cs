using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet.ChatPlugin;

internal static partial class Program
{
    private static void CheckLiveChatMetadata(string qa)
    {
        using var h=new ChatHarness(qa,"chat-live-metadata").Start();
        h.Settings.Values[ChatSettings.CodexEnabledKey]="true";
        ChatAwait(h.Runtime.Chat.ConnectAsync()); ChatAwait(h.Runtime.Chat.RefreshSessionStatesAsync());
        Assert(h.Runtime.Chat.IsConnected, "read-only production MCP refresh respects the 50-thread metadata limit");
        var view = h.Runtime.CreateView(); view.Refresh();
        CaptureChatVisual(view, 1200, 680, Path.Combine(qa, "chat-connected-metadata.png"));
    }
    private static void CheckChatTab(string qa)
    {
        using var h=new ChatHarness(qa,"chat-tab").Start();var runtime=h.Runtime;
        var user = JsonNode.Parse("""{"id":"t1","status":"completed","items":[{"type":"userMessage","content":[{"type":"text","text":"안녕\n다음 줄"}]},{"type":"agentMessage","phase":"commentary","text":"준비"},{"type":"agentMessage","phase":"final_answer","text":"## 답변\n**안녕하세요**"}]}""")!.AsObject();
        var turn = CodexConversation.Parse(user);
        Assert(turn.User == "안녕\n다음 줄" && turn.Answer == "## 답변\n**안녕하세요**", "chat history normalizes user content and Markdown while excluding commentary");
        var ready = turn with { Id = "ready", User = CodexConversation.ReadinessPrompt, Answer = "READY" };
        Assert(CodexConversation.IsReadiness(ready) && !CodexConversation.IsReadiness(turn with { User = "READY" }), "only the exact readiness request is hidden from chat history");
        var failedPreparation = new CodexChatSession(new()) { Status = "요청 전송 전 실패", PendingRequest = "전송되지 않은 요청" };
        failedPreparation.Stream.Apply(new() { ["type"] = "snapshot", ["revision"] = 1L, ["conversationState"] = new JsonObject { ["turn"] = new JsonObject { ["turnId"] = "ready", ["status"] = "completed", ["params"] = new JsonObject { ["input"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = CodexConversation.ReadinessPrompt }) }, ["items"] = new JsonArray(new JsonObject { ["type"] = "agentMessage", ["text"] = "READY" }) } } });
        CodexChatService.UpdateSession(failedPreparation);
        Assert(failedPreparation.Status == "요청 전송 전 실패" && failedPreparation.Answer.Length == 0, "a readiness completion cannot turn an unsent request failure into success");
        var session = new CodexChatSession(new() { ThreadId = Guid.NewGuid().ToString(), Title = "일반 대화" });
        session.ReadinessTurnId = "ready";
        CodexChatService.MergeTurns(session, new[] { ready with { User = "" }, turn with { Id = "literal-ready", User = "READY", Answer = "READY" } });
        Assert(session.Conversation.Count == 1 && session.Conversation[0].Id == "literal-ready", "known readiness IDs filter missing bootstrap user items while preserving genuine READY messages");
        session.Conversation = [];
        CodexChatService.MergeTurns(session, new[] { ready, turn });
        CodexChatService.MergeTurns(session, new[] { turn with { Answer = "# 갱신된 답변" }, turn with { Id = "t2", User = "추가 질문", Answer = "- 하나\n- 둘" } });
        CodexChatService.MergeTurns(session, new[] { turn with { Id = "old", User = "이전 대화" }, turn }, true);
        Assert(session.Conversation.Select(t => t.Id).Distinct().Count() == 3 && session.Conversation.Count == 3, "stream and history merge by turn identity without duplicated messages");
        // A previous page prepends without replacing newer streaming text.
        CodexChatService.MergeTurns(session, new[] { turn with { Id = "older", User = "더 이전 대화" } }, true);
        Assert(session.Conversation.First().Id == "older" && !session.Conversation.Any(CodexConversation.IsReadiness), "older pages preserve ordering and omit bootstrap turns");
        var projectSession = new CodexChatSession(new() { ThreadId = Guid.NewGuid().ToString(), Title = "프로젝트 작업", ProjectId = "fixture-project" }) { Running = true, Status = "작업 중…", PendingRequest = "프로젝트 요청을 실행해 주세요" };
        int opened = 0;
        var toast = new ChatTaskToast("작업 프롬프트", _ => opened++);
        toast.Bind(projectSession);
        Assert(toast.DismissButton.Visibility == Visibility.Collapsed && !toast.Terminal && !toast.ShowActivated && !toast.ShowInTaskbar, "working toast is passive and hides its terminal close action");
        toast.OpenButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(opened == 1 && toast.OpenRequested, "toast body routes to the matching conversation");
        projectSession.Running = false; projectSession.Status = "완료"; toast.Refresh();
        Assert(toast.Terminal && toast.DismissButton.Visibility == Visibility.Visible, "completed toast remains and reveals the rightmost close action");
        CaptureChatVisual(toast.Content as FrameworkElement ?? throw new Exception("toast content"), 560, 168, Path.Combine(qa, "chat-task-toast.png"));
        toast.DismissButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(opened == 1 && !projectSession.Running, "toast close neither opens a conversation nor changes its work state");
        var privateToast = new ChatTaskToast("PRIVATE_PROMPT", _ => {}, true);
        privateToast.Update("요청 실패 · PRIVATE_DETAIL", true);
        Assert(!System.Windows.Automation.AutomationProperties.GetName(privateToast.OpenButton).Contains("PRIVATE_"), "screen-sharing mode hides task prompts and failure details from toast text and accessibility");
        privateToast.SetPrivacy(false);
        Assert(System.Windows.Automation.AutomationProperties.GetName(privateToast.OpenButton).Contains("PRIVATE_PROMPT"), "turning screen-sharing mode off restores the original task notification");
        privateToast.Close();

        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var sessions = (List<CodexChatSession>)typeof(CodexChatService).GetField("sessions", flags)!.GetValue(runtime.Chat)!;
        var originals = sessions.ToArray(); sessions.Clear(); sessions.Add(session); sessions.Add(projectSession);
        var oldModels = runtime.Chat.Models;
        var model = new CodexModel("synthetic", "테스트 모델", "low", new[] { "low", "high" }, "priority");
        typeof(CodexChatService).GetProperty("Models", flags)!.SetValue(runtime.Chat, new[] { model });
        h.Settings.Values[ChatSettings.ModelKey] = model.Id; h.Settings.Values[ChatSettings.EffortKey] = "low";
        try {
            var view = runtime.CreateView(); view.Select(session, false); view.Refresh();
            var root = (Grid)view.Content; var left = (Grid)((Border)root.Children[0]).Child; var groupPanel = (StackPanel)((ScrollViewer)left.Children[1]).Content;
            Assert(!groupPanel.Children.OfType<Expander>().Any() && groupPanel.Children.OfType<Button>().Count() == 2, "chat sidebar lists every chat in one list without project groups");
            var right = (Grid)((Border)root.Children[1]).Child;
            var transcript = (StackPanel)((ScrollViewer)right.Children[1]).Content;
            var messagePanel = (StackPanel)transcript.Children[1];
            Assert(messagePanel.Children.OfType<Border>().Any(b => b.HorizontalAlignment == HorizontalAlignment.Right) && messagePanel.Children.OfType<Border>().Any(b => b.Child is StackPanel), "chat displays right-aligned user bubbles and agent Markdown blocks");
            view.Composer.Text = "미전송 초안\n다음 줄"; view.Select(projectSession, false); view.Select(session, false);
            Assert(view.Composer.Text == "미전송 초안\n다음 줄", "switching sessions preserves each in-memory multiline draft");
            Assert(view.Composer.AcceptsReturn && !view.HandleComposerKey(Key.Enter, ModifierKeys.Shift), "Shift Enter is left to the multiline editor for line breaks");
            typeof(OverlayChatView).GetField("composing",flags)!.SetValue(view,true);
            Assert(!view.HandleComposerKey(Key.Enter,ModifierKeys.None),"IME composition Enter is never treated as a send action");
            typeof(OverlayChatView).GetField("composing",flags)!.SetValue(view,false);
            view.Composer.Clear(); Assert(view.HandleComposerKey(Key.Enter, ModifierKeys.None), "plain Enter is routed to send with validation");
            view.ModelPicker.SelectedValue = model.Id; view.EffortPicker.SelectedItem = "high";
            Assert(runtime.Settings.CodexChatEffort == "high", "chat effort selection persists only a setting");
            view.Select(projectSession, false); projectSession.Running = true; projectSession.Status = "답변 생성 중…"; view.Refresh();
            var statuses = (Dictionary<string, (Button Button, TextBlock State)>)typeof(OverlayChatView).GetField("rows", flags)!.GetValue(view)!;
            Assert(statuses[projectSession.Reference.ThreadId].State.Text.Contains("●") && statuses[projectSession.Reference.ThreadId].State.Text.Contains("답변 생성 중"), "each session row shows its own live work state");
            view.Select(session, false); view.Refresh(); CaptureChatVisual(view, 1200, 680, Path.Combine(qa, "overlay-chat-conversation.png"));
            Assert(!(h.Store.Text ?? "").Contains("미전송 초안") && !JsonSerializer.Serialize(h.Settings.Values).Contains("미전송 초안"), "chat drafts and loaded transcripts are not persisted in plugin records");
        } finally {
            sessions.Clear(); sessions.AddRange(originals);
            typeof(CodexChatService).GetProperty("Models", flags)!.SetValue(runtime.Chat, oldModels);
        }
    }
    private static void CaptureChatVisual(FrameworkElement view, int width, int height, string path)
    {
        view.Measure(new(width, height)); view.Arrange(new(0, 0, width, height)); view.UpdateLayout();
        var drawing = new DrawingVisual(); using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(view) { AutoLayoutContent = false }, null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); png.Save(stream);
    }
}
