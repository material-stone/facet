namespace Facet.Core;

public sealed class AppData
{
    public int SchemaVersion { get; set; } = 2;
    public ExecutionPlan? Plan { get; set; }
    public string RoadmapGoal { get; set; } = "이번 주 주력 게임의 첫 플레이를 개선하고 사용자 반응 확인하기";
    public Settings Settings { get; set; } = new();
    public List<SleepEntry> Sleep { get; set; } = [];
    public List<WorkItem> Work { get; set; } = [];
    public List<ConnectedProject> Projects { get; set; } = [];
    public List<ProjectJournalEntry> ProjectJournals { get; set; } = [];
    public List<CalendarDayEntry> CalendarDays { get; set; } = [];
    // Missing until the user first edits presets; until then presets derive from used schedule categories.
    public List<SchedulePreset>? SchedulePresets { get; set; }
    // Missing until the user first edits tabs or widgets; WorkspaceLayouts.Default applies until then. ADR 0073.
    public WorkspaceLayout? Workspace { get; set; }
    public List<PluginGrant>? EnabledPlugins { get; set; }
    // Keyed by widget placement id, then plugin id. ADR 0075.
    public Dictionary<string, WidgetStore>? WidgetStores { get; set; }
    public Dictionary<string, Dictionary<string, string>>? PluginSettings { get; set; }
    public List<UserReminder> Reminders { get; set; } = [];
    public List<ConditionEntry> Conditions { get; set; } = [];
    public List<AgentChatReference> AgentChatReferences { get; set; } = [];
    // Legacy input only; PluginData moves it to the music plugin's settings on load.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.Nodes.JsonObject? Music { get; set; }
    public Dictionary<string, DateTime> Delivered { get; set; } = [];
    public List<NoticeLog> Notices { get; set; } = [];
    public List<SleepEntry> RemovedSleep { get; set; } = [];
}

public enum WebSearchEngine { Google, Naver, Bing }

public sealed class Settings
{
    public string Wake { get; set; } = "12:00";
    public string Bed { get; set; } = "02:00";
    public int RecordDelay { get; set; } = 15;
    public int CaffeineMinutes { get; set; } = 360;
    public int WrapMinutes { get; set; } = 90;
    public int WindMinutes { get; set; } = 60;
    public int HygieneMinutes { get; set; } = 15;
    public bool Notifications { get; set; } = true;
    public bool CaffeineEnabled { get; set; } = true;
    public bool WrapEnabled { get; set; } = true;
    public bool WindEnabled { get; set; } = true;
    public bool HygieneEnabled { get; set; } = true;
    public bool OverlayVisible { get; set; } = true;
    public bool ShowOverlayOnLaunch { get; set; } = true;
    public string WeatherRegionId { get; set; } = "";
    public bool OverlayTopmost { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool HideFullscreen { get; set; } = true;
    public bool HideAtNight { get; set; }
    // Legacy input only; see AppData.Music.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? MusicIndicatorVisible { get; set; }
    public bool Privacy { get; set; }
    public double Opacity { get; set; } = .95;
    public double? OverlayTransparencyPercent { get; set; }
    // Legacy input only; PluginData moves it to the sleep plugin's settings on load.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? SleepGraphTransparencyPercent { get; set; }
    public string OverlayBackgroundColor { get; set; } = "#141A1D";
    public string Skin { get; set; } = "default";
    // "<skin>.<option>"; a missing entry keeps the default the theme declares.
    public Dictionary<string, bool> SkinOptions { get; set; } = [];
    public double OverlayBlurRadius { get; set; } = 18;
    public int OverlayShowMilliseconds { get; set; } = 280;
    public int OverlayHideMilliseconds { get; set; } = 280;
    public double OverlayWidth { get; set; } = 350;
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    public string SleepHotkey { get; set; } = "Ctrl+Alt+S";
    public string WorkHotkey { get; set; } = "Ctrl+Alt+P";
    public string OverlayHotkey { get; set; } = "Win+Alt+O";
    public string QuickLaunchHotkey { get; set; } = "Win+Alt+Space";
    public string ExitHotkey { get; set; } = "Win+Alt+End";
    public Dictionary<string, string> LaunchAliases { get; set; } = [];
    public WebSearchEngine LauncherSearchEngine { get; set; } = WebSearchEngine.Google;
    // Input only: PluginData.MoveChat carries these to the chat plugin once (ADR 0084).
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? CodexChatEnabled { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CodexContextThreadId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CodexChatProjectId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CodexChatModel { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CodexChatEffort { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? CodexChatFast { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ChatAgent { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? ClaudeChatEnabled { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ClaudeChatModel { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? VoiceHotkey { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? VoiceMicrophone { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? VoiceReplyMode { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? WhisperServerPath { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? WhisperModelPath { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? VoiceSpeechVoice { get; set; }
    public string TherapyNote { get; set; } = "수면병원에 체중 변화와 반복 각성을 알리고 현재 치료 계획을 상담하기.";
    public DateOnly PlanStart { get; set; } = DateOnly.FromDateTime(DateTime.Now);
}

// Input only: the shape of sleep records from when they lived in records.dat.
public sealed class SleepEntry
{
    public SleepEntry Copy() => (SleepEntry)MemberwiseClone();
    public DateOnly Date { get; set; }
    public string? Bed { get; set; }
    public string? Attempt { get; set; }
    public int? Latency { get; set; }
    public int? Awakenings { get; set; }
    public int? AwakeMinutes { get; set; }
    public string? FinalWake { get; set; }
    public string? OutOfBed { get; set; }
    public int? Sleepiness { get; set; }
    public int? NapMinutes { get; set; }
    public string? LastCaffeine { get; set; }
    public string Treatment { get; set; } = "미기록";
    public string Note { get; set; } = "";
    public DateTime Updated { get; set; }
    public double? EstimatedSleepMinutes
    {
        get
        {
            if (!TimeOnly.TryParseExact(Attempt, "HH:mm", out var start) ||
                !TimeOnly.TryParseExact(FinalWake, "HH:mm", out var end) ||
                Latency is null || AwakeMinutes is null) return null;
            double interval = (end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
            if (interval <= 0) interval += 1440;
            double total = interval - Latency.Value - AwakeMinutes.Value;
            return total >= 0 ? total : null;
        }
    }
    public string Display => $"{Date:MM/dd}  ·  {Attempt ?? "—"} → {FinalWake ?? "—"}  ·  깨어 있던 시간 {AwakeMinutes?.ToString() ?? "—"}분";
}

public sealed class WorkItem
{
    public string? SourcePlanKey { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Project { get; set; } = "Afterlife";
    public string Title { get; set; } = "";
    public string Problem { get; set; } = "";
    public string Hypothesis { get; set; } = "";
    public string DoneWhen { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Next { get; set; } = "";
    public string Stage { get; set; } = "제안";
    public bool Focus { get; set; }
    public bool Archived { get; set; }
    public DateTime Updated { get; set; }
    public List<string> Checklist { get; set; } = [];
    public string Display => $"{(Focus ? "● " : "")}{Title}\n{Project} · {Stage}";
    public static readonly string[] Stages = ["제안", "채택", "구현", "기술 검증", "사람 검증", "출시 준비", "공개", "결과 관찰"];
}

public sealed class NoticeLog
{
    public DateTime At { get; set; }
    public string Text { get; set; } = "";
    public string Display => $"{At:MM/dd HH:mm}  {Text}";
}

