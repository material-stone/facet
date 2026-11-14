using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Facet;

// Installs skins and plugins from a zip or the online list, and prepares a zip for submission (ADR 0088).
// The list loads once when this panel is first shown; choosing a row downloads and applies it at once.
internal sealed class PackagesPanel : StackPanel
{
    private readonly StackPanel list = new();
    private readonly TextBlock status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private OverlayTabs? owner;
    private Controller? app;
    private bool listRequested;
    private IReadOnlyList<RegistryEntry> entries = [];
    private string dataRoot = "";
    private Action changed = () => { };
    private bool busy;

    internal PackagesPanel()
    {
        status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var buttons = new WrapPanel();
        IsVisibleChanged += (_, _) => { if (IsVisible && owner != null && !listRequested) { listRequested = true; LoadList(); } };
        buttons.Children.Add(Command("목록 새로고침", LoadList));
        buttons.Children.Add(Command("zip 파일로 설치", InstallFile));
        buttons.Children.Add(Command("스킨 폴더 열기", () => OpenFolder(Path.Combine(dataRoot, "skins"))));
        buttons.Children.Add(Command("올리기", Submit));
        Children.Add(buttons);
        Children.Add(status);
        Children.Add(list);
    }

    internal void Connect(OverlayTabs tabs, Controller controller, string root, Action onChanged) { owner = tabs; app = controller; dataRoot = root; changed = onChanged; }

    private static Button Command(string text, Action run)
    {
        var button = new Button { Content = text, Margin = new(0, 0, 8, 8), Padding = new(10, 4, 10, 4) };
        button.Click += (_, _) => run();
        return button;
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
            entries = await PackageRegistry.FetchAsync(CancellationToken.None);
            Show(entries.Count == 0 ? "공개된 패키지가 없습니다." : "");
            Draw();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException) { Show("목록을 불러오지 못했습니다. 인터넷 연결을 확인해 주세요. 폴더와 zip 설치는 그대로 쓸 수 있습니다.", true); }
        finally { busy = false; }
    }

    private void Draw()
    {
        list.Children.Clear();
        foreach (var entry in entries.OrderBy(entry => entry.Kind).ThenBy(entry => entry.Name, StringComparer.CurrentCulture)) list.Children.Add(Row(entry));
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
        string kind = entry.Kind == PackageKind.Plugin ? "플러그인" : "스킨";
        text.Children.Add(new TextBlock { Text = $"{entry.Name} {entry.Version}", FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var detail = new TextBlock { Text = $"{kind} · {entry.Author}" + (entry.Description.Length > 0 ? " · " + entry.Description : "") + (entry.Kind == PackageKind.Plugin ? " · 적용하면 앱과 같은 권한으로 실행됩니다" : ""), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        text.Children.Add(detail);
        row.Children.Add(text);
        string? installed = InstalledVersion(entry);
        bool update = installed != null && PackageRegistry.IsNewer(entry.Version, installed);
        bool download = installed == null || update, active = !download && IsActive(entry);
        var install = new Button { Content = update ? "업데이트" : active ? "사용 중" : "적용", IsEnabled = !active, VerticalAlignment = VerticalAlignment.Center, Padding = new(10, 4, 10, 4) };
        install.Click += async (_, _) => {
            if (busy) return;
            busy = true;
            install.IsEnabled = false;
            Show(download ? $"{entry.Name} 내려받는 중…" : $"{entry.Name} 적용 중…");
            try {
                if (download) await PackageRegistry.InstallAsync(entry, dataRoot, CancellationToken.None);
                changed();
                bool applied = Apply(entry);
                Show(applied ? $"{entry.Name}을(를) 적용했습니다." : $"{entry.Name}을(를) 설치했지만 적용하지 못했습니다. {owner?.PluginState(entry.Id)}", !applied);
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
            ? $"{info.Name}을(를) 설치했습니다. 앱과 같은 권한으로 실행되므로 신뢰할 때만 위 목록에서 켜세요."
            : $"{info.Name}을(를) 설치했습니다. 스킨 목록에서 선택하세요. 사용 중인 스킨을 업데이트했다면 다시 선택하거나 앱을 다시 시작하면 적용됩니다.");
    }

    private void InstallFile()
    {
        var dialog = new OpenFileDialog { Filter = "패키지 (*.zip)|*.zip", Title = "설치할 패키지 선택" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { Finish(Packages.Install(dialog.FileName, dataRoot)); if (entries.Count > 0) Draw(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { Show(ex.Message, true); }
    }

    // Nothing is uploaded from here: the zip is prepared and the signed-in browser attaches it to a submission.
    private void Submit()
    {
        var dialog = new OpenFolderDialog { Title = "올릴 스킨이나 플러그인 폴더 선택" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try {
            string zip = Packages.Pack(dialog.FolderName, Path.Combine(dataRoot, "outgoing-packages"));
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zip}\"") { UseShellExecute = false })?.Dispose();
            Process.Start(new ProcessStartInfo(PackageRegistry.SubmitUrl) { UseShellExecute = true })?.Dispose();
            Show($"{Path.GetFileName(zip)}을(를) 만들었습니다. 열린 GitHub 제출 양식에 이 파일을 끌어다 놓으세요. 스킨은 자동 검사 후, 플러그인은 관리자 승인 후 공개됩니다.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Show(ex.Message, true); }
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
    }
}
