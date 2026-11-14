// Retired features: DTOs only, retained to round-trip existing v1/v2 stores.
namespace Facet.Core;

public sealed class ExecutionPlan
{
    public DateOnly Start { get; set; }
    public string PrimaryGame { get; set; } = "미정";
    public List<PlanEntry> Entries { get; set; } = [];
}
public sealed class PlanEntry
{
    public DateOnly Date { get; set; }
    public string ItemId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Status { get; set; } = "미기록";
    public string Note { get; set; } = "";
    public DateTime Updated { get; set; }
    public string Display => $"{Date:MM/dd} · {Category} · {Status}\n{Title}{(Note.Length == 0 ? "" : "\n" + Note)}";
    public static readonly string[] Statuses = ["미기록", "완료", "일부 실천", "미실천", "해당 없음"];
}


public sealed class ConnectedProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Root { get; set; } = "";
    public bool Connected { get; set; } = true;
    public string Display => Name + (Connected ? "" : " · 연결 해제됨");
}

public sealed class ProjectJournalEntry
{
    public Guid ProjectId { get; set; }
    public DateOnly Date { get; set; }
    public DateTime Updated { get; set; }
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Work { get; set; } = [];
    public string Uncertainty { get; set; } = "";
    public string Comment { get; set; } = "";
    public string SourceSummary { get; set; } = "";
    public string Fingerprint { get; set; } = "";
}

