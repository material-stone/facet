using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Facet.Plugins;

namespace Facet;

// The app side of the plugin contract: tab and widget lookup, widget stores and plugin settings, all over AppData (ADR 0075).
// The Controller owns it: running plugins hold its settings objects across overlay rebuilds.
internal sealed class PluginRuntime(Func<AppData> data, Func<Action<AppData>, bool> commit) : IFacetWorkspace
{
    internal Func<string, string?> PluginOf = _ => null;
    private static readonly Regex Color = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, SettingsAccess> settings = [];
    internal Action<string, string>? SettingChanged;
    private AppData Current => data();
    private bool Commit(Action<AppData> change) => commit(change);

    public IReadOnlyList<TabInfo> Tabs => WorkspaceLayouts.Current(data()).Tabs.Select(t => new TabInfo(t.Id, t.Title, t.Hidden)).ToArray();

    public IReadOnlyList<PlacedWidget> WidgetsInTab(string tabId) =>
        WorkspaceLayouts.Current(data()).Tabs.Where(t => t.Id == tabId).SelectMany(t => t.Widgets.Select(w => Describe(t.Id, w))).ToArray();

    public PlacedWidget? Widget(string tabId, string instanceId) => WidgetsInTab(tabId).FirstOrDefault(w => w.InstanceId == instanceId);

    public IReadOnlyList<PlacedWidget> WidgetsById(string widgetId) =>
        WorkspaceLayouts.Current(data()).Tabs.SelectMany(t => t.Widgets.Where(w => w.WidgetId == widgetId).Select(w => Describe(t.Id, w))).ToArray();

    private PlacedWidget Describe(string tabId, WidgetPlacement item)
    {
        var store = PluginData.Store(data(), item.Id);
        return new(tabId, item.Id, item.WidgetId, PluginOf(item.WidgetId) ?? store?.PluginId ?? "", store == null ? null : new Reader(this, item.Id));
    }

    internal IWidgetData DataFor(string instanceId, string pluginId, string widgetId) => new Writer(this, instanceId, pluginId, widgetId);

    private WidgetStore? Store(string instanceId) => PluginData.Store(data(), instanceId);

    // A separate type from Writer, so another widget's store cannot be cast to something writable.
    private sealed class Reader(PluginRuntime host, string instanceId) : IWidgetDataReader
    {
        public IReadOnlyCollection<string> Keys => host.Store(instanceId)?.Values.Keys.ToArray() ?? [];
        public string? Get(string key) => host.Store(instanceId)?.Values.GetValueOrDefault(key);
    }

    private sealed class Writer(PluginRuntime host, string instanceId, string pluginId, string widgetId) : IWidgetData
    {
        public IReadOnlyCollection<string> Keys => host.Store(instanceId)?.Values.Keys.ToArray() ?? [];
        public string? Get(string key) => host.Store(instanceId)?.Values.GetValueOrDefault(key);

        public bool Set(string key, string value)
        {
            try { PluginData.SetValue(new AppData(), instanceId, pluginId, widgetId, key, value); }
            catch (ArgumentException) { return false; }
            if (host.Store(instanceId) is { } store && !store.Values.ContainsKey(key) && store.Values.Count >= PluginData.MaxKeys) return false;
            return host.Commit(d => PluginData.SetValue(d, instanceId, pluginId, widgetId, key, value));
        }

        public bool Remove(string key) => host.Store(instanceId)?.Values.ContainsKey(key) == true && host.Commit(d => PluginData.Store(d, instanceId)?.Values.Remove(key));
    }

    internal IPluginSettings SettingsFor(string pluginId)
    {
        if (!settings.TryGetValue(pluginId, out var access)) settings[pluginId] = access = new(this, pluginId);
        return access;
    }

    internal void SetSection(string pluginId, PluginSettingsSection? section) => ((SettingsAccess)SettingsFor(pluginId)).Section = section;
    internal PluginSettingsSection? Section(string pluginId) => settings.GetValueOrDefault(pluginId)?.Section;
    internal string ReadSetting(string pluginId, string key) => SettingsFor(pluginId).Get(key);

    // Checks the value against the field the plugin declared before anything is stored.
    internal bool WriteSetting(string pluginId, SettingField field, string value)
    {
        string? valid = field switch {
            SliderSetting slider => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)
                ? Math.Clamp(number, slider.Minimum, slider.Maximum).ToString(CultureInfo.InvariantCulture) : null,
            ColorSetting => Color.IsMatch(value) ? value : null,
            ChoiceSetting choice => choice.Options.Any(o => o.Value == value) ? value : null,
            ToggleSetting => value is "true" or "false" ? value : null,
            ActionSetting or CustomSetting => null,
            _ => value
        };
        if (valid == null || valid.Length > PluginData.MaxValueLength) return false;
        if (ReadSetting(pluginId, field.Key) == valid) return true;
        if (!commit(d => PluginData.SetSetting(d, pluginId, field.Key, valid))) return false;
        SettingChanged?.Invoke(pluginId, field.Key);
        ((SettingsAccess)SettingsFor(pluginId)).Raise(field.Key);
        return true;
    }

    private sealed class SettingsAccess(PluginRuntime host, string pluginId) : IPluginSettings
    {
        internal PluginSettingsSection? Section;
        public event Action<string>? Changed;

        public string Get(string key) => PluginData.Setting(host.Current, pluginId, key) ?? Section?.Fields.FirstOrDefault(f => f.Key == key) switch {
            SliderSetting slider => slider.Default.ToString(CultureInfo.InvariantCulture),
            ColorSetting color => color.Default,
            TextSetting text => text.Default,
            TextListSetting list => string.Join("\n", list.Default ?? []),
            ChoiceSetting choice => choice.Default,
            ToggleSetting toggle => toggle.Default ? "true" : "false",
            _ => ""
        };

        public double GetNumber(string key) => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : 0;
        public IReadOnlyList<string> GetList(string key) => Get(key).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public bool Save(string key, string value)
        {
            try { PluginData.SetSetting(new AppData(), pluginId, key, value); }
            catch (ArgumentException) { return false; }
            return host.Commit(d => PluginData.SetSetting(d, pluginId, key, value));
        }

        // One failing plugin handler must not stop the settings save or the other widgets.
        internal void Raise(string key)
        {
            foreach (var handler in Changed?.GetInvocationList() ?? []) {
                try { ((Action<string>)handler)(key); }
                catch (Exception ex) { Trace.TraceWarning("Plugin settings handler failed: " + ex.Message); }
            }
        }
    }
}
