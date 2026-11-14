using Facet.Design;

namespace Facet;

// The icons a tab can use. A saved id that is missing or no longer listed falls back to the tab's default.
internal static class TabIcons
{
    internal static readonly (IconKind Kind, string Label)[] Presets = [
        (IconKind.Dashboard, "대시보드"), (IconKind.Home, "집"), (IconKind.Star, "별"), (IconKind.Bookmark, "북마크"),
        (IconKind.Calendar, "달력"), (IconKind.Clock, "시계"), (IconKind.Check, "체크"), (IconKind.Work, "서류 가방"), (IconKind.Target, "목표"),
        (IconKind.Note, "노트"), (IconKind.Book, "책"), (IconKind.Folder, "폴더"), (IconKind.Chart, "차트"),
        (IconKind.Chat, "말풍선"), (IconKind.Sparkles, "반짝임"),
        (IconKind.Moon, "달"), (IconKind.Heart, "하트"), (IconKind.Dumbbell, "덤벨"), (IconKind.Coffee, "커피"),
        (IconKind.Music, "음악"), (IconKind.Gamepad, "게임패드"), (IconKind.Code, "코드"), (IconKind.Palette, "팔레트"), (IconKind.Globe, "지구본")];

    internal static string IdOf(IconKind kind) => kind.ToString();

    internal static IconKind For(WorkspaceTab tab)
    {
        if (tab.Id == WorkspaceLayouts.SettingsTab) return IconKind.Settings;
        foreach (var preset in Presets) if (IdOf(preset.Kind) == tab.Icon) return preset.Kind;
        return tab.Id switch {
            WorkspaceLayouts.ScheduleTab => IconKind.Calendar,
            WorkspaceLayouts.SleepTab => IconKind.Moon,
            WorkspaceLayouts.ChatTab => IconKind.Sparkles,
            _ => IconKind.Dashboard
        };
    }
}
