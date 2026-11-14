namespace Facet.Core;

// The first-party plugins were published under the app's old name; their records move to the facet.* ids once (ADR 0096).
public static class PluginIds
{
    public const string Prefix = "facet.", RenamedPrefix = "teum.";
    private const string WidgetPrefix = "plugin.";

    public static string Current(string id) => id.StartsWith(RenamedPrefix, StringComparison.Ordinal) ? Prefix + id[RenamedPrefix.Length..] : id;

    public static string CurrentWidget(string widgetId) =>
        widgetId.StartsWith(WidgetPrefix + RenamedPrefix, StringComparison.Ordinal) ? WidgetPrefix + Current(widgetId[WidgetPrefix.Length..]) : widgetId;

    // Grants, settings, placements and widget stores. A value already saved under the new id wins.
    public static void Rename(AppData data)
    {
        if (data.EnabledPlugins != null)
            data.EnabledPlugins = data.EnabledPlugins.Where(g => g != null).Select(g => { g.Id = Current(g.Id); return g; }).GroupBy(g => g.Id).Select(g => g.Last()).ToList();
        if (data.PluginSettings != null)
            foreach (var old in data.PluginSettings.Keys.Where(k => k != Current(k)).ToArray()) {
                var values = data.PluginSettings[old];
                data.PluginSettings.Remove(old);
                if (values == null) continue;
                if (!data.PluginSettings.TryGetValue(Current(old), out var kept) || kept == null) data.PluginSettings[Current(old)] = values;
                else foreach (var (key, value) in values) kept.TryAdd(key, value);
            }
        foreach (var item in data.Workspace?.Tabs.SelectMany(t => t.Widgets) ?? []) item.WidgetId = CurrentWidget(item.WidgetId);
        foreach (var store in data.WidgetStores?.Values ?? Enumerable.Empty<WidgetStore>()) {
            if (store == null) continue;
            store.PluginId = Current(store.PluginId);
            store.WidgetId = CurrentWidget(store.WidgetId);
        }
    }
}
