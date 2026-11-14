using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Facet;
using Facet.Core;

internal static partial class Program
{
    private static void CheckQuickLaunch(Controller controller, string qa)
    {
        string root = Path.Combine(qa, "quick-launch-fixture"), first = Path.Combine(root, "first"), second = Path.Combine(root, "second");
        Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        foreach (string file in new[] { "stone.exe", "stonecode.exe", "otherstone.cmd", "ignored.txt" }) File.WriteAllText(Path.Combine(first, file), "fixture");
        File.WriteAllText(Path.Combine(second, "stone.exe"), "second PATH entry");
        File.WriteAllText(Path.Combine(first, "stone.cmd"), "lower PATHEXT priority");
        string folder = Path.Combine(root, "folder with spaces"); Directory.CreateDirectory(folder);
        string document = Path.Combine(folder, "notes.txt"); File.WriteAllText(document, "fixture");
        var aliases = QuickLaunchCatalog.ParseAliases($"work={folder}\nnotes={document}");
        Assert(aliases.Count == 2 && aliases["WORK"] == folder, "alias parser preserves absolute paths and matches case insensitively");
        foreach (string invalid in new[] { "bad", "a=relative", $"a={folder}\nA={folder}", $"@@a={folder}", $" ={folder}" }) {
            bool rejected = false; try { QuickLaunchCatalog.ParseAliases(invalid); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "invalid or duplicate alias is rejected: " + invalid.Split('\n')[0]);
        }
        var catalog = new QuickLaunchCatalog(first + ";" + second + ";" + Path.Combine(root,"missing"), ".EXE;.CMD");
        System.Collections.Generic.IReadOnlyList<LaunchCandidate> Search(string text) => catalog.SearchAsync(text, aliases, CancellationToken.None).GetAwaiter().GetResult();
        Assert(Search("").Count == 0, "empty app input keeps candidate list hidden");
        var command = Search("> ").Single();
        Assert(command.Text == "help" && command.Command == LaunchCommand.Help && command.Completion == "> help", "command prefix lists the reserved help command");
        Assert(Search("> h").Single().Command == LaunchCommand.Help && Search("> HELP").Single().Command == LaunchCommand.Help,
            "reserved commands support prefix completion and case-insensitive matching");
        Assert(Search("> stone").Count == 0 && Search("> help.exe").Count == 0, "unknown reserved commands cannot fall back to PATH or executable extensions");
        bool commandRejected = false; try { QuickLaunchCatalog.StartInfo(command); } catch (ArgumentException) { commandRejected = true; }
        Assert(commandRejected, "reserved commands cannot cross the external process launch boundary");
        var apps = Search("stone");
        Assert(apps.Count == 3 && apps[0].Text == "stone" && apps[1].Text == "stonecode", "exact and prefix matches precede substring matches");
        Assert(apps[0].Target == Path.Combine(first,"stone.exe"), "PATH order and PATHEXT priority choose the executable used for launch");
        Assert(Search("stone.exe").Single().Target == apps[0].Target, "explicit executable extension is accepted");
        Assert(Search("ignored").Count == 0 && Search("stone & calc").Count == 0, "non-executable files and shell fragments are not executable suggestions");
        Assert(Search("@@wo").Single().Completion == "@@work" && Search("@@").Count == 2, "alias prefix and empty alias search return saved aliases");
        Assert(Search("@relative").Count == 0 && Search("@" + folder).Any(x => x.Target == folder), "path mode requires an absolute path and includes existing folder");
        Assert(Search("@" + folder + Path.DirectorySeparatorChar).Any(x => x.Target == document), "absolute path completion enumerates immediate children");
        var appInfo = QuickLaunchCatalog.StartInfo(apps[0]);
        Assert(appInfo.FileName == apps[0].Target && appInfo.Arguments == "", "app launch uses resolved full path without interpreting typed arguments");
        var fileInfo = QuickLaunchCatalog.StartInfo(Search("@@notes").Single());
        Assert(Path.GetFileName(fileInfo.FileName) == "explorer.exe" && fileInfo.Arguments == "/select,\"" + document + "\"", "file aliases select the file in Explorer instead of executing it");
        Assert(QuickLaunchCatalog.StartInfo(Search("@@work").Single()).Arguments == "\"" + folder + "\"", "folder aliases open their quoted absolute path");
        using (var cancelled = new CancellationTokenSource()) {
            cancelled.Cancel(); bool rejected = false;
            try { catalog.SearchAsync("stone", aliases, cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { rejected = true; }
            Assert(rejected, "cancelled searches never publish stale results");
        }
        var original = controller.Data.Settings.LaunchAliases;
        controller.Data.Settings.LaunchAliases = aliases;
        controller.Store.Save(controller.Data);
        Assert(new Store(controller.Store.Root).Load().Settings.LaunchAliases["work"] == folder, "aliases persist in encrypted settings");
        Assert(new Settings().OverlayHotkey == "Win+Alt+O", "new installations use requested overlay shortcut");
        var reserved = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(controller.Data.Settings))!;
        reserved.OverlayHotkey = Controller.QuickLaunchHotkey;
        bool reservedRejected = false; try { controller.RegisterKeys(reserved); } catch (ArgumentException) { reservedRejected = true; }
        Assert(reservedRejected, "quick launch shortcut is reserved before any native registration changes");

        const string webQuery = "한글 C# & a=b + \"두 단어\" ? #끝";
        var webInfo = QuickLaunchWebSearch.StartInfo("? " + webQuery);
        var webUri = new Uri(webInfo.FileName);
        Assert(webInfo.UseShellExecute && webInfo.Arguments == "" && webUri.Scheme == "https" && webUri.Host == "www.google.com" &&
            webUri.Fragment == "" && Uri.UnescapeDataString(webUri.Query[3..]) == webQuery,
            "web search opens a fixed HTTPS URL through the default browser and encodes the whole query as data");
        foreach(var engine in Enum.GetValues<WebSearchEngine>()) {
            var uri=new Uri(QuickLaunchWebSearch.StartInfo("? "+webQuery,engine).FileName);
            string expectedHost=engine switch {WebSearchEngine.Naver=>"search.naver.com",WebSearchEngine.Bing=>"www.bing.com",_=>"www.google.com"};
            string key=engine==WebSearchEngine.Naver ? "?query=" : "?q=";
            Assert(uri.Host==expectedHost && Uri.UnescapeDataString(uri.Query[key.Length..])==webQuery && uri.Fragment=="",
                "each search engine receives the encoded query: "+engine);
        }
        foreach(string address in new[]{"https://example.com/path?q=hello%20world#part","http://localhost:8080/test","example.com/path?q=one#two","127.0.0.1:8080/a","[::1]:8080/a","예시.한국/경로"}) {
            var uri=new Uri(QuickLaunchWebSearch.StartInfo("? "+address,WebSearchEngine.Naver).FileName);
            Assert(uri.Host!="search.naver.com" && uri.Scheme is "http" or "https", "web addresses open directly instead of being searched: "+address);
        }
        Assert(QuickLaunchWebSearch.StartInfo("? example.com").FileName=="https://example.com/" &&
            QuickLaunchWebSearch.StartInfo("? https://example.com/a?x=1&y=2#part").FileName=="https://example.com/a?x=1&y=2#part" &&
            QuickLaunchWebSearch.StartInfo("https://example.com/a").FileName=="https://example.com/a",
            "bare hosts default to HTTPS, URL query/fragment are retained and full URLs work without a prefix");
        foreach(string query in new[]{"site:example.com text","example.com 검색","user@example.com","javascript:alert(1)","file:///C:/Windows/notepad.exe","ftp://example.com"}) {
            var uri=new Uri(QuickLaunchWebSearch.StartInfo("? "+query,WebSearchEngine.Bing).FileName);
            Assert(uri.Host=="www.bing.com" && Uri.UnescapeDataString(uri.Query[3..])==query,
                "ordinary text and non-web schemes remain encoded search data: "+query);
        }
        Assert(!QuickLaunchWebSearch.IsInput("stone.exe") && !QuickLaunchWebSearch.IsInput("@@work") && !QuickLaunchWebSearch.IsInput("> help"),
            "address detection preserves normal app, alias and reserved command modes");
        foreach (string emptyWeb in new[] { "?", "? ", "? \t\r\n", "ordinary app" }) {
            bool rejected = false; try { QuickLaunchWebSearch.StartInfo(emptyWeb); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "empty or non-search input cannot launch a web search: " + emptyWeb.Trim());
        }
        Assert(QuickLaunchWebSearch.IsInput("? ") && !QuickLaunchWebSearch.IsInput("?word") && !QuickLaunchWebSearch.IsInput("\" ? search"),
            "web search requires the question-mark whitespace prefix and does not overlap app or chat input");
        string? realBrowser = QuickLaunchWebSearch.DefaultBrowserExecutable();
        Assert(realBrowser == null || Path.IsPathFullyQualified(realBrowser) && File.Exists(realBrowser), "browser icon association resolves an existing absolute executable or gracefully returns no icon (found=" + (realBrowser != null) + ")");
        ProcessStartInfo? launched = null; int browserLookups = 0;
        var view = new QuickLaunchView(controller, catalog, info => launched = info, () => { Interlocked.Increment(ref browserLookups); return realBrowser ?? apps[0].Target; });
        var input = (TextBox)view.FindName("Input"); var list = (ListBox)view.FindName("Candidates");
        var ghost = (TextBlock)view.FindName("Ghost"); var frame = (Border)view.FindName("CandidatesFrame");
        Assert(new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch).TryGetGlyphTypeface(out var inputTypeface) &&
            inputTypeface.FontUri.OriginalString.Contains("Pretendard-Bold.ttf", StringComparison.OrdinalIgnoreCase) &&
            inputTypeface.CharacterToGlyphMap.ContainsKey('가') && inputTypeface.CharacterToGlyphMap.ContainsKey('A'),
            "launcher resolves the bundled Pretendard font with Korean and Latin glyphs instead of an installed fallback");
        void Layout() { view.Measure(new Size(1280,800)); view.Arrange(new Rect(0,0,1280,800)); view.UpdateLayout(); }
        void WaitFor(System.Func<bool> ready) {
            var timeout = Stopwatch.StartNew(); while ((!list.IsHitTestVisible || !ready()) && timeout.Elapsed < TimeSpan.FromSeconds(6)) Pump(25);
            Assert(list.IsHitTestVisible && ready(), "asynchronous quick launch result arrived"); Layout();
        }
        void Capture(string name) {
            Layout(); var bitmap = new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32); bitmap.Render(view);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(qa,name + ".png")); png.Save(output);
        }
        view.Begin(); Layout();
        Assert(frame.Visibility == Visibility.Collapsed && list.Items.Count == 0, "initial input mode shows no candidate frame");
        var inputFrame = (Border)((Grid)view.FindName("Layout")).Children[0];
        double centerBefore = inputFrame.TranslatePoint(new Point(0,32),view).Y;
        Assert(Math.Abs(centerBefore - 400) < 1, "input box is vertically centered in the monitor area");
        Capture("quick-launch-empty");
        var help = (Border)view.FindName("HelpPanel");
        var helpScroll = (ScrollViewer)view.FindName("HelpScroll");
        Assert(help.Visibility == Visibility.Collapsed && input.Text.Length == 0 && ghost.Text.Length == 0, "empty launcher has no help text or completion");
        input.Text = "?"; WaitFor(() => list.Items.Count == 0);
        Assert(help.Visibility == Visibility.Collapsed, "question mark no longer opens launcher help");
        input.Text = "? "; input.CaretIndex = input.Text.Length;
        var browserImage = (Image)view.FindName("LeadingIcon");
        WaitFor(() => browserImage.Source != null);
        var resolvedBrowserIcon = browserImage.Source;
        Assert(browserImage.Visibility == Visibility.Visible && list.Items.Count == 0 && frame.Visibility == Visibility.Collapsed &&
            ghost.Text == "" && help.Visibility == Visibility.Collapsed && launched == null, "web prefix displays the browser icon without app candidates, help or automatic launch");
        view.HandleKey(Key.Enter,ModifierKeys.None);
        Assert(launched == null && input.Text == "? " && ((TextBlock)view.FindName("Status")).Visibility == Visibility.Visible,
            "empty search leaves the input open and explains that a query is required");
        input.Text = "? " + webQuery; input.CaretIndex = input.Text.Length; Pump(100);
        Assert(ReferenceEquals(browserImage.Source,resolvedBrowserIcon) && browserLookups == 1 && list.Items.Count == 0,
            "editing a web query reuses the completed browser icon without flickering or repeating association lookup");
        Capture("quick-launch-web-search");
        view.HandleKey(Key.Enter,ModifierKeys.None);
        Assert(launched?.FileName == webInfo.FileName && launched.UseShellExecute, "Enter dispatches only the encoded search URL without starting a browser command line directly");
        var originalEngine=controller.Data.Settings.LauncherSearchEngine;
        // Keep the same query and view open while committing settings, as a live launcher does.
        var engineSettings=new OverlaySettingsView();
        engineSettings.Configure(()=>controller.Data.Settings,next=>controller.Commit(data=>data.Settings=next));
        var settingsGroups=(StackPanel)engineSettings.FindName("Groups");
        var engineEditor=settingsGroups.Children.OfType<StackPanel>().SelectMany(group=>group.Children.OfType<Grid>())
            .Select(row=>(FrameworkElement)((StackPanel)row.Children[1]).Children[0])
            .OfType<ComboBox>().Single(editor=>System.Windows.Automation.AutomationProperties.GetName(editor)=="검색 엔진");
        engineEditor.SelectedValue=WebSearchEngine.Naver;
        view.HandleKey(Key.Enter,ModifierKeys.None);
        Assert(new Uri(launched!.FileName).Host=="search.naver.com" && !engineSettings.HasChanges,
            "changing the engine in settings immediately affects an already typed launcher query");
        engineEditor.SelectedValue=WebSearchEngine.Bing;
        view.HandleKey(Key.Enter,ModifierKeys.None);
        Assert(new Uri(launched!.FileName).Host=="www.bing.com" && new Store(controller.Store.Root).Load().Settings.LauncherSearchEngine==WebSearchEngine.Bing,
            "the next launcher execution uses the latest persisted engine without reopening");
        input.Text="? example.com/path?q=one&x=two#part"; view.HandleKey(Key.Enter,ModifierKeys.None);
        Assert(launched!.FileName=="https://example.com/path?q=one&x=two#part", "launcher opens a typed address independently of the engine");
        input.Text="https://example.com/direct"; view.HandleKey(Key.Enter,ModifierKeys.None);
        Assert(launched!.FileName=="https://example.com/direct", "full URL input opens directly without the search prefix");
        engineEditor.SelectedValue=originalEngine;
        launched = null;
        input.Text = "> "; input.CaretIndex = input.Text.Length;
        WaitFor(() => list.Items.Count == 1);
        Assert(((QuickLaunchView.Row)list.Items[0]).Candidate.Command == LaunchCommand.Help && ghost.Text == "> help" &&
            ((Facet.Design.Icon)view.FindName("LeadingCommandIcon")).Visibility == Visibility.Visible,
            "reserved command appears in candidates with ghost completion and a help icon");
        Capture("quick-launch-commands");
        input.Text = "> h"; input.CaretIndex = input.Text.Length; WaitFor(() => list.Items.Count == 1);
        view.HandleKey(Key.Up,ModifierKeys.None);
        Assert(input.Text == "> help" && list.Items.Count == 1, "keyboard navigation retains the reserved-command prefix in the adopted text");
        view.HandleKey(Key.Tab,ModifierKeys.None);
        Assert(input.Text == "> help", "Tab completes a partial reserved command");
        view.HandleKey(Key.Enter,ModifierKeys.None); Pump(150); Layout();
        Assert(help.Visibility == Visibility.Visible && input.Text == "" && list.Items.Count == 0 && frame.Visibility == Visibility.Collapsed && ghost.Text == "" && launched == null,
            "executing help opens the panel and clears input without launching an app");
        Assert(help.TranslatePoint(new Point(0,help.ActualHeight),view).Y <= inputFrame.TranslatePoint(new Point(),view).Y - 10 &&
            inputFrame.TranslatePoint(new Point(0,32),view).Y == centerBefore, "help appears above the fixed centered input box");
        Assert(view.ContainsPointerSource(helpScroll), "help panel scrolling counts as an internal launcher interaction");
        view.HandleKey(Key.Enter,ModifierKeys.None); view.HandleKey(Key.Tab,ModifierKeys.None);
        Assert(launched == null && ((TextBlock)view.FindName("Status")).Visibility == Visibility.Collapsed, "empty input while help is open does not execute or display a missing-candidate error");
        Capture("quick-launch-help");
        view.Measure(new Size(480,360));view.Arrange(new Rect(0,0,480,360));view.UpdateLayout();
        Assert(help.TranslatePoint(new Point(),view).Y >= 0 && helpScroll.ScrollableHeight > 0 &&
            help.TranslatePoint(new Point(0,help.ActualHeight),view).Y <= inputFrame.TranslatePoint(new Point(),view).Y - 10,
            "small displays keep help above the input and allow its content to scroll");
        var smallHelp = new RenderTargetBitmap(480,360,96,96,PixelFormats.Pbgra32);smallHelp.Render(view);
        var smallPng = new PngBitmapEncoder();smallPng.Frames.Add(BitmapFrame.Create(smallHelp));using(var output = File.Create(Path.Combine(qa,"quick-launch-help-small.png")))smallPng.Save(output);
        Layout();
        input.Text = "st"; input.CaretIndex = input.Text.Length;
        WaitFor(() => list.Items.Count == 3);
        Assert(help.Visibility == Visibility.Visible, "help stays open while normal app search continues");
        input.Text = "> help"; input.CaretIndex = input.Text.Length;
        view.HandleKey(Key.Enter,ModifierKeys.None); Pump(150);
        Assert(help.Visibility == Visibility.Collapsed && input.Text == "" && launched == null, "executing help again closes the panel without closing the launcher");
        input.Text = "> he"; input.CaretIndex = input.Text.Length; WaitFor(() => list.Items.Count == 1);
        var helpItem = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        helpItem.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) {RoutedEvent=Mouse.PreviewMouseUpEvent});
        Assert(help.Visibility == Visibility.Visible && input.Text == "" && launched == null, "clicking a reserved command uses the same internal help toggle");
        input.Text = "> help"; view.HandleKey(Key.Enter,ModifierKeys.None); Pump(150);
        input.Text = "st"; input.CaretIndex = input.Text.Length; WaitFor(() => list.Items.Count == 3);
        Assert(ghost.Text == "stone" && frame.Visibility == Visibility.Visible, "best prefix suggestion appears as ghost text above the candidate list");
        Assert(inputFrame.TranslatePoint(new Point(0,32),view).Y == centerBefore, "candidate list does not move the centered input box");
        WaitFor(() => list.Items.Cast<QuickLaunchView.Row>().All(row => row.IconResolved));
        var hovered = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
        view.HandleCandidateHover(hovered, new Point(20,20));
        Assert(((QuickLaunchView.Row)list.SelectedItem).Candidate.Text == "stonecode" && input.Text == "stonecode" && ghost.Text == "" &&
            input.CaretIndex == input.Text.Length && input.SelectionLength == 0 && list.Items.Count == 3 && launched == null &&
            ReferenceEquals(((Image)view.FindName("LeadingIcon")).Source, ((QuickLaunchView.Row)list.SelectedItem).Icon),
            "hover adopts the candidate text with an editable end caret and matching icon without launching or replacing the list");
        input.Text = input.Text[..^1]; input.CaretIndex = input.Text.Length;
        WaitFor(() => list.Items.Count == 1);
        Assert(input.Text == "stonecod" && ghost.Text == "stonecode", "deleting after hover edits the adopted text and searches again");
        WaitFor(() => ((QuickLaunchView.Row)list.Items[0]).IconResolved);
        input.Select(2,3);
        view.HandleCandidateHover((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0), new Point(21,20));
        Assert(input.Text == "stonecod" && input.SelectionStart == 2 && input.SelectionLength == 3,
            "moving within the same candidate preserves edited text and selection after search and icon rows refresh");
        input.Select(input.Text.Length,0);
        input.Text += "x"; input.CaretIndex = input.Text.Length;
        WaitFor(() => list.Items.Count == 0);
        Assert(input.Text == "stonecodx", "typing after hover continues from the edited candidate text");
        input.Text = "st"; input.CaretIndex = input.Text.Length;
        WaitFor(() => list.Items.Count == 3 && list.Items.Cast<QuickLaunchView.Row>().All(row => row.IconResolved));
        hovered = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
        view.HandleCandidateHover(hovered, new Point(22,20));
        Assert(input.Text == "st", "the same hovered candidate does not overwrite a newly typed query");
        view.HandleCandidateHover((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0), new Point(24,24));
        Assert(input.Text == "stone" && input.CaretIndex == input.Text.Length && input.SelectionLength == 0,
            "moving to a different candidate adopts its text with the caret at the end");
        view.HandleCandidateHover(hovered, new Point(25,25));
        Assert(input.Text == "stonecode", "returning from another candidate adopts the previous candidate again");
        view.HandleKey(Key.Down,ModifierKeys.None); Pump(150);
        Assert(((QuickLaunchView.Row)list.SelectedItem).Candidate.Text == "otherstone" && input.Text == "otherstone" &&
            input.CaretIndex == input.Text.Length && input.SelectionLength == 0 && list.Items.Count == 3 && ghost.Text == "",
            "Down adopts the selected completion with an editable end caret while preserving the result set");
        view.HandleCandidateHover(hovered, Mouse.GetPosition(view));
        Assert(((QuickLaunchView.Row)list.SelectedItem).Candidate.Text == "otherstone", "stationary mouse does not override keyboard navigation");
        view.HandleKey(Key.Up,ModifierKeys.None);
        Assert(input.Text == "stonecode" && ((QuickLaunchView.Row)list.SelectedItem).Candidate.Text == "stonecode" &&
            ReferenceEquals(((Image)view.FindName("LeadingIcon")).Source, ((QuickLaunchView.Row)list.SelectedItem).Icon),
            "Up adopts the preceding candidate and keeps the input icon synchronized");
        input.Text = input.Text[..^1]; input.CaretIndex = input.Text.Length;
        WaitFor(() => list.Items.Count == 1 && ((QuickLaunchView.Row)list.SelectedItem).Candidate.Text == "stonecode");
        Assert(input.Text == "stonecod" && ghost.Text == "stonecode", "deleting after navigation searches from the adopted candidate text");
        input.CaretIndex = 0; Assert(ghost.Text == "", "ghost completion hides while editing inside existing text");
        input.CaretIndex = input.Text.Length;
        view.HandleKey(Key.Tab,ModifierKeys.None);
        Assert(input.Text == "stonecode", "Tab accepts the selected completion");
        input.Text += "x"; input.CaretIndex = input.Text.Length;
        WaitFor(() => list.Items.Count == 0);
        Assert(input.Text == "stonecodex" && frame.Visibility == Visibility.Collapsed, "typing after navigation edits the adopted text and replaces the original result set");
        input.Text = "@@wo"; input.CaretIndex = input.Text.Length;
        WaitFor(() => list.Items.Count == 1 && ((QuickLaunchView.Row)list.Items[0]).Candidate.Text == "work");
        Assert(ghost.Text == "@@work", "alias input uses the same ghost completion behavior");
        view.HandleKey(Key.Down,ModifierKeys.None);
        Assert(input.Text == "@@work" && input.CaretIndex == input.Text.Length, "keyboard navigation adopts alias completion with its prefix intact");
        Capture("quick-launch-alias");
        input.Text = "st"; input.CaretIndex = input.Text.Length;
        WaitFor(() => list.Items.Count == 3);
        WaitFor(() => ((QuickLaunchView.Row)list.Items[0]).Icon != null);
        Capture("quick-launch-apps");
        // Visual QA for an overflowing list, including the reserved scrollbar gutter.
        var originalItems = list.ItemsSource;
        var sampleIcon = ((QuickLaunchView.Row)list.Items[0]).Icon;
        list.ItemsSource = Enumerable.Range(1,8).Select(i => new QuickLaunchView.Row(
            new LaunchCandidate("stone tool " + i, apps[0].Target, "stone-tool" + i, false), sampleIcon, true)).ToArray();
        list.SelectedIndex = 0;
        Capture("quick-launch-scrollbar-top");
        var candidateScroll = (ScrollViewer)VisualTreeHelper.GetChild(list,0);
        candidateScroll.ScrollToEnd();
        Capture("quick-launch-scrollbar-bottom");
        list.ItemsSource = originalItems; list.SelectedIndex = 0;
        var leading = (Image)view.FindName("LeadingIcon");
        var previousIcon = leading.Source;
        int clearedIcons = 0;
        var sourceProperty = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image));
        EventHandler observeIcon = (_, _) => { if (leading.Source == null) clearedIcons++; };
        sourceProperty.AddValueChanged(leading, observeIcon);
        try {
            input.Text = "sto"; input.CaretIndex = input.Text.Length;
            Assert(ReferenceEquals(leading.Source, previousIcon) && leading.Visibility == Visibility.Visible, "typing preserves the displayed icon while the next search is pending");
            Assert(ghost.Text == "" && !list.IsHitTestVisible, "pending results cannot complete or launch stale candidates");
            WaitFor(() => ((QuickLaunchView.Row)list.Items[0]).IconResolved);
            Assert(ReferenceEquals(leading.Source, previousIcon), "unchanged executable reuses its completed icon");
            input.Text = "stonec"; input.CaretIndex = input.Text.Length;
            Assert(ReferenceEquals(leading.Source, previousIcon), "changing the matched executable retains the prior icon until lookup completes");
            WaitFor(() => list.Items.Count == 1 && ((QuickLaunchView.Row)list.Items[0]).IconResolved);
            Assert(ReferenceEquals(leading.Source, ((QuickLaunchView.Row)list.Items[0]).Icon) && !ReferenceEquals(leading.Source, previousIcon) && clearedIcons == 0,
                "new executable icon replaces the old icon without an intermediate empty state");
        } finally { sourceProperty.RemoveValueChanged(leading, observeIcon); }
        input.Text = "st"; input.CaretIndex = input.Text.Length;
        WaitFor(() => list.Items.Count == 3);
        view.HandleKey(Key.Enter,ModifierKeys.None);
        Assert(launched?.FileName == apps[0].Target, "Enter dispatches selected executable through the launch boundary");
        input.Text = "@@missing"; WaitFor(() => frame.Visibility == Visibility.Collapsed);
        view.HandleKey(Key.Enter,ModifierKeys.None);
        WaitFor(() => ((TextBlock)view.FindName("Status")).Visibility == Visibility.Visible);
        Assert(launched?.FileName == apps[0].Target, "missing candidate leaves input open and launches nothing");
        input.Text = "stone"; input.CaretIndex = input.Text.Length;
        WaitFor(() => list.Items.Count == 3);
        var clicked = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
        clicked.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
        Assert(launched?.FileName == Path.Combine(first,"stonecode.exe"), "single click launches the clicked row rather than the prior selection: " + launched?.FileName);
        input.Text = "st"; view.End(); Pump(150);
        Assert(list.Items.Count == 0 && ghost.Text == "", "hiding input mode cancels queued search and removes suggestions");
        var fields = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var overlay = controller.Overlay;
        var behind = (Image)typeof(OverlayWindow).GetField("behind",fields)!.GetValue(overlay)!;
        var backdrop = (SolidColorBrush)typeof(OverlayWindow).GetField("backdrop",fields)!.GetValue(overlay)!;
        var blur = (System.Windows.Media.Effects.BlurEffect)behind.Effect;
        behind.Source = BitmapSource.Create(1,1,96,96,PixelFormats.Bgr32,null,new byte[4],4);
        behind.Opacity = 1; backdrop.Opacity = .8; blur.Radius = 18;
        // A session with no capture request verifies detachment without reading the user's screen.
        var priorCapture = new BackdropCaptureSession(overlay.Dispatcher,(_,_)=>true,()=>new ControlledBackdrop());
        typeof(OverlayWindow).GetField("capture",fields)!.SetValue(overlay, priorCapture);
        controller.Overlay.SetQuickLaunch(true);
        var layers = (Grid)controller.Overlay.Content;
        Assert(controller.Overlay.IsQuickLaunch && layers.Children[2].Visibility == Visibility.Collapsed && layers.Children[3].Visibility == Visibility.Visible, "input mode replaces the overlay tabs");
        Assert(behind.Source == null && behind.Opacity == 0 && blur.Radius == 0 && backdrop.Opacity == 0 &&
            typeof(OverlayWindow).GetField("capture",fields)!.GetValue(overlay) == null && priorCapture.Stopped.Wait(2000),
            "quick launch immediately removes prior background pixels blur tint and capture session");
        overlay.Present(false,.8,18,280,280);
        typeof(OverlayWindow).GetMethod("ApplyProgress",fields)!.Invoke(overlay,[1d]);
        Assert(backdrop.Opacity == 0 && (double)typeof(OverlayWindow).GetField("requestedBlurRadius",fields)!.GetValue(overlay)! == 0,
            "input mode enforces zero background opacity and blur for saved nonzero effect settings");
        controller.Overlay.SetQuickLaunch(false);
        Assert(!controller.Overlay.IsQuickLaunch && layers.Children[2].Visibility == Visibility.Visible && layers.Children[3].Visibility == Visibility.Collapsed, "normal overlay mode restores the existing tabs");
        overlay.Present(false,.8,18,280,280);
        typeof(OverlayWindow).GetMethod("ApplyProgress",fields)!.Invoke(overlay,[1d]);
        Assert(backdrop.Opacity == .8 && (double)typeof(OverlayWindow).GetField("requestedBlurRadius",fields)!.GetValue(overlay)! == 18,
            "returning to normal overlay mode restores its requested tint and blur settings");
        int originalHideMilliseconds = controller.Data.Settings.OverlayHideMilliseconds;
        var visibilityRequest = typeof(OverlayWindow).GetField("requestedVisible",fields)!;
        var popup = (QuickLaunchView)layers.Children[3];
        var popupInput = (TextBox)popup.FindName("Input");
        var popupList = (ListBox)popup.FindName("Candidates");
        var popupFrame = (Border)popup.FindName("CandidatesFrame");
        void ArmQuickLaunchDismissal()
        {
            // Exercise routed/activation events without showing or activating a test window.
            overlay.SetQuickLaunch(true);
            controller.Data.Settings.OverlayVisible = true;
            controller.Data.Settings.OverlayHideMilliseconds = 0;
            visibilityRequest.SetValue(overlay,true);
        }
        void Click(UIElement source) => source.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        void Deactivate() => typeof(Window).GetMethod("OnDeactivated",fields)!.Invoke(overlay,[EventArgs.Empty]);
        ArmQuickLaunchDismissal();
        Click(popupInput);
        popupFrame.Visibility = Visibility.Visible;
        Click(popupList);
        popupInput.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,Environment.TickCount,popupInput,popupList) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        Assert(controller.Data.Settings.OverlayVisible, "input candidate clicks and internal keyboard focus changes keep quick launch open");
        popupInput.Text = "st";
        Click(layers);
        Pump(150);
        Assert(!controller.Data.Settings.OverlayVisible && popupList.Items.Count == 0 && popupFrame.Visibility == Visibility.Collapsed,
            "outside click dismisses input and candidates and cancels pending results");
        Assert(!(bool)typeof(Controller).GetField("overlayRestoreAllowed",fields)!.GetValue(controller)!,
            "automatic dismissal preserves the newly clicked app's focus even with zero hide duration");
        ArmQuickLaunchDismissal();
        popupInput.Text = "st";
        Deactivate(); Pump(150);
        Assert(controller.Data.Settings.OverlayVisible && (bool)visibilityRequest.GetValue(overlay)! && popupInput.Text == "st",
            "window deactivation keeps quick launch visible and preserves its input");
        overlay.SetQuickLaunch(false);
        controller.Data.Settings.OverlayVisible = true; visibilityRequest.SetValue(overlay,true);
        Click(layers);
        Assert(controller.Data.Settings.OverlayVisible, "normal overlay background clicks keep it open");
        overlay.CaptureToolVisibleProbe = () => true;
        Deactivate();
        Assert(controller.Data.Settings.OverlayVisible, "capture tool focus keeps the normal overlay open");
        overlay.CaptureToolVisibleProbe = () => false;
        Deactivate();
        Assert(controller.Data.Settings.OverlayVisible && (bool)visibilityRequest.GetValue(overlay)!,
            "normal overlay deactivation keeps its visibility request after capture tool closes");
        overlay.CaptureToolVisibleProbe = () => Native.IsCaptureToolVisible();
        controller.Data.Settings.OverlayVisible = false;
        overlay.Present(false,.8,18,280,0);
        controller.Data.Settings.OverlayHideMilliseconds = originalHideMilliseconds;
        controller.Data.Settings.LaunchAliases = original;
        controller.Store.Save(controller.Data);
    }
}
