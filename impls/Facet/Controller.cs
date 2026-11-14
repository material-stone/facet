using System.Windows.Interop;
using System.Windows.Threading;
using Facet.Deployment;
using Forms = System.Windows.Forms;

namespace Facet;
public sealed class Controller : IDisposable
{
    public const string ExitHotkey = "Win+Alt+End";
    public const string QuickLaunchHotkey = "Win+Alt+Space";
    internal string ExitShortcut { get; }
    internal string QuickLaunchShortcut { get; }
    private static readonly int[] HotkeyIds = [103, 104, 105];
    private const int FirstPluginHotkey = 200;
    private readonly Dictionary<int, Action> pluginHotkeys = [];
    // A shortcut the user changed while the plugin runs; registered shortcuts of plugins come from here first.
    private readonly Dictionary<string, string> hotkeyOverrides = [];
    public AppData Data { get; private set; }
    public Store Store { get; }
    internal Dispatcher Dispatcher => Overlay.Dispatcher;
    public string? LastError { get; private set; }
    public OverlayWindow Overlay { get; }
    internal PluginHost Plugins { get; }
    internal PluginRuntime PluginRuntime { get; }
    public string HotkeyStatus { get; private set; } = "";
    public bool Exiting { get; private set; }
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private readonly DispatcherTimer timer;
    private HwndSource? source;
    private IntPtr handle;
    private ToastWindow? toast;
    private ReminderPipeServer? reminderServer;
    private AgentToolHost? agentTools;
    private bool disposed;
    private bool exitRequested;
    internal bool ExitPending => exitRequested;
    private IntPtr overlayReturnWindow;
    private bool overlayRestoreAllowed = true;
    private string[] registered = [];
    private readonly string? bundledPluginError;
    public Controller(string root) : this(root, ExitHotkey) { }
    internal Controller(string root, string exitShortcut, string? quickLaunchShortcut = null)
    {
        ExitShortcut = exitShortcut;
        QuickLaunchShortcut = quickLaunchShortcut ?? QuickLaunchHotkey;
        Store = new(root); Data = Store.Load();
        Design.Skins.UseFolder(Path.Combine(root, "skins"));
        Design.Skins.Apply(Application.Current, Data.Settings.Skin);
        // Current visibility never controls startup; use ShowOverlayOnLaunch and launch origin.
        Data.Settings.OverlayVisible = false;
        string? deployment = LaunchProtocol.FindLauncher() is { } packaged ? Path.GetDirectoryName(packaged) : null;
        bundledPluginError = PortablePlugins.Install(deployment, root) ?? PortablePlugins.UpgradeRenamed(deployment, root);
        Plugins = new(root, id => new PluginShell(this, id));
        Plugins.Changed += RegisterPluginKeys;
        PluginRuntime = new(() => Data, Commit);
        Overlay = new(this);
        trayIcon = Design.AppIcon.CreateTrayIcon();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = LaunchProtocol.DisplayName, Visible = true };
        var menu = Design.Typography.CreateTrayMenu();
        menu.Items.Add("오버레이 열기", null, (_, _) => ResetOverlay());
        menu.Items.Add($"빠른 실행 ({QuickLaunchHotkey})", null, (_, _) => ToggleQuickLaunch());
        var pluginCommands = new Forms.ToolStripSeparator();
        menu.Items.Add(pluginCommands);
        // Plugins come and go while the app runs, so their commands are listed each time the menu opens.
        menu.Opening += (_, _) => {
            int at = menu.Items.IndexOf(pluginCommands);
            while (at > 2) { menu.Items.RemoveAt(2); at--; }
            foreach (var (label, run) in Plugins.TrayCommands) menu.Items.Insert(at++, new Forms.ToolStripMenuItem(label, null, (_, _) => RunPluginAction(run)));
        };
        menu.Items.Add($"{LaunchProtocol.DisplayName} 종료 ({ExitShortcut})", null, (_, _) => Exit());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ResetOverlay();
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => Tick();
    }
    public void Start(bool open)
    {
        handle = new WindowInteropHelper(Overlay).EnsureHandle();
        source = HwndSource.FromHwnd(handle); source.AddHook(Hook);
        if (Facet.Deployment.LaunchProtocol.FindLauncher() is { } launcher) TaskbarIdentity.Configure(handle, launcher);
        HotkeyStatus = RegisterKeys(Data.Settings);
        if (HotkeyStatus.Contains("실패")) ReportError(HotkeyStatus);
        RegisterPluginKeys();
        ApplyDisplay();
        HandleLaunch(open);
        if (Store.RecoveryMessage != null) ReportError(Store.RecoveryMessage);
        if (bundledPluginError != null) ReportError(bundledPluginError);
        timer.Start(); Tick();
        reminderServer ??= new ReminderPipeServer(Store.Root, Dispatcher, HandleReminderCommand);
        agentTools ??= new AgentToolHost(this);
    }
    internal void HandleLaunch(bool mayOpen)
    {
        _ = Overlay.RefreshWeatherOnLaunchAsync();
        if (!mayOpen) return;
        if (Data.Settings.ShowOverlayOnLaunch) ResetOverlay();
        else
        {
            toast?.Close();
            toast = new ToastWindow($"{LaunchProtocol.DisplayName}이 실행되었습니다", "", ResetOverlay, startup: true);
            toast.Show();
        }
    }
    internal void ReportError(string message)
    {
        LastError = message;
        tray.ShowBalloonTip(5000, LaunchProtocol.DisplayName, message, Forms.ToolTipIcon.Warning);
    }
    public bool Save()
    {
        try { Store.Save(Data); LastError = null; return true; }
        catch (Exception ex) { LastError = "저장하지 못했습니다. 입력을 유지하고 다시 시도해 주세요. " + ex.Message; return false; }
    }
    public bool Commit(Action<AppData> change)
    {
        var before = System.Text.Json.JsonSerializer.Serialize(Data);
        change(Data);
        if (Save()) { Tick(false); return true; }
        Data = System.Text.Json.JsonSerializer.Deserialize<AppData>(before)!; return false;
    }

    // Applies a validated agent change and saves it; validation errors leave the loaded data untouched.
    internal T Change<T>(Func<AppData, T> change)
    {
        var before = System.Text.Json.JsonSerializer.Serialize(Data);
        T result;
        try { result = change(Data); }
        catch { Data = System.Text.Json.JsonSerializer.Deserialize<AppData>(before)!; throw; }
        if (Save()) { Tick(false); return result; }
        Data = System.Text.Json.JsonSerializer.Deserialize<AppData>(before)!;
        throw new IOException("저장하지 못해 변경을 취소했습니다.");
    }

    internal ReminderReply HandleReminderCommand(ReminderCommand command)
    {
        if (disposed || Exiting || ExitPending || !Overlay.IsEnabled) return new(false, "busy", "앱이 종료 또는 저장 확인 중입니다.");
        try {
            var candidate = new AppData { Reminders = Data.Reminders.Select(x => x with { }).ToList() };
            var reply = UserReminders.Apply(candidate, command);
            if (!reply.Ok || !reply.Changed) return reply;
            return Commit(data => data.Reminders = candidate.Reminders) ? reply : new(false, "save_failed", "저장하지 못해 변경을 취소했습니다.");
        }
        catch (ArgumentException ex) { return new(false, "invalid_request", ex.Message); }
    }
    public void ToggleOverlay()
    {
        if (Overlay.IsQuickLaunch && Data.Settings.OverlayVisible) {
            if (!Overlay.TrySaveEdits()) return;
            Overlay.SetQuickLaunch(false); ApplyDisplay(); Overlay.Activate(); return;
        }
        if (Data.Settings.OverlayVisible) { HideOverlay(); return; }
        Overlay.SetQuickLaunch(false);
        Commit(d => d.Settings.OverlayVisible = true);
        ApplyDisplay();
    }
    public void HideOverlay(bool restore = true)
    {
        if (!Overlay.TrySaveEdits()) return;
        bool previousRestore = overlayRestoreAllowed;
        overlayRestoreAllowed = restore;
        if (!Commit(d => d.Settings.OverlayVisible = false)) { overlayRestoreAllowed = previousRestore; return; }
        ApplyDisplay();
    }
    internal void OverlayDismissed(bool restore)
    {
        if (restore && overlayRestoreAllowed && !disposed && !Exiting && !Overlay.IsActive) Native.RestoreForeground(overlayReturnWindow);
    }
    public void ToggleQuickLaunch()
    {
        if (Overlay.IsQuickLaunch && Data.Settings.OverlayVisible) { HideOverlay(); return; }
        if (!Overlay.TrySaveEdits()) return;
        Overlay.SetQuickLaunch(true);
        if (!Data.Settings.OverlayVisible && !Commit(d => d.Settings.OverlayVisible = true)) { Overlay.SetQuickLaunch(false); return; }
        ApplyDisplay(); Overlay.Activate(); Overlay.FocusQuickLaunch();
    }
    public void ResetOverlay()
    {
        if (!Overlay.TrySaveEdits()) return;
        Overlay.SetQuickLaunch(false);
        if (!Data.Settings.OverlayVisible) ToggleOverlay();
        else { Native.CoverScreen(Overlay); ApplyDisplay(); Overlay.Activate(); }
    }
    public void ApplyDisplay()
    {
        var s = Data.Settings;
        if (s.OverlayVisible && !Overlay.IsVisible)
        {
            overlayReturnWindow = Native.ForegroundWindow;
            overlayRestoreAllowed = true;
            Native.CoverScreen(Overlay, true);
        }
        else if (Overlay.IsVisible) Native.CoverScreen(Overlay);
        Overlay.Present(s.OverlayVisible, OverlayAppearance.Strength(s), s.OverlayBlurRadius, s.OverlayShowMilliseconds, s.OverlayHideMilliseconds, OverlayAppearance.BackgroundColor(s));
    }
    public void Tick(bool notifications = true)
    {
        ApplyDisplay();
        if (!notifications) return;
        if (toast?.IsVisible == true) return;
        var now = DateTime.Now;
        var custom = UserReminders.Due(Data, DateTimeOffset.Now);
        if (custom != null) {
            bool hidden = Data.Settings.HideFullscreen && Native.FullscreenOther();
            if (!Commit(data => {
                UserReminders.MarkDelivered(data, custom.Id, DateTimeOffset.Now);
                data.Notices.Insert(0, new() { At = now, Text = custom.Title + (hidden ? " · 전체 화면 중 생략" : "") });
                if (data.Notices.Count > 100) data.Notices.RemoveRange(100, data.Notices.Count - 100);
            })) return;
            if (!hidden) ShowToast(custom.Title, custom.Body, ResetOverlay);
            return;
        }
    }
    private void ShowToast(string title, string body, Action open)
    {
        if (Data.Settings.Privacy) { title = $"{LaunchProtocol.DisplayName} 알림"; body = "오버레이에서 알림을 확인해 주세요."; }
        toast?.Close(); toast = new ToastWindow(title, body, open); toast.Show();
    }
    // Plugin code can throw anything; a hotkey or tray item must not take the app down.
    private void RunPluginAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { ReportError("플러그인 명령을 실행하지 못했습니다. " + ex.Message); }
    }
    // Plugin shortcuts sit apart from the app's own, so one bad shortcut only loses itself (ADR 0084).
    private void RegisterPluginKeys()
    {
        if (handle == IntPtr.Zero) return;
        foreach (int id in pluginHotkeys.Keys) Native.UnregisterHotKey(handle, id);
        pluginHotkeys.Clear();
        int next = FirstPluginHotkey;
        foreach (var (plugin, hotkey) in Plugins.Hotkeys) {
            int id = next++;
            string shortcut = hotkeyOverrides.GetValueOrDefault(plugin + "/" + hotkey.Id, hotkey.Shortcut);
            if (HotkeyProblem(id, shortcut) is { } problem) { ReportError(problem); continue; }
            pluginHotkeys[id] = hotkey.Pressed;
        }
    }
    // Registers it, or says why it could not.
    private string? HotkeyProblem(int id, string shortcut)
    {
        (uint modifiers, uint key) parsed;
        try { parsed = Native.ParseHotkey(shortcut); }
        catch (ArgumentException ex) { return ex.Message; }
        if (Native.RegisterHotKey(handle, id, parsed.modifiers, parsed.key)) return null;
        return $"{shortcut} 등록 실패: {new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()).Message}";
    }
    // A refusal keeps the shortcut that works now.
    internal string? SetPluginHotkey(string plugin, string id, string shortcut)
    {
        var known = Plugins.Hotkeys.Select((item, index) => (item, Slot: FirstPluginHotkey + index)).FirstOrDefault(entry => entry.item.PluginId == plugin && entry.item.Hotkey.Id == id);
        if (known.item.Hotkey == null) return "등록되지 않은 단축키입니다.";
        (uint modifiers, uint key) parsed;
        try { parsed = Native.ParseHotkey(shortcut); }
        catch (ArgumentException ex) { return ex.Message; }
        if (registered.Any(key => Native.ParseHotkey(key) == parsed)) return "앱 단축키와 다르게 지정하세요.";
        string overrideKey = plugin + "/" + id;
        string previous = hotkeyOverrides.GetValueOrDefault(overrideKey, known.item.Hotkey.Shortcut);
        if (handle == IntPtr.Zero) { hotkeyOverrides[overrideKey] = shortcut; return null; }
        Native.UnregisterHotKey(handle, known.Slot);
        if (HotkeyProblem(known.Slot, shortcut) is { } problem) {
            if (pluginHotkeys.ContainsKey(known.Slot)) HotkeyProblem(known.Slot, previous);
            return problem;
        }
        hotkeyOverrides[overrideKey] = shortcut;
        pluginHotkeys[known.Slot] = known.item.Hotkey.Pressed;
        return null;
    }
    // Opens the overlay on the tab that holds the widget.
    internal bool ShowWidget(string widgetId)
    {
        ResetOverlay();
        return Data.Settings.OverlayVisible && !Overlay.IsQuickLaunch && Overlay.ShowWidget(widgetId);
    }
    internal void OpenPluginSettings(string pluginId)
    {
        ResetOverlay();
        if (Data.Settings.OverlayVisible && !Overlay.IsQuickLaunch) Overlay.OpenPluginSettings(pluginId);
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312)
        {
            int id = wParam.ToInt32();
            if (id == 103) ToggleOverlay();
            else if (id == 104) Dispatcher.BeginInvoke(Exit);
            else if (id == 105) ToggleQuickLaunch();
            else if (pluginHotkeys.TryGetValue(id, out var pressed)) RunPluginAction(pressed);
            handled = true;
        }
        if (msg == 0x007E) Dispatcher.BeginInvoke(() => { if (Overlay.IsVisible) Native.CoverScreen(Overlay); });
        return IntPtr.Zero;
    }
    public string RegisterKeys(Settings s)
    {
        string[] keys = [s.OverlayHotkey, ExitShortcut, QuickLaunchShortcut];
        var parsed = keys.Select(Native.ParseHotkey).ToArray();
        if (parsed[0] == parsed[1]) throw new ArgumentException($"{ExitShortcut}는 앱 종료 전용입니다. 다른 단축키를 지정하세요.");
        if (parsed[0] == parsed[2]) throw new ArgumentException($"{QuickLaunchShortcut}는 빠른 실행 전용입니다. 다른 단축키를 지정하세요.");
        if (parsed.Distinct().Count() != HotkeyIds.Length) throw new ArgumentException("단축키를 서로 다르게 지정하세요.");
        for (int i = 0; i < HotkeyIds.Length; i++) Native.UnregisterHotKey(handle, HotkeyIds[i]);
        for (int i = 0; i < HotkeyIds.Length; i++)
        {
            if (Native.RegisterHotKey(handle, HotkeyIds[i], parsed[i].modifiers, parsed[i].key)) continue;
            int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            for (int j = 0; j < HotkeyIds.Length; j++) Native.UnregisterHotKey(handle, HotkeyIds[j]);
            for (int j = 0; j < registered.Length; j++) { var old = Native.ParseHotkey(registered[j]); Native.RegisterHotKey(handle, HotkeyIds[j], old.modifiers, old.key); }
            string advice = i is 1 or 2 ? "다른 앱에서 이 고정 단축키를 사용 중인지 확인하세요." : "설정에서 바꾸세요.";
            return $"{keys[i]} 등록 실패: {new System.ComponentModel.Win32Exception(error).Message} (오류 {error}). {advice} 트레이는 계속 사용할 수 있습니다.";
        }
        registered = keys;
        return $"전역 단축키 {HotkeyIds.Length}개가 등록되었습니다. 앱 종료: {ExitShortcut}";
    }
    public bool ApplySettings(Settings next)
    {
        bool weatherChanged = Data.Settings.WeatherRegionId != next.WeatherRegionId;
        bool skinChanged = Design.Skins.Resolve(Data.Settings.Skin) != Design.Skins.Resolve(next.Skin);
        string result = RegisterKeys(next);
        HotkeyStatus = result;
        if (result.Contains("실패")) { LastError = result; return false; }
        if (!Commit(d => d.Settings = next)) { RegisterKeys(Data.Settings); return false; }
        ApplyDisplay();
        Overlay.ApplyThemeOptions();
        if (weatherChanged) Overlay.ReloadWeatherLocation();
        // The settings view that is saving gets replaced, so the switch waits until this save has returned.
        if (skinChanged) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!Exiting && !disposed) Overlay.SwitchSkin(Data.Settings.Skin); }));
        return true;
    }
    // Also reloads the skin in use, because an update keeps its id.
    internal bool ApplySkin(string id)
    {
        Design.Skins.Rescan();
        if (Design.Skins.Resolve(id) != id) return false;
        if (Design.Skins.Resolve(Data.Settings.Skin) == id) {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!Exiting && !disposed) Overlay.SwitchSkin(id); }));
            return true;
        }
        var next = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(Data.Settings))!;
        next.Skin = id;
        return ApplySettings(next);
    }
    public void Exit()
    {
        if (Exiting || exitRequested || !Overlay.IsEnabled) return;
        exitRequested = true;
        try
        {
            if (!Overlay.TrySaveEdits()) return;
            Exiting = true; Application.Current.Shutdown();
        }
        finally { exitRequested = false; }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; timer.Stop(); Overlay.StopWeatherUpdates(); reminderServer?.Dispose(); agentTools?.Dispose(); toast?.Close();
        Plugins.Dispose();
        for (int i = 0; i < HotkeyIds.Length; i++) Native.UnregisterHotKey(handle, HotkeyIds[i]);
        foreach (int id in pluginHotkeys.Keys) Native.UnregisterHotKey(handle, id);
        source?.RemoveHook(Hook); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); trayIcon.Dispose();
    }
}
