using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Facet;

// Installs one kind of package (skins or plugins) from a zip or the online list, and prepares a zip for submission (ADR 0088, 0100).
// The list loads once when this panel is first shown; choosing a row downloads and applies it at once.
internal sealed class PackagesPanel : StackPanel
{
    private readonly PackageKind kind;
    private string Noun => kind == PackageKind.Plugin ? "플러그인" : "스킨";
    private readonly StackPanel list = new();
    private readonly TextBlock status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private OverlayTabs? owner;
    private Controller? app;
    private bool listRequested;
    private IReadOnlyList<RegistryEntry> entries = [];
    private string dataRoot = "";
    private Action changed = () => { };
    private bool busy;

    internal PackagesPanel(PackageKind kind)
    {
        this.kind = kind;
        status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        // Right-aligned so the commands sit level with the row's label; plugins open their folder from the installed list.
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        IsVisibleChanged += (_, _) => { if (IsVisible && owner != null && !listRequested) { listRequested = true; LoadList(); } };
        buttons.Children.Add(Command(Design.IconKind.Refresh, "목록 새로고침", LoadList, showText: false));
        buttons.Children.Add(Command(Design.IconKind.Archive, "파일로 설치", InstallFile));
        if (kind == PackageKind.Skin) buttons.Children.Add(Command(Design.IconKind.Folder, Noun + " 폴더 열기", () => OpenFolder(Path.Combine(dataRoot, "skins"))));
        buttons.Children.Add(Command(Design.IconKind.Upload, "업로드", Submit, "직접 만든 " + Noun + " 올리기"));
        Children.Add(buttons);
        Children.Add(status);
        Children.Add(list);
    }

    internal void Connect(OverlayTabs tabs, Controller controller, string root, Action onChanged) { owner = tabs; app = controller; dataRoot = root; changed = onChanged; }

    private static Button Command(Design.IconKind icon, string text, Action run, string? tip = null, bool showText = true)
    {
        var button = PluginSettingsPanel.IconButton(icon, text, showText);
        button.Margin = new(8, 0, 0, 8);
        if (tip != null) { button.ToolTip = tip; System.Windows.Automation.AutomationProperties.SetHelpText(button, tip); }
        button.Click += (_, _) => run();
        return button;
    }

    // Install buttons and the installed mark share one width and end at the row's right edge.
    private const double StateWidth = 108;

    private static FrameworkElement StateContent(Design.IconKind icon, string text, string brush)
    {
        var mark = new Design.Icon { Kind = icon, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        mark.SetResourceReference(Design.Icon.ForegroundProperty, brush);
        var caption = new TextBlock { Text = text, Margin = new(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        caption.SetResourceReference(TextBlock.ForegroundProperty, brush);
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        content.Children.Add(mark);
        content.Children.Add(caption);
        // A fixed inner width with right-aligned content: the button template centres its content otherwise.
        return new Grid { Width = StateWidth - 26, HorizontalAlignment = HorizontalAlignment.Right, Children = { content } };
    }

    private void Show(string text, bool error = false)
    {
        status.Text = text;
        status.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "Muted");
    }

    private async void LoadList()
    {
        if (busy) return;
        busy = true;
        Show("목록을 불러오는 중…");
        try {
            entries = (await PackageRegistry.FetchAsync(CancellationToken.None)).Where(entry => entry.Kind == kind).ToList();
            Show(entries.Count == 0 ? $"아직 공개된 {Noun}이(가) 없습니다." : "");
            Draw();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException) { Show("목록을 불러오지 못했습니다. 인터넷 연결을 확인해 주세요. 폴더와 zip 설치는 그대로 쓸 수 있습니다.", true); }
        finally { busy = false; }
    }

    private void Draw()
    {
        list.Children.Clear();
        foreach (var entry in entries.OrderBy(entry => entry.Name, StringComparer.CurrentCulture)) list.Children.Add(Row(entry));
    }

    private string? InstalledVersion(RegistryEntry entry)
    {
        if (entry.Kind == PackageKind.Plugin) return owner?.DiscoverPlugins().FirstOrDefault(plugin => plugin.Id == entry.Id)?.Version;
        return Design.Skins.Rescan().FirstOrDefault(skin => skin.Id == entry.Id)?.External?.Version;
    }

    private Grid Row(RegistryEntry entry)
    {
        var row = new Grid { Margin = new(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new(0, 0, 8, 0) };
        text.Children.Add(new TextBlock { Text = $"{entry.Name} {entry.Version}", FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var detail = new TextBlock { Text = (entry.Description.Length > 0 ? entry.Description + "\n" : "") + "만든 사람: " + entry.Author, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        text.Children.Add(detail);
        row.Children.Add(text);
        string? installed = InstalledVersion(entry);
        bool update = installed != null && PackageRegistry.IsNewer(entry.Version, installed);
        bool download = installed == null || update, active = !download && IsActive(entry);
        if (active) {
            // Not a button: the caption ends at the row's right edge, where the install buttons end.
            var done = new Border { Width = StateWidth, Height = 34, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Child = StateContent(Design.IconKind.Check, kind == PackageKind.Plugin ? "설치됨" : "사용 중", "Mint") };
            ((FrameworkElement)done.Child).VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(done, entry.Name + (kind == PackageKind.Plugin ? " 설치됨" : " 사용 중"));
            Grid.SetColumn(done, 1);
            row.Children.Add(done);
            return row;
        }
        string caption = update ? "업데이트" : installed == null ? "설치" : (kind == PackageKind.Plugin ? "켜기" : "적용");
        var install = new Button { Content = StateContent(download ? Design.IconKind.Download : Design.IconKind.Check, caption, "Ink"), ContentTemplate = null, HorizontalContentAlignment = HorizontalAlignment.Left, Width = StateWidth, Height = 34, Margin = new(0), VerticalAlignment = VerticalAlignment.Center, Padding = new(12, 0, 12, 0) };
        System.Windows.Automation.AutomationProperties.SetName(install, entry.Name + " " + caption);
        install.Click += async (_, _) => {
            if (busy) return;
            busy = true;
            install.IsEnabled = false;
            Show(download ? $"{entry.Name} 내려받는 중…" : $"{entry.Name} 켜는 중…");
            try {
                if (download) await PackageRegistry.InstallAsync(entry, dataRoot, CancellationToken.None);
                changed();
                bool applied = Apply(entry);
                Show(applied ? (kind == PackageKind.Plugin ? $"{entry.Name}을(를) 켰습니다. 위젯 편집에서 배치하세요." : $"{entry.Name} 스킨을 적용했습니다.") : $"{entry.Name}을(를) 설치했지만 적용하지 못했습니다. {owner?.PluginState(entry.Id)}", !applied);
                Draw();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or IOException or UnauthorizedAccessException) { Show(ex is InvalidDataException ? ex.Message : "내려받지 못했습니다. 인터넷 연결을 확인해 주세요.", true); install.IsEnabled = true; }
            finally { busy = false; }
        };
        Grid.SetColumn(install, 1);
        row.Children.Add(install);
        return row;
    }

    private bool IsActive(RegistryEntry entry) => entry.Kind == PackageKind.Plugin
        ? owner?.IsPluginEnabled(entry.Id) == true
        : app != null && Design.Skins.Resolve(app.Data.Settings.Skin) == entry.Id;

    private bool Apply(RegistryEntry entry) => entry.Kind == PackageKind.Plugin
        ? owner?.StartPackagePlugin(entry.Id) == true
        : app?.ApplySkin(entry.Id) == true;

    private void Finish(PackageInfo info)
    {
        changed();
        Show(info.Kind == PackageKind.Plugin
            ? $"{info.Name} 플러그인을 설치했습니다. 앱과 같은 권한으로 실행되므로 믿을 수 있을 때만 \"설치된 플러그인\"에서 켜세요."
            : $"{info.Name} 스킨을 설치했습니다. 위의 스킨 목록에서 고르세요.");
    }

    private void InstallFile()
    {
        var dialog = new OpenFileDialog { Filter = Noun + " 패키지 (*.zip)|*.zip", Title = "설치할 " + Noun + " zip 선택" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { Finish(Packages.Install(dialog.FileName, dataRoot)); if (entries.Count > 0) Draw(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { Show(ex.Message, true); }
    }

    // Nothing is uploaded from here: the zip is prepared and the signed-in browser attaches it to a submission.
    private void Submit()
    {
        var dialog = new OpenFolderDialog { Title = "올릴 " + Noun + " 폴더 선택" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { Show(Submit(dialog.FolderName, dataRoot)); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Show(ex.Message, true); }
    }

    // Shared with the skin tab; returns the message to show.
    internal static string Submit(string folder, string dataRoot)
    {
        string zip = Packages.Pack(folder, Path.Combine(dataRoot, "outgoing-packages"));
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = false })?.Dispose();
        Process.Start(new ProcessStartInfo(PackageRegistry.SubmitUrl) { UseShellExecute = true })?.Dispose();
        return $"{Path.GetFileName(zip)}을(를) 만들었습니다. 열린 GitHub 제출 양식에 이 파일을 끌어다 놓으세요. 스킨은 자동 검사 후, 플러그인은 관리자 승인 후 공개됩니다.";
    }

    internal static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
    }
}
