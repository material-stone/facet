using System.Text.Json.Serialization;

namespace Facet.Core;

public sealed class CalendarTodo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

public sealed class CalendarDayEntry
{
    public DateOnly Date { get; set; }
    public List<CalendarTodo> Todos { get; set; } = [];
    public string Note { get; set; } = "";
    public List<DaySchedule> Schedules { get; set; } = [];
    public DateTime Updated { get; set; }
}

public sealed class DaySchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int StartMinute { get; set; }
    public long DurationMinutes { get; set; } = 30;
    public string Color { get; set; } = "#82CFAD";
    public string Label { get; set; } = "개인";
    public string Content { get; set; } = "";
    // Transient projection relative to the date being viewed; canonical storage has neither.
    [JsonIgnore] public int StartDayOffset { get; set; }
    [JsonIgnore] public DateOnly? OwnerDate { get; set; }
    [JsonIgnore] public int VisibleStartMinute => (int)Math.Clamp((long)StartDayOffset*1440+StartMinute,0,1440);
    [JsonIgnore] public int VisibleDurationMinutes => (int)Math.Max(0,Math.Min(1440,(long)StartDayOffset*1440+StartMinute+DurationMinutes)-VisibleStartMinute);
    public static IEnumerable<(int DayOffset,int StartMinute,int DurationMinutes)> SplitRange(int start,int duration)
    {
        int end=checked(start+duration);
        for(int cursor=start;cursor<end;) {
            int day=(int)Math.Floor(cursor/1440d),local=cursor-day*1440;
            int count=Math.Min(end-cursor,1440-local);yield return(day,local,count);cursor+=count;
        }
    }
    public DaySchedule Copy() => (DaySchedule)MemberwiseClone();
    public bool ContainsSlot(int slot) => slot>=0 && slot<48 && slot*30>=VisibleStartMinute && slot*30<VisibleStartMinute+VisibleDurationMinutes;

    public static void Validate(DaySchedule item)
    {
        if (item.Id == Guid.Empty || item.Name == null || item.Name.Length > 200 ||
            (string.IsNullOrWhiteSpace(item.Name) && string.IsNullOrWhiteSpace(item.Content)) ||
            item.StartMinute < 0 || item.StartMinute >= 1440 || item.StartMinute % 30 != 0 ||
            item.DurationMinutes < 30 || item.DurationMinutes > ((long)DateOnly.MaxValue.DayNumber+1)*1440 || item.DurationMinutes % 30 != 0 ||
            string.IsNullOrWhiteSpace(item.Label) || item.Label.Length > 40 || item.Content == null || item.Content.Length > 20000 ||
            item.Color == null || item.Color.Length != 7 || item.Color[0] != '#' || !item.Color.Skip(1).All(Uri.IsHexDigit))
            throw new ArgumentException("일정 이름(최대 200자)이나 내용을 입력하고 레이블(1~40자), #RRGGBB 색상과 30분 단위 시간을 확인해 주세요.");
    }
}

// The calendar widget's own store file: schedules, todos and notes by date. ADR 0076.
public sealed class CalendarStoreFile
{
    public const int CurrentVersion = 1;
    public int SchemaVersion { get; set; } = CurrentVersion;
    public List<CalendarDayEntry> Days { get; set; } = [];
    // Missing until the user first edits presets, as in AppData.SchedulePresets.
    public List<SchedulePreset>? SchedulePresets { get; set; }
}

public static class CalendarNotes
{
    // What the store already holds wins; older records only fill in dates it does not have.
    public static List<CalendarDayEntry> Merge(IEnumerable<CalendarDayEntry> stored,IEnumerable<CalendarDayEntry> older)
    {
        var kept=stored.ToList();var dates=kept.Select(day=>day.Date).ToHashSet();
        return kept.Concat(older.Where(day=>!dates.Contains(day.Date))).OrderBy(day=>day.Date).Select(Copy).ToList();
    }

    public static void ValidateStore(CalendarStoreFile? file)
    {
        if(file==null || file.SchemaVersion!=CalendarStoreFile.CurrentVersion || file.Days==null || file.Days.Any(day=>day==null) || file.Days.Select(day=>day.Date).Distinct().Count()!=file.Days.Count)
            throw new InvalidDataException("달력 저장소 형식이 올바르지 않습니다.");
        foreach(var day in file.Days)Validate(day);
    }

    public static CalendarDayEntry Copy(CalendarDayEntry entry) => new() {
        Date = entry.Date, Note = entry.Note, Updated = entry.Updated, Schedules = entry.Schedules.Select(s => s.Copy()).ToList(),
        Todos = entry.Todos.Select(t => new CalendarTodo { Id = t.Id, Title = t.Title, Done = t.Done }).ToList()
    };

    public static CalendarDayEntry Get(AppData data, DateOnly date) =>
        data.CalendarDays.FirstOrDefault(d => d.Date == date) is { } entry ? Copy(entry) : new() { Date = date };

    public static IReadOnlyList<DaySchedule> SchedulesForDate(AppData data,DateOnly date)
    {
        var result=new List<DaySchedule>();
        foreach(var day in data.CalendarDays.Where(day=>day.Date<=date))
            foreach(var stored in day.Schedules) {
                var item=stored.Copy();item.StartDayOffset=day.Date.DayNumber-date.DayNumber;item.OwnerDate=day.Date;
                if(item.VisibleDurationMinutes>0)result.Add(item);
            }
        return result;
    }

    public static bool HasContentOnDate(AppData data,DateOnly date) => data.CalendarDays.Any(day=>day.Date==date ||
        day.Date<date && day.Schedules.Any(item=>(long)day.Date.DayNumber*1440+item.StartMinute+item.DurationMinutes>(long)date.DayNumber*1440));

    public static IEnumerable<DateOnly> RecordedDates(AppData data) => data.CalendarDays.SelectMany(day=>
        Enumerable.Range(day.Date.DayNumber,(int)Math.Min(DateOnly.MaxValue.DayNumber-day.Date.DayNumber+1,
            day.Schedules.Select(item=>(item.StartMinute+item.DurationMinutes-1)/1440+1).DefaultIfEmpty(1).Max()))
            .Select(DateOnly.FromDayNumber)).Distinct();

    public static void SaveSchedule(AppData data,CalendarDayEntry current,DaySchedule item,bool delete,DateTime now)
    {
        if(!delete)DaySchedule.Validate(item);
        var startDate=current.Date.AddDays(item.StartDayOffset);
        var candidate=new AppData {CalendarDays=data.CalendarDays.Select(Copy).ToList()};
        Save(candidate,current,now);
        var oldDate=item.OwnerDate??startDate;
        var old=Get(candidate,oldDate);old.Schedules.RemoveAll(existing=>existing.Id==item.Id);Save(candidate,old,now);
        if(!delete) {
            var target=Get(candidate,startDate);var canonical=item.Copy();canonical.StartDayOffset=0;canonical.OwnerDate=null;
            target.Schedules.RemoveAll(existing=>existing.Id==item.Id);target.Schedules.Add(canonical);Save(candidate,target,now);
        }
        data.CalendarDays=candidate.CalendarDays;
    }

    public static void Validate(CalendarDayEntry entry)
    {
        if (entry.Schedules == null || entry.Schedules.Count > 200 || entry.Schedules.Any(s => s == null) ||
            entry.Schedules.Select(s => s.Id).Distinct().Count() != entry.Schedules.Count)
            throw new ArgumentException("상세 일정은 중복 없는 항목으로 하루 200개까지 작성할 수 있습니다.");
        foreach (var schedule in entry.Schedules) {
            DaySchedule.Validate(schedule);
            if((long)entry.Date.DayNumber*1440+schedule.StartMinute+schedule.DurationMinutes>((long)DateOnly.MaxValue.DayNumber+1)*1440)
                throw new ArgumentException("일정 종료 날짜가 지원하는 날짜 범위를 벗어납니다.");
            if(schedule.StartDayOffset!=0 || schedule.OwnerDate!=null)throw new ArgumentException("화면용 구간 대신 실제 시작 날짜의 일정을 저장해 주세요.");
        }
        if (entry.Note == null || entry.Note.Length > 20000 || entry.Todos == null || entry.Todos.Count > 200)
            throw new ArgumentException("노트는 20,000자, 할 일은 하루 200개까지 작성할 수 있습니다.");
        if (entry.Todos.Any(t => t == null || t.Id == Guid.Empty || string.IsNullOrWhiteSpace(t.Title) || t.Title.Length > 200) ||
            entry.Todos.Select(t => t.Id).Distinct().Count() != entry.Todos.Count)
            throw new ArgumentException("할 일은 중복 없는 항목으로 1~200자까지 작성해 주세요.");
    }

    public static void Save(AppData data, CalendarDayEntry entry, DateTime now)
    {
        Validate(entry);
        var copy = Copy(entry); copy.Updated = now;
        // Replace only after validation; callers can keep the old list for rollback.
        var days = data.CalendarDays.Where(d => d.Date != entry.Date).ToList();
        if (copy.Todos.Count > 0 || copy.Schedules.Count > 0 || !string.IsNullOrWhiteSpace(copy.Note)) days.Add(copy);
        data.CalendarDays = days;
    }

    public static IReadOnlyList<SchedulePreset> SchedulePresets(AppData data) => (data.SchedulePresets ??
        data.CalendarDays.OrderByDescending(day=>day.Date).SelectMany(day=>day.Schedules).Select(s=>new SchedulePreset{Color=s.Color.ToUpperInvariant(),Label=s.Label}).ToList())
        .Where(p=>SchedulePreset.IsValid(p)).DistinctBy(p=>p.Key).Take(SchedulePreset.MaxCount).Select(p=>p.Copy()).ToArray();

    // Adds (to the front) or removes one preset; materializes derived presets on the first edit.
    public static void SaveSchedulePreset(AppData data,SchedulePreset preset,bool delete)
    {
        var item=new SchedulePreset{Color=(preset.Color??"").Trim().ToUpperInvariant(),Label=(preset.Label??"").Trim()};
        if(!delete && !SchedulePreset.IsValid(item))throw new ArgumentException("레이블(1~40자)과 #RRGGBB 색상을 확인해 주세요.");
        var next=SchedulePresets(data).Where(p=>p.Key!=item.Key).ToList();
        if(!delete) {
            if(next.Count>=SchedulePreset.MaxCount)throw new ArgumentException($"프리셋은 {SchedulePreset.MaxCount}개까지 저장할 수 있습니다.");
            next.Insert(0,item);
        }
        data.SchedulePresets=next;
    }
}

public sealed class SchedulePreset
{
    public const int MaxCount=50;
    public string Color { get; set; } = "#82CFAD";
    public string Label { get; set; } = "개인";
    [JsonIgnore] public string Key => Color.ToUpperInvariant()+"\n"+Label;
    public SchedulePreset Copy() => new() {Color=Color,Label=Label};
    public static bool IsValid(SchedulePreset p) => p.Color is {Length:7} c && c[0]=='#' && c.Skip(1).All(Uri.IsHexDigit) &&
        !string.IsNullOrWhiteSpace(p.Label) && p.Label.Length<=40 && p.Label==p.Label.Trim();
}
