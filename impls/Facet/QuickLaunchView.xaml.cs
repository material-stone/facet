using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Threading;

namespace Facet;

public partial class QuickLaunchView : UserControl
{
    internal sealed record Row(LaunchCandidate Candidate, ImageSource? Icon, bool IconResolved = false);
    private readonly Controller app;
    private readonly QuickLaunchCatalog catalog;
    private readonly QuickLaunchIcons icons = new();
    private readonly Action<ProcessStartInfo> start;
    private readonly Func<string?> browserExecutable;
    private Task<ImageSource?>? browserIcon;
    private CancellationTokenSource? cancellation;
    private Task search = Task.CompletedTask;
    private bool active, composing, launching, acceptingCandidate;
    private Point? lastPointerPosition;
    private LaunchCandidate? lastHoveredCandidate;
    private int generation, resultGeneration = -1;
    internal QuickLaunchView(Controller app, QuickLaunchCatalog? catalog = null, Action<ProcessStartInfo>? start = null, Func<string?>? browserExecutable = null)
    {
        this.app = app; this.catalog = catalog ?? new();
        this.start = start ?? (info => { using var process = Process.Start(info); });
        this.browserExecutable = browserExecutable ?? QuickLaunchWebSearch.DefaultBrowserExecutable;
        InitializeComponent();
        InitializeModes();
        SizeChanged += (_, _) => {
            Layout.Width = Math.Min(680, Math.Max(1, ActualWidth - 48));
            Candidates.MaxHeight = Math.Max(60, Math.Min(380, ActualHeight / 2 - 110));
            HelpPanel.MaxHeight = Math.Max(1, ActualHeight / 2 - 52);
        };
        Input.TextChanged += (_, _) => { if (!acceptingCandidate) QueueSearch(); };
        Input.SelectionChanged += (_, _) => UpdateGhost();
        Input.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateGhost()));
        TextCompositionManager.AddPreviewTextInputStartHandler(Input, (_, _) => { composing = true; UpdateGhost(); });
        TextCompositionManager.AddPreviewTextInputHandler(Input, (_, _) => { composing = false; Dispatcher.BeginInvoke(UpdateGhost); });
        Candidates.SelectionChanged += (_, _) => { UpdateGhost(); UpdateSelectedIcon(); };
        Candidates.PreviewMouseMove += (_, e) => HandleCandidateHover(e.OriginalSource as DependencyObject, e.GetPosition(this));
        Candidates.PreviewMouseLeftButtonUp += (_, e) => {
            if (resultGeneration == generation && ItemsControl.ContainerFromElement(Candidates, e.OriginalSource as DependencyObject) is ListBoxItem item && item.Content is Row row) {
                e.Handled = true; _ = LaunchAsync(row.Candidate);
            }
        };
    }
    internal void Begin()
    {
        browserIcon = null;
        active = true; composing = false; lastPointerPosition = null; lastHoveredCandidate = null; Input.Clear(); ClearResults(); HelpPanel.Visibility = Visibility.Collapsed;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusInput));
    }
    internal void End()
    {
        active = false; generation++; cancellation?.Cancel(); ClearResults(); HelpPanel.Visibility = Visibility.Collapsed;
        CloseFlyout();
    }
    internal void FocusInput() { if (active && IsVisible) { Input.Focus(); Keyboard.Focus(Input); } }
    internal void HandleCandidateHover(DependencyObject? source, Point position)
    {
        // Layout/icon refreshes and keyboard scrolling can synthesize mouse moves.
        // Only a changed pointer position may replace the keyboard selection.
        if (lastPointerPosition == position) return;
        lastPointerPosition = position;
        if (!active || composing || !Candidates.IsHitTestVisible || resultGeneration != generation) return;
        if (ItemsControl.ContainerFromElement(Candidates, source) is ListBoxItem { Content: Row row } item) {
            // Compare the candidate value, not its row container: typing and icon
            // loading can recreate the same row while the user edits its text.
            if (lastHoveredCandidate == row.Candidate) return;
            lastHoveredCandidate = row.Candidate;
            Candidates.SelectedItem = item.Content;
            AcceptSelectedCandidate(); FocusInput();
        }
    }
    internal bool ContainsPointerSource(DependencyObject? source)
    {
        if (ContainsModeSource(source)) return true;
        for (var node = source; node != null && !ReferenceEquals(node, this);
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (ReferenceEquals(node, InputFrame) || ReferenceEquals(node, CandidatesFrame) && CandidatesFrame.Visibility == Visibility.Visible ||
                ReferenceEquals(node, HelpPanel) && HelpPanel.Visibility == Visibility.Visible) return true;
        return false;
    }
    private void ClearResults(bool keepIcon = false)
    {
        Candidates.ItemsSource = null; CandidatesFrame.Visibility = Visibility.Collapsed;
        Status.Visibility = Visibility.Collapsed; Ghost.Text = ""; LeadingCommandIcon.Visibility = Visibility.Collapsed;
        if (!keepIcon) { LeadingIcon.Visibility = Visibility.Collapsed; LeadingIcon.Source = null; }
    }
    private void QueueSearch()
    {
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = new();
        int version = ++generation; string text = Input.Text;
        if (RefreshMode()) { ClearResults(); HelpPanel.Visibility=Visibility.Collapsed; resultGeneration=version; search=Task.CompletedTask; return; }
        if (QuickLaunchWebSearch.IsInput(text)) {
            ClearResults(keepIcon: true); Candidates.IsHitTestVisible = true; resultGeneration = version;
            search = active ? UpdateBrowserIconAsync(version, cancellation.Token) : Task.CompletedTask;
            return;
        }
        Status.Visibility = Visibility.Collapsed; Ghost.Text = "";
        Candidates.IsHitTestVisible = false;
        if (text.Length == 0) { ClearResults(); resultGeneration = version; search = Task.CompletedTask; return; }
        if (active) search = SearchAsync(text, version, cancellation.Token);
    }
    private async Task UpdateBrowserIconAsync(int version, CancellationToken token)
    {
        try {
            // Resolve the local browser association once per launcher session;
            // changing the query reuses this work and never clears a completed icon.
            browserIcon ??= Task.Run(() => {
                string? path = browserExecutable();
                if (path == null) return null;
                lock (icons) return icons.Get(path);
            });
            var icon = await browserIcon;
            if (active && version == generation && !token.IsCancellationRequested) SetLeadingIcon(icon);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException or InvalidOperationException or System.Security.SecurityException) {
            if (active && version == generation && !token.IsCancellationRequested) SetLeadingIcon(null);
        }
    }
    private async Task SearchAsync(string text, int version, CancellationToken token)
    {
        try {
            await Task.Delay(75, token);
            var aliases = new Dictionary<string, string>(app.Data.Settings.LaunchAliases ?? new(), StringComparer.OrdinalIgnoreCase);
            var found = await catalog.SearchAsync(text, aliases, token);
            if (!active || version != generation || token.IsCancellationRequested) return;
            // Keep completed icons for unchanged targets; unresolved icons never clear the input icon.
            var previous = Candidates.Items.Cast<Row>().Where(x => x.IconResolved)
                .GroupBy(x => x.Candidate.Target, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var rows = found.Select(x => x.Command != null ? new Row(x, null, true) : previous.TryGetValue(x.Target, out var old)
                ? new Row(x, old.Icon, true) : new Row(x, null)).ToArray();
            resultGeneration = version;
            Candidates.ItemsSource = rows;
            CandidatesFrame.Visibility = found.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Candidates.IsHitTestVisible = true;
            Candidates.SelectedIndex = found.Count > 0 ? 0 : -1;
            UpdateGhost();
            if (found.Count == 0) { SetLeadingIcon(null); return; }
            UpdateSelectedIcon();
            if (rows.Any(x => !x.IconResolved)) _ = LoadIconsAsync(rows, version, token);
        } catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) {
            if (active && version == generation) ShowError("후보를 가져오지 못했습니다. " + ex.Message);
        }
    }
    private async Task LoadIconsAsync(IReadOnlyList<Row> found, int version, CancellationToken token)
    {
        try {
            var rows = await Task.Run(() => found.Select(x => {
                token.ThrowIfCancellationRequested();
                if (x.IconResolved) return x;
                lock (icons) return new Row(x.Candidate, icons.Get(x.Candidate.Target), true);
            }).ToArray(), token);
            if (!active || version != generation || token.IsCancellationRequested) return;
            int selected = Candidates.SelectedIndex;
            Candidates.ItemsSource = rows; Candidates.SelectedIndex = selected; UpdateGhost();
        } catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) { }
    }
    private void UpdateGhost()
    {
        var row = resultGeneration == generation ? Candidates.SelectedItem as Row : null;
        string text = Input.Text, completion = row?.Candidate.Completion ?? "";
        bool ghost = !composing && text.Length > 0 && Input.SelectionLength == 0 && Input.CaretIndex == text.Length && Input.HorizontalOffset == 0 &&
            completion.Length > text.Length && completion.StartsWith(text, StringComparison.OrdinalIgnoreCase);
        // Preserve the user's casing so ghost glyphs line up exactly with typed text.
        Ghost.Inlines.Clear();
        if (ghost) {
            Ghost.Inlines.Add(new Run(text) { Foreground = Brushes.Transparent });
            Ghost.Inlines.Add(new Run(completion[text.Length..]));
        }
    }
    private void UpdateSelectedIcon()
    {
        if (resultGeneration == generation && Candidates.SelectedItem is Row { IconResolved: true } row) {
            SetLeadingIcon(row.Icon);
            LeadingCommandIcon.Visibility = row.Candidate.Command != null ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    private void SetLeadingIcon(ImageSource? icon)
    {
        if (!ReferenceEquals(LeadingIcon.Source, icon)) LeadingIcon.Source = icon;
        LeadingIcon.Visibility = icon == null ? Visibility.Collapsed : Visibility.Visible;
        LeadingCommandIcon.Visibility = Visibility.Collapsed;
    }
    internal bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Escape && flyout.Visibility == Visibility.Visible) { CloseFlyout(); FocusInput(); return true; }
        if (mode != null) {
            if (key==Key.Escape && modifiers==ModifierKeys.None) {app.HideOverlay();return true;}
            if (key==Key.Enter && modifiers==ModifierKeys.None && Input.IsKeyboardFocusWithin && !composing) {_=SubmitModeAsync();return true;}
            return false;
        }
        if (key == Key.Escape && modifiers == ModifierKeys.None) { app.HideOverlay(); return true; }
        if (composing || modifiers != ModifierKeys.None) return false;
        if (HelpPanel.Visibility == Visibility.Visible && Input.Text.Length == 0) {
            if (key is Key.Enter or Key.Tab) return true;
            if (key == Key.Down) { HelpScroll.LineDown(); return true; }
            if (key == Key.Up) { HelpScroll.LineUp(); return true; }
            if (key == Key.PageDown) { HelpScroll.PageDown(); return true; }
            if (key == Key.PageUp) { HelpScroll.PageUp(); return true; }
        }
        if (resultGeneration != generation && key is Key.Down or Key.Up or Key.Tab) return true;
        if (key is Key.Down or Key.Up && Candidates.Items.Count > 0) {
            lastPointerPosition = Mouse.GetPosition(this);
            Candidates.SelectedIndex = (Candidates.SelectedIndex + (key == Key.Down ? 1 : Candidates.Items.Count - 1)) % Candidates.Items.Count;
            AcceptSelectedCandidate();
            Candidates.ScrollIntoView(Candidates.SelectedItem); FocusInput(); return true;
        }
        if (key == Key.Tab || (key == Key.Right && Ghost.Text.Length > 0)) {
            if (Candidates.SelectedItem is Row row && Input.SelectionLength == 0 && Input.CaretIndex == Input.Text.Length) {
                Input.Text = row.Candidate.Completion; Input.CaretIndex = Input.Text.Length; FocusInput();
            }
            return true;
        }
        if (key == Key.Enter) { _ = LaunchAsync(); return true; }
        return false;
    }
    private void AcceptSelectedCandidate()
    {
        if (Candidates.SelectedItem is not Row row) return;
        // Navigation edits the visible input while retaining the original result set.
        // The next user edit starts a fresh search from this completed text.
        acceptingCandidate = true;
        try { Input.Text = row.Candidate.Completion; Input.Select(Input.Text.Length, 0); }
        finally { acceptingCandidate = false; }
        UpdateGhost(); UpdateSelectedIcon();
    }
    private async Task LaunchAsync(LaunchCandidate? clicked = null)
    {
        if (launching) return;
        launching = true;
        int version = generation;
        try {
            if (clicked == null && active && QuickLaunchWebSearch.IsInput(Input.Text)) {
                start(QuickLaunchWebSearch.StartInfo(Input.Text, app.Data.Settings.LauncherSearchEngine));
                app.HideOverlay(restore: false); return;
            }
            // Wait for text matching, never for potentially slow shell icon extraction.
            if (clicked == null && (resultGeneration != generation || Candidates.SelectedItem == null)) await search;
            if (!active || version != generation) return;
            var candidate = clicked ?? (resultGeneration == generation ? (Candidates.SelectedItem as Row)?.Candidate : null);
            if (candidate == null) { ShowError("실행할 후보가 없습니다."); return; }
            if (candidate.Command == LaunchCommand.Help) {
                HelpPanel.Visibility = HelpPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                if (HelpPanel.Visibility == Visibility.Visible) HelpScroll.ScrollToHome();
                Input.Clear(); FocusInput(); return;
            }
            var info = QuickLaunchCatalog.StartInfo(candidate);
            start(info);
            app.HideOverlay(restore: false);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException) {
            ShowError("열지 못했습니다. " + ex.Message); FocusInput();
        } finally { launching = false; }
    }
    private void ShowError(string message) { Status.Text = message; Status.Visibility = Visibility.Visible; }
}
