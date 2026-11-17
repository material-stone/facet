using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Facet;
using Facet.Core;
using Facet.Design;
using Facet.Plugins;

// The skin widget layer (ADR 0110): manifest slot, coordinates, messages and action routing.
internal static partial class Program
{
    private sealed class FakeState(string type = "test.plugin/thing@1") : IWidgetState
    {
        public int Calls;
        public string Type => type;
        public string Json() => "{\"n\":1}";
        public event Action? Changed { add { } remove { } }
        public Task<string?> Act(string name, string argsJson)
        {
            Calls++;
            if (name == "boom") throw new ArgumentException("bad");
            return Task.FromResult<string?>(argsJson);
        }
    }

    private static void CheckSkinWidgets(Controller controller, string qa)
    {
        const string world = "\"world\":{\"page\":\"w.html\",\"scenes\":[\"room\"]}";
        Assert(ThemeManifest.Parse("{" + world + ",\"widgets\":[\"facet.clock/time@1\",\"facet.app/tabs@1\"]}") is { Widgets: ["facet.clock/time@1", "facet.app/tabs@1"] }, "a world skin lists the widget state types its page draws");
        Assert(ThemeManifest.Parse("{\"widgets\":[\"facet.clock/time@1\"]}") == null, "a widget list without a world page is refused");
        Assert(new[] { "Facet.clock/time@1", "facet.clock/time", "facet.clock/time@0", "../x/y@1", "facet.clock/ti me@1", "facet.clock@1" }.All(bad => ThemeManifest.Parse("{" + world + ",\"widgets\":[\"" + bad + "\"]}") == null), "widget types follow <pluginId>/<widget>@<version>");
        Assert(ThemeManifest.Parse("{" + world + ",\"widgets\":[\"a/b@1\",\"a/b@1\"]}") == null && ThemeManifest.Parse("{" + world + ",\"widgets\":[" + string.Join(",", Enumerable.Range(0, ThemeManifest.MaxWidgetTypes + 1).Select(i => "\"a/w" + i + "@1\"")) + "]}") == null, "widget types are listed once each and at most " + ThemeManifest.MaxWidgetTypes);
        Assert(ThemeManifest.Parse("{" + world + "}") is { Widgets: null } && Skins.LoadVisuals("catroom") is { WidgetTypes.Count: 9, SoftHud: true } catroom && catroom.WidgetTypes.Contains("facet.app/tabs@1") && catroom.WidgetTypes.Contains("facet.sleep/week@1"), "a skin without a widget list draws no widgets and the cat room draws every first-party widget type on its page");
        Assert(!WidgetTheme.HudKey.Contains("Cat") && typeof(WidgetTheme).GetProperty("IsCatHud") is { } catHud && catHud.GetValue(null) is false, "the cat form is inert but kept for older plugins");

        Assert(SkinWidgets.BoardToView(10, 20, 100, 50, 800, 600, 30, -40, 2) == new Rect(450, 300, 200, 100), "board pixels follow the view's pan and zoom");
        Assert(SkinWidgets.ToBase(new Point(50, 25), new Size(100, 50), 200, 100) == new Point(100, 50), "pointer positions are scaled to the widget's base size");
        var board = new OverlayWorkspaceView("skin-check") { CreateView = _ => new Border(), BaseSize = _ => (200, 100), SkinDrawn = (_, view) => view is Border };
        board.Measure(new Size(800, 600)); board.Arrange(new Rect(0, 0, 800, 600));
        board.Render(new WorkspaceTab { Id = "skin-check", Title = "t", Widgets = [new() { Id = "w1", WidgetId = "x", X = 10, Y = 20, Width = 100, Height = 50 }] }, false);
        board.PanTo(30, -40); board.ZoomAt(new Point(400, 300), 2);
        var tile = board.SkinTiles().Single();
        Assert(board.Zoom == 2 && tile.Id == "w1" && tile.Bounds == SkinWidgets.BoardToView(10, 20, 100, 50, 800, 600, board.ViewX, board.ViewY, 2) && tile.BaseWidth == 200 && tile.BaseHeight == 100, "a skin tile reports its place after pan and zoom with its base size");
        var skinTile = board.TileForCheck("w1")!;
        Assert(board.IsSkinTile("w1") && ((Grid)skinTile).Children.OfType<WidgetStretch>().Single().Visibility == Visibility.Hidden && ((Grid)skinTile).Children.OfType<Border>().Any(catcher => catcher.Background == System.Windows.Media.Brushes.Transparent), "a skin-drawn tile hides its view and keeps a transparent hit surface");
        board.Render(new WorkspaceTab { Id = "skin-check", Title = "t", Widgets = [new() { Id = "w1", WidgetId = "x", X = 10, Y = 20, Width = 100, Height = 50 }] }, true);
        Assert(board.SkinTiles().Count() == 1 && ((Grid)board.TileForCheck("w1")!).Children.OfType<Border>().Any(cover => cover.Cursor == System.Windows.Input.Cursors.SizeAll), "editing a skin tile shows the app's edit frame");

        using (var message = JsonDocument.Parse(SkinWidgets.WidgetsMessage([new("w1", "a/b@1", new Rect(1.04, 2, 3, 4), 200, 100, "{\"k\":true}"), new("w2", "a/b@1", new Rect(0, 0, 1, 1), 1, 1, "[1]")]))) {
            var first = message.RootElement.GetProperty("widgets")[0];
            Assert(new[] { "id", "type", "x", "y", "width", "height", "baseWidth", "baseHeight", "state" }.All(key => first.TryGetProperty(key, out _)) && first.GetProperty("x").GetDouble() == 1 && first.GetProperty("state").GetProperty("k").GetBoolean(), "a widgets message carries place, base size and state");
            Assert(message.RootElement.GetProperty("widgets")[1].GetProperty("state").ValueKind == JsonValueKind.Null, "state that is not a JSON object is sent as null");
        }
        using (var pointer = JsonDocument.Parse(SkinWidgets.PointerMessage("w1", "wheel", new Point(3.33, 4), -120))) {
            var body = pointer.RootElement.GetProperty("pointer");
            Assert(body.GetProperty("kind").GetString() == "wheel" && body.GetProperty("x").GetDouble() == 3.3 && body.GetProperty("delta").GetInt32() == -120 && body.GetProperty("button").GetInt32() == 0, "a pointer message names the widget, kind, base point, delta and button");
        }
        var fake = new FakeState();
        JsonElement Route(string json) => JsonDocument.Parse(SkinWidgets.RouteAction(json, id => id == "w1" ? fake : null).GetAwaiter().GetResult()).RootElement.GetProperty("actionResult");
        var unknown = Route("{\"type\":\"action\",\"id\":\"w9\",\"name\":\"go\",\"args\":{}}");
        Assert(!unknown.GetProperty("ok").GetBoolean() && unknown.GetProperty("error").GetString() == "unknown-widget" && fake.Calls == 0, "an action for a widget the page was not given is refused before any plugin runs");
        Assert(Route("{\"type\":\"action\",\"id\":\"w1\",\"name\":\"Go!\",\"args\":{}}").GetProperty("error").GetString() == "bad-name" && Route("{\"type\":\"action\",\"id\":\"w1\",\"name\":\"go\",\"args\":[1]}").GetProperty("error").GetString() == "bad-args" && fake.Calls == 0, "bad action names and non-object arguments are refused");
        var done = Route("{\"type\":\"action\",\"id\":\"w1\",\"name\":\"go\",\"args\":{\"a\":2}}");
        Assert(done.GetProperty("ok").GetBoolean() && done.GetProperty("result").GetProperty("a").GetInt32() == 2 && done.GetProperty("id").GetString() == "w1" && done.GetProperty("name").GetString() == "go", "a known widget runs the action and its JSON result comes back");
        Assert(!Route("{\"type\":\"action\",\"id\":\"w1\",\"name\":\"boom\"}").GetProperty("ok").GetBoolean(), "a refused action reports failure");

        var tabs = ((Grid)controller.Overlay.Content).Children.OfType<OverlayTabs>().Single();
        Assert(JsonDocument.Parse(tabs.RunSkinAction("{\"type\":\"action\",\"id\":\"plugin.x\",\"name\":\"go\"}").GetAwaiter().GetResult()).RootElement.GetProperty("actionResult").GetProperty("error").GetString() == "unknown-widget", "without a world page no widget takes actions");
        var tabsState = tabs.TabsStateForCheck;
        bool added = tabs.WorkspaceTabs.Count(tab => !tab.Hidden) < 2 && tabs.AddWorkspaceTab("스킨 확인");
        string tabsJson = tabsState.Json();
        using (var state = JsonDocument.Parse(tabsJson)) {
            var list = state.RootElement.GetProperty("tabs").EnumerateArray().ToArray();
            Assert(tabsState.Type == "facet.app/tabs@1" && list.Length > 1 && list.Count(tab => tab.GetProperty("selected").GetBoolean()) == 1 && list.All(tab => tab.GetProperty("id").GetString()!.Length > 0 && tab.TryGetProperty("name", out _) && tab.TryGetProperty("icon", out _)), "the tab bar publishes its visible tabs with one selected " + tabsJson);
            string before = tabs.SelectedWorkspace!.TabId, other = list.First(tab => !tab.GetProperty("selected").GetBoolean()).GetProperty("id").GetString()!;
            Assert(tabsState.Act("select", "{\"id\":\"" + other + "\"}").GetAwaiter().GetResult() == "true" && tabs.SelectedWorkspace!.TabId == other, "select {id} switches the tab");
            bool refused = false; try { tabsState.Act("select", "{\"id\":\"missing\"}").GetAwaiter().GetResult(); } catch (ArgumentException) { refused = true; }
            Assert(refused, "select refuses an unknown tab");
            tabsState.Act("select", "{\"id\":\"" + before + "\"}").GetAwaiter().GetResult();
        }
        if (added) tabs.RemoveWorkspaceTab(tabs.WorkspaceTabs.Last().Id);

        // A frame built while the overlay is hidden must not wipe the page's widgets, and showing the overlay must resend them.
        var sent = new List<string>();
        bool wasShown = controller.Overlay.IsVisible;
        if (wasShown) { controller.ToggleOverlay(); Pump(350); }
        tabs.SkinFrames = sent.Add; tabs.SetSkinWorld(true);
        tabs.SendSkinFrame(); Pump(100);
        Assert(sent.Count == 0, "a hidden overlay sends no widget frame that would clear the page");
        controller.ToggleOverlay(); PumpUntil(() => sent.Count > 0, 3000);
        Assert(sent.Count > 0 && sent[^1].StartsWith("{\"widgets\":"), "showing the overlay sends the skin widgets frame again");
        controller.ToggleOverlay(); Pump(350);
        tabs.SetSkinWorld(false); tabs.SkinFrames = null;
        if (wasShown) { controller.ToggleOverlay(); Pump(350); }

        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("--world-widgets-check");
        using var process = Process.Start(info)!; string output = process.StandardOutput.ReadToEnd();
        Assert(process.WaitForExit(60000) && process.ExitCode == 0, "a world page receives widgets and pointer messages and gets action results: " + output.Trim());
    }

    // A test page in the real locked WebView2: it reports what it receives and asks for one refused and one allowed action.
    private static int RunWorldWidgetsCheck()
    {
        var ui = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        const string page = "<script>const say=m=>chrome.webview.postMessage({type:'probe',message:m});"
            + "chrome.webview.addEventListener('message',e=>{const m=e.data;"
            + "if(m.widgets){say('widgets:'+JSON.stringify(m.widgets));chrome.webview.postMessage({type:'action',id:'nope',name:'go',args:{}});chrome.webview.postMessage({type:'action',id:'w1',name:'go',args:{n:7}});}"
            + "if(m.actionResult)say('result:'+JSON.stringify(m.actionResult));if(m.pointer)say('pointer:'+JSON.stringify(m.pointer));});"
            + "chrome.webview.postMessage({type:'loaded'});chrome.webview.postMessage({type:'ready'});</script>";
        var overlay = new Window { Left = -20000, Top = -20000, Width = 200, Height = 200, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false }; overlay.Show();
        var world = new WorldBackdrop(page, ["x"], _ => Task.FromException<WeatherSnapshot>(new OperationCanceledException()));
        world.DataFolderForCheck = Path.Combine(Path.GetTempPath(), "facet-world-check");
        var fake = new FakeState();
        world.ActionRequested = json => SkinWidgets.RouteAction(json, id => id == "w1" ? fake : null);
        world.Widgets(SkinWidgets.WidgetsMessage([new("w1", "test.plugin/thing@1", new Rect(10, 20, 30, 40), 60, 80, fake.Json())]));
        var seen = new List<string>();
        int code = 1;
        void Finish() { if (code == 0) return; bool widgets = seen.Any(m => m.StartsWith("widgets:") && m.Contains("\"id\":\"w1\"") && m.Contains("\"baseWidth\":60") && m.Contains("\"state\":{\"n\":1}"));
            bool refused = seen.Any(m => m.StartsWith("result:") && m.Contains("\"id\":\"nope\"") && m.Contains("\"ok\":false"));
            bool allowed = seen.Any(m => m.StartsWith("result:") && m.Contains("\"id\":\"w1\"") && m.Contains("\"ok\":true") && m.Contains("\"n\":7"));
            bool pointer = seen.Any(m => m.StartsWith("pointer:") && m.Contains("\"kind\":\"down\""));
            if (widgets && refused && allowed && pointer && fake.Calls == 1) { code = 0; ui.Shutdown(); } }
        world.ProbeForCheck = result => {
            seen.Add(result);
            if (result.StartsWith("widgets:")) world.Pointer(SkinWidgets.PointerMessage("w1", "down", new Point(5, 6)));
            Finish();
        };
        world.Failed += () => { Console.WriteLine("failed: " + world.FailureForCheck); ui.Shutdown(); };
        var limit = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) }; limit.Tick += (_, _) => { limit.Stop(); ui.Shutdown(); }; limit.Start();
        ui.Dispatcher.BeginInvoke(() => world.SetVisible(true, overlay));
        ui.Run();
        world.Dispose();
        Console.WriteLine(string.Join(" | ", seen));
        return code;
    }
}
