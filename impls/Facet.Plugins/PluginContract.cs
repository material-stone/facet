using System.Windows;

namespace Facet.Plugins;

// A skin can ask widgets for their HUD form; a widget without one simply keeps its normal view.
public static class WidgetTheme
{
    public const string HudKey = "Facet.HudWidgets";
    public static bool IsHud => Application.Current?.TryFindResource(HudKey) is true;
    public const string SoftHudKey = "Facet.SoftHudWidgets";
    // A HUD skin can ask for rounded plates and plain numbers instead of the tactical look (ADR 0090).
    public static bool IsSoftHud => IsHud && Application.Current?.TryFindResource(SoftHudKey) is true;
}

// Entry point: a public class with a parameterless constructor in the plugin.json assembly.
public interface IFacetPlugin
{
    void Register(IWidgetRegistry registry);
}

// A plugin that also implements IDisposable is disposed when it is turned off, reloaded or the app exits.
// It lives as long as it stays on, so it can own work that outlasts a widget, such as playback.
public interface IWidgetRegistry
{
    // The plugin's own folder and settings, for work that exists before any widget is placed.
    string Directory { get; }
    // Created by the app for files the user would miss, such as downloads. Outlives removing or replacing the plugin folder.
    string DataDirectory { get; }
    IPluginSettings Settings { get; }
    // The plugin's own encrypted file, for records too large for settings. It outlives the plugin's folder.
    IPluginStore Store { get; }
    void AddWidget(WidgetDescriptor widget);
    // Optional: offers a tool to connected agents while the plugin is on.
    void AddAgentTool(PluginAgentTool tool);
    // Optional: adds this plugin's section to the plugin settings. A later call replaces the section.
    void AddSettings(PluginSettingsSection section);
    // What the plugin may ask of the app itself (ADR 0084).
    IPluginShell Shell { get; }
    // Optional: takes over the launcher while its input matches.
    void AddLauncherMode(LauncherMode mode);
    // Optional: a global shortcut that works while the plugin is on.
    void AddHotkey(PluginHotkey hotkey);
    // Optional: an item in the tray menu.
    void AddTrayCommand(string label, Action run);
}

public interface IPluginShell
{
    // The app's data folder, for files that must keep their path across plugin versions.
    string DataRoot { get; }
    // True while the user wants records kept off the screen.
    bool Privacy { get; }
    // Hides the overlay and launcher; false when an unsaved draft kept them open.
    bool HideOverlay();
    // Opens the overlay on the tab that holds the widget; false when it is not placed on a visible tab.
    bool ShowWidget(string widgetId);
    // Opens this plugin's settings section.
    void OpenSettings();
    void ReportError(string message);
    // Rebinds one of this plugin's hotkeys. Returns why it was refused, or null; a refusal keeps the old shortcut.
    string? SetHotkey(string id, string shortcut);
}

public sealed record PluginHotkey(string Id, string Shortcut, Action Pressed);

// Create runs once per launcher view.
public sealed record LauncherMode(string Id, Func<ILauncherHost, ILauncherSession> Create);

public interface ILauncherHost
{
    string InputText { get; set; }
    void FocusInput();
    // One in-window flyout at a time; an outside click or Escape closes it without closing the launcher.
    void ShowFlyout(FrameworkElement anchor, FrameworkElement content, bool above = false);
    void CloseFlyout();
}

public interface ILauncherSession : IDisposable
{
    bool Matches(string input);
    // Shown above and below the unchanged centered input while the input matches.
    FrameworkElement Above { get; }
    FrameworkElement Below { get; }
    // Called on every input change while it matches. Active is false for a launcher that is not on screen.
    void Refresh(string input, bool active);
    // Plain Enter. The session reports its own failures.
    Task SubmitAsync(string input);
}

// Sizes are in grid cells. A view that implements IDisposable is disposed when its tile goes away.
// AllowMultiple is several on one tab; AllowMultipleTabs is placing on more than one tab.
// UsesData asks the app to create a logical store for each placed widget.
public sealed record WidgetDescriptor(string Id, string Title, Func<WidgetContext, FrameworkElement> Create)
{
    public string Description { get; init; } = "";
    public int DefaultColumns { get; init; } = 3;
    public int DefaultRows { get; init; } = 2;
    public int MinColumns { get; init; } = 1;
    public int MinRows { get; init; } = 1;
    public bool AllowMultiple { get; init; } = true;
    public bool AllowMultipleTabs { get; init; } = true;
    public bool UsesData { get; init; }
    // True wraps the view in the app's panel, drawn by the current skin; false shows only the view's own content.
    public bool HasPanel { get; init; }
    // HUD themes replace HasPanel with this: only a widget with a HUD form sits on a HudPanel.
    public bool HasHudPanel { get; init; }
}

// InstanceId is stable across restarts and differs per placement, even for the same widget on another tab.
// StorageDirectory is the plugin's own folder for its files. Data is null unless the descriptor set UsesData.
public sealed record WidgetContext(string InstanceId, string StorageDirectory)
{
    public string TabId { get; init; } = "";
    public IWidgetData? Data { get; init; }
    public IPluginSettings Settings { get; init; } = EmptyHost.Instance;
    public IFacetWorkspace Workspace { get; init; } = EmptyHost.Instance;
    public IWidgetModal Modal { get; init; } = EmptyHost.Instance;
    public IWidgetLayer Layer { get; init; } = EmptyHost.Instance;
}

// A full-overlay layer a widget owns, such as an editor with its own pickers: above the boards and below widget modals.
// The plugin draws the backdrop and closes it; the app only places it (ADR 0101).
public interface IWidgetLayer
{
    void Add(FrameworkElement layer);
    void Remove(FrameworkElement layer);
}

// Optional on a widget view. The app asks the views of the selected tab about Escape and keys, and every view before
// it hides the overlay, switches tabs or saves with Ctrl+S; false from TrySave keeps the overlay where it is.
public interface IWidgetInput
{
    bool HandleEscape();
    bool TrySave();
    bool HandleKey(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers);
}

// The app shows the view over the whole overlay; the plugin owns its content and state.
public interface IWidgetModal
{
    // Null when another modal is open or the view already has a parent.
    IModalHandle? Show(FrameworkElement content, ModalOptions? options = null);
}

public interface IModalHandle
{
    bool IsOpen { get; }
    // Closes without asking CanClose.
    void Close();
    // Raised once however the modal closes, after the view has left the overlay.
    event Action? Closed;
}

public sealed record ModalOptions
{
    // Asked before Escape, an outside click or hiding closes the modal; false keeps it open, for example after a failed save.
    public Func<bool>? CanClose { get; init; }
    // Gets Escape first; true means the content used it, for example to close its own drop-down.
    public Func<bool>? HandleEscape { get; init; }
}

// Read is null before the first write and throws when the file and its backups are unreadable. Write replaces the whole text atomically and returns false when it could not be saved.
public interface IPluginStore
{
    string? Read();
    bool Write(string text);
}

public interface IWidgetDataReader
{
    IReadOnlyCollection<string> Keys { get; }
    string? Get(string key);
}

// Saved in the app's records. Set and Remove return false when the value is rejected or could not be saved.
public interface IWidgetData : IWidgetDataReader
{
    bool Set(string key, string value);
    bool Remove(string key);
}

// Name is lowercase letters, digits and underscores. InputSchema is a JSON Schema; Run takes the arguments as JSON
// and returns the result as JSON on the UI thread. ArgumentException reports a bad request.
public sealed record PluginAgentTool(string Name, string Description, string InputSchema, Func<string, Task<string>> Run);

public sealed record TabInfo(string Id, string Title, bool Hidden);

// Data is another widget's store, readable only; null when that widget has none.
public sealed record PlacedWidget(string TabId, string InstanceId, string WidgetId, string PluginId, IWidgetDataReader? Data);

// Widget ids are global: "builtin.clock" or the value of WidgetIds.Of(pluginId, id).
public interface IFacetWorkspace
{
    IReadOnlyList<TabInfo> Tabs { get; }
    IReadOnlyList<PlacedWidget> WidgetsInTab(string tabId);
    PlacedWidget? Widget(string tabId, string instanceId);
    IReadOnlyList<PlacedWidget> WidgetsById(string widgetId);
}

public static class WidgetIds
{
    public static string Of(string pluginId, string widgetId) => "plugin." + pluginId + "." + widgetId;
}

// Values come from the app's records, or the field's default until the user changes them.
public interface IPluginSettings
{
    string Get(string key);
    double GetNumber(string key);
    IReadOnlyList<string> GetList(string key);
    // Saves a value the plugin manages itself, with no settings field. False when rejected or not saved.
    bool Save(string key, string value);
    // Raised on the UI thread with the changed key. Unsubscribe when the widget is disposed.
    event Action<string>? Changed;
}

public sealed record PluginSettingsSection(IReadOnlyList<SettingField> Fields);

public abstract record SettingField(string Key, string Label)
{
    public string Description { get; init; } = "";
    // Consecutive fields with the same group sit under one heading.
    public string Group { get; init; } = "";
}

public sealed record SliderSetting(string Key, string Label, double Minimum, double Maximum, double Default) : SettingField(Key, Label)
{
    public double Step { get; init; } = 1;
}
// Colors are "#RRGGBB".
public sealed record ColorSetting(string Key, string Label, string Default) : SettingField(Key, Label);
public sealed record TextSetting(string Key, string Label, string Default = "") : SettingField(Key, Label)
{
    // Asked before saving; a returned message is shown and the typed text stays unsaved.
    public Func<string, string?>? Check { get; init; }
}
// Saved as "true" or "false".
public sealed record ToggleSetting(string Key, string Label, bool Default = false) : SettingField(Key, Label);
// The plugin's own view. Stores nothing by itself; the view saves through the plugin's settings.
public sealed record CustomSetting(string Key, string Label, Func<FrameworkElement> Create) : SettingField(Key, Label);
// A list the user edits, one entry per line.
public sealed record TextListSetting(string Key, string Label, IReadOnlyList<string>? Default = null) : SettingField(Key, Label);
public sealed record SettingOption(string Value, string Label);
public sealed record ChoiceSetting(string Key, string Label, IReadOnlyList<SettingOption> Options, string Default) : SettingField(Key, Label);
// A button; the returned text is shown beside it. Stores nothing.
public sealed record ActionSetting(string Key, string Label, string ButtonText, Func<Task<string>> Run) : SettingField(Key, Label);

internal sealed class EmptyHost : IPluginSettings, IFacetWorkspace, IWidgetModal, IWidgetLayer
{
    public void Add(FrameworkElement layer) { }
    public void Remove(FrameworkElement layer) { }
    internal static readonly EmptyHost Instance = new();
    public IModalHandle? Show(FrameworkElement content, ModalOptions? options = null) => null;
    public string Get(string key) => "";
    public double GetNumber(string key) => 0;
    public IReadOnlyList<string> GetList(string key) => [];
    public bool Save(string key, string value) => false;
    public event Action<string>? Changed { add { } remove { } }
    public IReadOnlyList<TabInfo> Tabs => [];
    public IReadOnlyList<PlacedWidget> WidgetsInTab(string tabId) => [];
    public PlacedWidget? Widget(string tabId, string instanceId) => null;
    public IReadOnlyList<PlacedWidget> WidgetsById(string widgetId) => [];
}
