namespace Facet.Core;

public sealed record UserReminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string Kind { get; set; } = "reminder";
    public bool Completed { get; set; }
    public bool Cancelled { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}

public sealed class ReminderCommand
{
    public int Version { get; set; } = 1;
    public string Action { get; set; } = "list";
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset? At { get; set; }
    public string Kind { get; set; } = "reminder";
    public Guid? Id { get; set; }
}

public sealed record ReminderReply(bool Ok, string Code, string Message, UserReminder? Item = null,
    IReadOnlyList<UserReminder>? Items = null, bool Changed = false);

public static class UserReminders
{
    public const int MaximumCount = 1000;

    public static void Validate(UserReminder item)
    {
        if (item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 160 || item.Key.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 120 || item.Title.Any(char.IsControl) ||
            item.Body == null || item.Body.Length > 1000 || item.At.Year is < 2000 or > 2100 || item.Kind is not ("schedule" or "reminder"))
            throw new ArgumentException("알림은 key(160자 이하), 제목(120자 이하), 2000~2100년 시각, schedule/reminder 종류가 필요합니다.");
    }

    public static void ValidateAll(List<UserReminder>? items)
    {
        if (items == null || items.Count > MaximumCount || items.Any(x => x == null) ||
            items.Select(x => x.Id).Distinct().Count() != items.Count || items.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new ArgumentException("알림 목록이 올바르지 않습니다.");
        foreach (var item in items) Validate(item);
    }

    public static ReminderReply Apply(AppData data, ReminderCommand command)
    {
        if (command.Version != 1) throw new ArgumentException("지원하지 않는 알림 프로토콜 버전입니다.");
        if (command.Action == null || command.Key == null || command.Title == null || command.Body == null || command.Kind == null)
            throw new ArgumentException("알림 명령의 문자열은 null일 수 없습니다.");
        if (command.Action == "list") return new(true, "ok", "등록된 알림", Items: data.Reminders.OrderBy(x => x.At).ToArray());
        if (command.Action == "add") {
            var item = new UserReminder { Key = command.Key.Trim(), Title = command.Title.Trim(), Body = command.Body.Trim(),
                At = command.At ?? throw new ArgumentException("시각이 필요합니다."), Kind = command.Kind };
            Validate(item);
            var existing = data.Reminders.SingleOrDefault(x => x.Key == item.Key);
            if (existing != null) {
                if (existing.Title != item.Title || existing.Body != item.Body || existing.At != item.At || existing.Kind != item.Kind)
                    return new(false, "key_conflict", "같은 key의 내용이 다릅니다. 기존 알림을 보존했습니다.");
                return new(true, "existing", "이미 등록된 알림입니다.", existing);
            }
            if (data.Reminders.Count >= MaximumCount) throw new ArgumentException("알림은 최대 1000개까지 보관합니다.");
            data.Reminders.Add(item); return new(true, "created", "알림을 추가했습니다.", item, Changed: true);
        }
        if (command.Action is not ("complete" or "cancel")) throw new ArgumentException("add/list/complete/cancel 명령만 지원합니다.");
        var target = data.Reminders.SingleOrDefault(x => x.Id == command.Id);
        if (target == null) return new(false, "not_found", "해당 알림이 없습니다.");
        if (command.Action == "complete") {
            if (target.Cancelled) return new(false, "cancelled", "취소한 알림은 완료할 수 없습니다.");
            bool changed = !target.Completed; target.Completed = true;
            return new(true, "completed", "완료했습니다.", target, Changed: changed);
        }
        bool cancelled = !target.Cancelled; target.Cancelled = true;
        return new(true, "cancelled", "취소했습니다.", target, Changed: cancelled);
    }

    public static UserReminder? Due(AppData data, DateTimeOffset now) => !data.Settings.Notifications ? null : data.Reminders
        .Where(x => !x.Completed && !x.Cancelled && x.DeliveredAt == null && x.At <= now).OrderBy(x => x.At).FirstOrDefault();

    public static bool MarkDelivered(AppData data, Guid id, DateTimeOffset now)
    {
        var item = data.Reminders.SingleOrDefault(x => x.Id == id);
        if (item == null || item.Completed || item.Cancelled || item.DeliveredAt != null || item.At > now) return false;
        item.DeliveredAt = now; return true;
    }
}

// A schedule as it stands today: real start and end, its title, and the first memo line.
public sealed record ScheduleSlot(string Title, string Memo, DateTime Start, DateTime End)
{
    public bool IsEnding(DateTime now) => End - now <= TimeSpan.FromMinutes(10);
}

public static class DaySchedules
{
    private static IEnumerable<string> Lines(string text) => text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);

    // A schedule without a name is titled by its first content line, so the memo is the line after it.
    public static (ScheduleSlot? Previous, ScheduleSlot? Current, ScheduleSlot? Next) Around(AppData data, DateTime now)
    {
        var date = DateOnly.FromDateTime(now); var midnight = date.ToDateTime(TimeOnly.MinValue);
        var slots = CalendarNotes.SchedulesForDate(data, date).Select(item => {
            var lines = Lines(item.Content).ToArray();
            bool named = item.Name.Trim().Length > 0;
            var start = midnight.AddMinutes(item.StartDayOffset * 1440L + item.StartMinute);
            return new ScheduleSlot(named ? item.Name.Trim() : lines.FirstOrDefault() ?? "", lines.ElementAtOrDefault(named ? 0 : 1) ?? "", start, start.AddMinutes(item.DurationMinutes));
        }).OrderBy(slot => slot.Start).ToArray();
        return (slots.LastOrDefault(slot => slot.End <= now), slots.LastOrDefault(slot => slot.Start <= now && now < slot.End), slots.FirstOrDefault(slot => slot.Start > now));
    }
}

public sealed record AgendaCard(string Id, string Title, string Body, string Label, DateTime? At);

public static class DashboardAgenda
{
    public static IReadOnlyList<AgendaCard> Today(AppData data, DateTimeOffset now)
    {
        var cards = data.Reminders.Where(x => !x.Completed && !x.Cancelled && x.At.ToOffset(now.Offset).Date == now.Date)
            .Select(x => new AgendaCard(x.Id.ToString(), x.Title, x.Body, x.Kind == "schedule" ? "일정" : "알림", x.At.ToOffset(now.Offset).DateTime)).ToList();
        var day = data.CalendarDays.FirstOrDefault(x => x.Date == DateOnly.FromDateTime(now.DateTime));
        if (day != null) cards.AddRange(day.Todos.Where(x => !x.Done).Select(x => new AgendaCard(x.Id.ToString(), x.Title, "", "할 일", null)));
        return cards.OrderBy(x => x.At ?? DateTime.MaxValue).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }
}
