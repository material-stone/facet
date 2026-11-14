namespace Facet.Core;

// A placed widget's logical store inside the app's records; one per placement, so the same widget on two tabs has two.
public sealed class WidgetStore
{
    public string PluginId { get; set; } = "";
    public string WidgetId { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = [];
}

// App-managed plugin records. ADR 0075.
public static class PluginData
{
    public const int MaxKeys = 200, MaxKeyLength = 64, MaxValueLength = 65536;
    public const string WeatherPlugin = "facet.weather", WeatherRegionKey = "region";

    public static WidgetStore? Store(AppData data, string instanceId) => data.WidgetStores?.GetValueOrDefault(instanceId);

    public static WidgetStore EnsureStore(AppData data, string instanceId, string pluginId, string widgetId)
    {
        data.WidgetStores ??= [];
        if (!data.WidgetStores.TryGetValue(instanceId, out var store)) data.WidgetStores[instanceId] = store = new() { PluginId = pluginId, WidgetId = widgetId };
        return store;
    }

    public static void SetValue(AppData data, string instanceId, string pluginId, string widgetId, string key, string value)
    {
        Validate(key, value);
        var store = EnsureStore(data, instanceId, pluginId, widgetId);
        if (!store.Values.ContainsKey(key) && store.Values.Count >= MaxKeys) throw new ArgumentException("위젯 저장소의 항목 수를 넘었습니다.");
        store.Values[key] = value;
    }

    // A store lives as long as its placement; removing the widget or its tab removes the data.
    public static void PurgeStores(AppData data)
    {
        if (data.WidgetStores == null) return;
        var placed = WorkspaceLayouts.Current(data).Tabs.SelectMany(t => t.Widgets).Select(w => w.Id).ToHashSet();
        foreach (var id in data.WidgetStores.Keys.Where(id => !placed.Contains(id)).ToArray()) data.WidgetStores.Remove(id);
    }

    public static string? Setting(AppData data, string pluginId, string key) => data.PluginSettings?.GetValueOrDefault(pluginId)?.GetValueOrDefault(key);

    public static void SetSetting(AppData data, string pluginId, string key, string value)
    {
        Validate(key, value);
        data.PluginSettings ??= [];
        if (!data.PluginSettings.TryGetValue(pluginId, out var values)) data.PluginSettings[pluginId] = values = [];
        values[key] = value;
    }

    // The built-in weather widget and agent tool share the weather plugin's region, so one setting moves them all.
    public static string WeatherRegion(AppData data) => Setting(data, WeatherPlugin, WeatherRegionKey) ?? data.Settings.WeatherRegionId;

    // Drops malformed entries and carries the old native region setting over once.
    public static void Normalize(AppData data)
    {
        PluginIds.Rename(data);
        if (data.WidgetStores != null)
            foreach (var id in data.WidgetStores.Where(p => p.Value?.Values == null).Select(p => p.Key).ToArray()) data.WidgetStores.Remove(id);
        if (data.PluginSettings != null)
            foreach (var id in data.PluginSettings.Where(p => p.Value == null).Select(p => p.Key).ToArray()) data.PluginSettings.Remove(id);
        if (Setting(data, WeatherPlugin, WeatherRegionKey) == null && data.Settings.WeatherRegionId.Length > 0)
            SetSetting(data, WeatherPlugin, WeatherRegionKey, data.Settings.WeatherRegionId);
        MoveLegacyMusic(data);
        MoveConditionWidgets(data);
        MoveSleep(data);
        MoveChat(data);
    }

    // One-time move for records from when the music player was built in. Delete once no such records remain (ADR 0080).
    private static void MoveLegacyMusic(AppData data)
    {
        const string plugin = "facet.music", old = "builtin.music", widget = "plugin." + plugin + ".player";
        if (data.Music is { } music && Setting(data, plugin, "playlist") == null) {
            string saved = music.ToJsonString(new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            if (saved.Length <= MaxValueLength) SetSetting(data, plugin, "playlist", saved);
        }
        data.Music = null;
        if (data.Settings.MusicIndicatorVisible == false && Setting(data, plugin, "indicator") == null) SetSetting(data, plugin, "indicator", "off");
        data.Settings.MusicIndicatorVisible = null;
        foreach (var item in data.Workspace?.Tabs.SelectMany(t => t.Widgets) ?? [])
            if (item.WidgetId == old) item.WidgetId = widget;
    }

    // Placements from when the condition widgets were built in keep their place on the board (ADR 0081).
    private static void MoveConditionWidgets(AppData data)
    {
        foreach (var item in data.Workspace?.Tabs.SelectMany(t => t.Widgets) ?? [])
            item.WidgetId = item.WidgetId switch { "builtin.condition" => "plugin." + LegacyConditions.Plugin + ".current", "builtin.condition-history" => "plugin." + LegacyConditions.Plugin + ".history", _ => item.WidgetId };
    }

    // Placements and the background setting from when sleep was built in; the records themselves move in Store (ADR 0083).
    private static void MoveSleep(AppData data)
    {
        if (data.Settings.SleepGraphTransparencyPercent is double transparency) {
            if (transparency > 0 && double.IsFinite(transparency) && Setting(data, LegacySleep.Plugin, LegacySleep.TransparencyKey) == null)
                SetSetting(data, LegacySleep.Plugin, LegacySleep.TransparencyKey, Math.Min(transparency, 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
            data.Settings.SleepGraphTransparencyPercent = null;
        }
        // A never-edited layout used to show the graph by default; someone with records keeps it.
        if (data.Workspace == null && data.Sleep.Count > 0) {
            var layout = WorkspaceLayouts.Default();
            layout.GridVersion = 3;
            layout.Tabs[0].Widgets.Add(new() { Id = "builtin.sleep", WidgetId = "builtin.sleep", X = -6, Y = -1, Width = 9, Height = 3 });
            data.Workspace = WorkspaceLayouts.Normalize(layout);
        }
        foreach (var item in data.Workspace?.Tabs.SelectMany(t => t.Widgets) ?? [])
            if (item.WidgetId == "builtin.sleep") item.WidgetId = LegacySleep.DurationWidget;
    }

    // Settings and the layout from when chat was built in; the chat references move in Store (ADR 0084).
    private static void MoveChat(AppData data)
    {
        var s = data.Settings;
        void Move(string key, object? value) {
            string? text = value switch { bool flag => flag ? "true" : "false", string word => word, _ => null };
            if (!string.IsNullOrEmpty(text) && Setting(data, LegacyChat.Plugin, key) == null) SetSetting(data, LegacyChat.Plugin, key, text);
        }
        // A setting stays absent when it was false or empty, which is also its default in the plugin.
        Move("agent", s.ChatAgent); Move("codexEnabled", s.CodexChatEnabled); Move("codexContextThread", s.CodexContextThreadId); Move("codexModel", s.CodexChatModel);
        Move("codexEffort", s.CodexChatEffort); Move("codexFast", s.CodexChatFast); Move("claudeEnabled", s.ClaudeChatEnabled); Move("claudeModel", s.ClaudeChatModel);
        Move("voiceHotkey", s.VoiceHotkey); Move("voiceMicrophone", s.VoiceMicrophone); Move("voiceReply", s.VoiceReplyMode);
        Move("whisperServer", s.WhisperServerPath); Move("whisperModel", s.WhisperModelPath); Move("voiceSpeech", s.VoiceSpeechVoice);
        bool used = s.CodexChatEnabled == true || s.ClaudeChatEnabled == true || data.AgentChatReferences.Count > 0;
        s.ChatAgent = s.CodexContextThreadId = s.CodexChatProjectId = s.CodexChatModel = s.CodexChatEffort = s.ClaudeChatModel = s.VoiceHotkey = s.VoiceMicrophone = s.VoiceReplyMode = s.WhisperServerPath = s.WhisperModelPath = s.VoiceSpeechVoice = null;
        s.CodexChatEnabled = s.CodexChatFast = s.ClaudeChatEnabled = null;
        // A never-edited layout used to show the chat page; someone who used it keeps it.
        if (data.Workspace == null && used) {
            var layout = WorkspaceLayouts.Default();
            layout.GridVersion = 4;
            data.Workspace = WorkspaceLayouts.Normalize(layout);
        }
    }

    private static void Validate(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength) throw new ArgumentException("저장 키가 올바르지 않습니다.");
        if (value == null || value.Length > MaxValueLength) throw new ArgumentException("저장 값이 너무 큽니다.");
    }
}
