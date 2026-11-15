using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace Facet;

// The skin tab's one list (ADR 0103): installed skins to apply, then registry skins to install.
// Installed rows carry a chip when the registry has a newer version or no longer lists them; an unlisted skin can still be applied.
internal sealed class SkinsPanel : StackPanel
{
    internal enum SkinState { Current, Update, Unsupported }

    private const string IconFont = "Segoe MDL2 Assets";
    private const double ControlHeight = 32;
    // The shared TextBox style adds an outer margin; zero keeps the box level with the toolbar buttons.
    private readonly TextBox search = new() { Height = ControlHeight, MinHeight = 0, Margin = new(0), VerticalContentAlignment = VerticalAlignment.Center, Padding = new(8, 0, 8, 0) };
    private readonly StackPanel installedList = new(), availableList = new();
    private readonly TextBlock installedHeading = Heading(), availableHeading = Heading();
    private readonly TextBlock status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock folderText = Caption(), refreshedText = Caption();
    private readonly string group = "Skins" + Guid.NewGuid().ToString("N");
    private Controller? app;
    private string dataRoot = "";
    private Action changed = () => { };
    private IReadOnlyList<RegistryEntry> entries = [];
    private bool registryLoaded, listRequested, busy, drawing;
    private string selected = Design.Skins.DefaultId;
    private string? installing;
    private static ResourceDictionary? defaults;

    internal event Action? SelectionChanged;

    internal SkinsPanel()
    {
        Children.Add(Toolbar());
        Children.Add(status);
        Children.Add(installedHeading);
        Children.Add(installedList);
        Children.Add(availableHeading);
        Children.Add(availableList);
        var footer = new DockPanel { Margin = new(0, 12, 0, 0), LastChildFill = true };
        var submit = new Button { Content = "직접 만든 스킨 올리기", Padding = new(8, 2, 8, 2), Margin = new(8, 0, 0, 0), FontSize = 12 };
        submit.Click += (_, _) => Submit();
        DockPanel.SetDock(submit, Dock.Right); DockPanel.SetDock(refreshedText, Dock.Right);
        refreshedText.VerticalAlignment = VerticalAlignment.Center; folderText.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(submit); footer.Children.Add(refreshedText); footer.Children.Add(folderText);
        Children.Add(footer);
        IsVisibleChanged += (_, _) => { if (IsVisible && app != null && !listRequested) { listRequested = true; Refresh(); } };
        Draw();
    }

    internal void Connect(Controller controller, string root, Action onChanged)
    {
        app = controller; dataRoot = root; changed = onChanged;
        folderText.Text = "스킨 폴더: " + SkinFolder;
        folderText.ToolTip = SkinFolder;
    }

    private string SkinFolder => Path.Combine(dataRoot, "skins");

    internal string SelectedSkin {
        get => selected;
        set { selected = Design.Skins.Resolve(value); Draw(); }
    }

    // Rereads the skin folder; the settings row calls this on load.
    internal void Reload(string? skin) { Design.Skins.Rescan(); SelectedSkin = skin ?? Design.Skins.DefaultId; }

    // Unlisted only once the registry was read, so an offline list never marks every skin unsupported.
    internal static SkinState StateOf(Design.SkinOption skin, IReadOnlyList<RegistryEntry> registry, bool registryLoaded)
    {
        if (!registryLoaded || skin.External == null) return SkinState.Current;
        var entry = registry.FirstOrDefault(item => item.Kind == PackageKind.Skin && item.Id == skin.Id);
        if (entry == null) return SkinState.Unsupported;
        return PackageRegistry.IsNewer(entry.Version, skin.External.Version) ? SkinState.Update : SkinState.Current;
    }

    // Registry skins with no installed folder of the same id.
    internal static IEnumerable<RegistryEntry> Installable(IEnumerable<Design.SkinOption> installed, IReadOnlyList<RegistryEntry> registry) =>
        registry.Where(entry => entry.Kind == PackageKind.Skin && installed.All(skin => skin.Id != entry.Id)).OrderBy(entry => entry.Name, StringComparer.CurrentCulture);

    private FrameworkElement Toolbar()
    {
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new());
        for (int i = 0; i < 3; i++) bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var box = new Grid();
        var hint = new TextBlock { Text = "스킨 검색", Margin = new(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetName(search, "스킨 검색");
        search.TextChanged += (_, e) => { e.Handled = true; hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Draw(); };
        box.Children.Add(search); box.Children.Add(hint);
        bar.Children.Add(box);
        void Place(Button button, int column) { Grid.SetColumn(button, column); button.Margin = new(6, 0, 0, 0); bar.Children.Add(button); }
        Place(IconButton("", "목록 새로고침", Refresh), 1);
        Place(IconButton("", "스킨 폴더 열기", () => PackagesPanel.OpenFolder(SkinFolder)), 2);
        var file = ToolbarButton("파일로 설치", "zip 파일로 스킨 설치", InstallFile);
        Place(file, 3);
        return bar;
    }

    private static Button ToolbarButton(object content, string name, Action run)
    {
        var button = new Button { Content = content, Height = ControlHeight, MinHeight = 0, Padding = new(10, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = name };
        // The shared button template formats string content, so element content needs the plain presenter.
        if (content is not string) button.ContentTemplate = null;
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => run();
        return button;
    }

    private static Button IconButton(string glyph, string name, Action run)
    {
        var button = ToolbarButton(new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 14, VerticalAlignment = VerticalAlignment.Center }, name, run);
        button.Width = ControlHeight; button.Padding = new(0);
        return button;
    }

    private static TextBlock Heading()
    {
        var text = new TextBlock { FontSize = 12, Margin = new(0, 14, 0, 4) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return text;
    }

    private static TextBlock Caption()
    {
        var text = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return text;
    }

    private void Show(string text, bool error = false)
    {
        status.Text = text;
        status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        status.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "Muted");
    }

    private bool Matches(params string[] fields)
    {
        string query = search.Text.Trim();
        return query.Length == 0 || fields.Any(field => field.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void Draw()
    {
        drawing = true;
        try {
            var installed = Design.Skins.Options;
            var shownInstalled = installed.Where(skin => Matches(skin.Name, skin.Id)).ToList();
            installedList.Children.Clear();
            foreach (var skin in shownInstalled) installedList.Children.Add(InstalledRow(skin));
            installedHeading.Text = $"설치된 스킨 · {installed.Count}";
            var available = Installable(installed, entries).ToList();
            availableList.Children.Clear();
            foreach (var entry in available.Where(entry => Matches(entry.Name, entry.Id, entry.Description))) availableList.Children.Add(AvailableRow(entry));
            availableHeading.Text = $"설치할 수 있는 스킨 · {available.Count}";
            bool showAvailable = available.Count > 0;
            availableHeading.Visibility = availableList.Visibility = showAvailable ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { drawing = false; }
    }

    private Border RowFrame(UIElement content, bool active, bool first)
    {
        var frame = new Border { Child = content, Padding = new(8, 6, 8, 6), CornerRadius = new(active ? 6 : 0), BorderThickness = new(0, first || active ? 0 : 1, 0, 0) };
        frame.SetResourceReference(Border.BorderBrushProperty, "Edge");
        if (active) frame.SetResourceReference(Border.BackgroundProperty, "Selected");
        return frame;
    }

    private Border InstalledRow(Design.SkinOption skin)
    {
        bool active = skin.Id == selected;
        var grid = new Grid();
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) grid.ColumnDefinitions.Add(new() { Width = width });
        var radio = new RadioButton { GroupName = group, IsChecked = active, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0) };
        AutomationProperties.SetName(radio, skin.Name + " 적용");
        radio.Checked += (_, _) => { if (drawing) return; selected = skin.Id; SelectionChanged?.Invoke(); Draw(); };
        grid.Children.Add(radio);
        var swatch = Swatch(skin); Grid.SetColumn(swatch, 1); grid.Children.Add(swatch);
        var name = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = skin.Name, FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        if (active) name.Children.Add(Muted(" 사용 중", 11));
        var state = StateOf(skin, entries, registryLoaded);
        if (state == SkinState.Update) name.Children.Add(Chip("업데이트", "Mint"));
        if (state == SkinState.Unsupported) name.Children.Add(Chip("지원 안 함", "Danger", "공개 목록에서 빠진 스킨입니다. 계속 쓸 수 있지만 업데이트는 받을 수 없습니다."));
        Grid.SetColumn(name, 2); grid.Children.Add(name);
        if (skin.External?.Version is { Length: > 0 } version) { var text = Muted("v" + version, 11); text.Margin = new(8, 0, 4, 0); Grid.SetColumn(text, 3); grid.Children.Add(text); }
        if (skin.External != null) { var menu = MenuButton(skin, state); Grid.SetColumn(menu, 4); grid.Children.Add(menu); }
        // A click anywhere on the row applies it, like the radio.
        // No divider right under the highlighted row: it would sit against its rounded fill.
        bool afterActive = installedList.Children.Count > 0 && installedList.Children[^1] is Border { Tag: true };
        var frame = RowFrame(grid, active, installedList.Children.Count == 0 || afterActive);
        frame.Tag = active;
        frame.Background ??= Brushes.Transparent;
        frame.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject source && FindButton(source) == null) radio.IsChecked = true; };
        return frame;
    }

    private static ButtonBase? FindButton(DependencyObject source)
    {
        for (var node = source; node != null; node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node))
            if (node is ButtonBase button) return button;
        return null;
    }

    private Button MenuButton(Design.SkinOption skin, SkinState state)
    {
        // Outlined like the toolbar buttons so the menu reads as a control on every row, not only on hover.
        var glyph = new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var button = new Button { Content = glyph, Width = 30, Height = 28, MinHeight = 0, Padding = new(0), Margin = new(4, 0, 0, 0), ToolTip = "더 보기", ContentTemplate = null };
        AutomationProperties.SetName(button, skin.Name + " 메뉴");
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        if (state == SkinState.Update && entries.FirstOrDefault(entry => entry.Kind == PackageKind.Skin && entry.Id == skin.Id) is { } entry) {
            var update = new MenuItem { Header = "업데이트" };
            update.Click += (_, _) => Install(entry);
            menu.Items.Add(update);
        }
        var remove = new MenuItem { Header = "제거" };
        remove.SetResourceReference(Control.ForegroundProperty, "Danger");
        remove.Click += (_, _) => Remove(skin);
        menu.Items.Add(remove);
        button.ContextMenu = menu;
        button.Click += (_, _) => menu.IsOpen = true;
        return button;
    }

    private Border AvailableRow(RegistryEntry entry)
    {
        var grid = new Grid();
        foreach (var width in new[] { new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) grid.ColumnDefinitions.Add(new() { Width = width });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = entry.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        string detail = string.Join(" · ", new[] { entry.Description, entry.Author }.Where(part => part.Length > 0));
        if (detail.Length > 0) { var line = Muted(detail, 11); line.TextWrapping = TextWrapping.NoWrap; line.TextTrimming = TextTrimming.CharacterEllipsis; line.ToolTip = detail; text.Children.Add(line); }
        grid.Children.Add(text);
        var version = Muted("v" + entry.Version, 11); version.Margin = new(8, 0, 8, 0); Grid.SetColumn(version, 1); grid.Children.Add(version);
        bool running = installing == entry.Id;
        var install = new Button { Content = running ? "설치 중" : "설치", IsEnabled = !running, Height = 28, MinHeight = 0, MinWidth = 64, Padding = new(10, 0, 10, 0), Margin = new(0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(install, entry.Name + " 설치");
        install.Click += (_, _) => Install(entry);
        Grid.SetColumn(install, 2); grid.Children.Add(install);
        return RowFrame(grid, false, availableList.Children.Count == 0);
    }

    private static TextBlock Muted(string text, double size)
    {
        var block = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return block;
    }

    // Outlined in the tone color: soft tone fills are too light under tone text in some skins.
    private static Border Chip(string text, string tone, string? tip = null)
    {
        var label = new TextBlock { Text = text, FontSize = 11 };
        label.SetResourceReference(TextBlock.ForegroundProperty, tone);
        var chip = new Border { Child = label, CornerRadius = new(4), BorderThickness = new(1), Padding = new(6, 0, 6, 1), Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
        chip.SetResourceReference(Border.BorderBrushProperty, tone);
        return chip;
    }

    // Three of the skin's own colors; tokens it leaves out fall back to the default skin, as they do when applied.
    private static StackPanel Swatch(Design.SkinOption skin)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0) };
        foreach (string token in new[] { "Bg", "Panel", "Mint" }) {
            var square = new Border { Width = 12, Height = 12, CornerRadius = new(3), Margin = new(0, 0, 3, 0), Background = new SolidColorBrush(ColorOf(skin, token)) };
            square.SetResourceReference(Border.BorderBrushProperty, "Edge"); square.BorderThickness = new(0.5);
            panel.Children.Add(square);
        }
        return panel;
    }

    private static Color ColorOf(Design.SkinOption skin, string token)
    {
        if (skin.External?.Colors.TryGetValue(token, out var color) == true) return color;
        try { defaults ??= new ResourceDictionary { Source = new Uri("pack://application:,,,/Facet;component/Design/Tokens.xaml") }; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Windows.Markup.XamlParseException) { return Colors.Gray; }
        return defaults[token] switch { SolidColorBrush brush => brush.Color, Color value => value, _ => Colors.Gray };
    }

    private async void Refresh()
    {
        if (busy) return;
        busy = true;
        Design.Skins.Rescan();
        Draw();
        Show("목록을 불러오는 중…");
        try {
            entries = (await PackageRegistry.FetchAsync(CancellationToken.None)).Where(entry => entry.Kind == PackageKind.Skin).ToList();
            registryLoaded = true;
            Show("");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException) {
            Show("공개 목록을 불러오지 못했습니다. 인터넷 연결을 확인해 주세요. 설치된 스킨은 그대로 쓸 수 있습니다.", true);
        }
        finally { busy = false; }
        refreshedText.Text = DateTime.Now.ToString("HH:mm") + " 새로고침함  ";
        Draw();
    }

    private async void Install(RegistryEntry entry)
    {
        if (busy) return;
        busy = true; installing = entry.Id;
        Draw();
        Show($"{entry.Name} 내려받는 중…");
        try {
            await PackageRegistry.InstallAsync(entry, dataRoot, CancellationToken.None);
            Design.Skins.Rescan();
            if (entry.Id == selected) app?.ApplySkin(entry.Id);
            changed();
            Show($"{entry.Name} 스킨을 설치했습니다. 설치된 스킨에서 골라 적용하세요.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or IOException or UnauthorizedAccessException) {
            Show(ex is InvalidDataException ? ex.Message : "내려받지 못했습니다. 인터넷 연결을 확인해 주세요.", true);
        }
        finally { busy = false; installing = null; Draw(); }
    }

    private void InstallFile()
    {
        var dialog = new OpenFileDialog { Filter = "스킨 패키지 (*.zip)|*.zip", Title = "설치할 스킨 zip 선택" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try {
            var info = Packages.Install(dialog.FileName, dataRoot, info => info.Kind == PackageKind.Skin);
            Design.Skins.Rescan();
            changed();
            Show($"{info.Name} 스킨을 설치했습니다. 설치된 스킨에서 골라 적용하세요.");
        }
        catch (InvalidDataException ex) { Show(ex.Message.StartsWith("패키지 내용이", StringComparison.Ordinal) ? "스킨 패키지가 아닙니다. 플러그인은 플러그인 탭에서 설치하세요." : ex.Message, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Show(ex.Message, true); }
        Draw();
    }

    // The folder goes away; a skin in use first hands over to the default skin.
    private void Remove(Design.SkinOption skin)
    {
        if (skin.External == null) return;
        if (MessageBox.Show(Window.GetWindow(this)!, $"{skin.Name} 스킨을 제거할까요? 스킨 폴더가 삭제되고 되돌릴 수 없습니다.", "스킨 제거", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        if (skin.Id == selected) { selected = Design.Skins.DefaultId; SelectionChanged?.Invoke(); }
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => {
            try {
                Directory.Delete(skin.External.Directory, true);
                Show($"{skin.Name} 스킨을 제거했습니다.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Show($"{skin.Name} 스킨 파일이 사용 중이라 지우지 못했습니다. 앱을 다시 시작한 뒤 제거해 주세요.", true); }
            Design.Skins.Rescan();
            changed();
            Draw();
        }));
    }

    private void Submit()
    {
        var dialog = new OpenFolderDialog { Title = "올릴 스킨 폴더 선택" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { Show(PackagesPanel.Submit(dialog.FolderName, dataRoot)); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Show(ex.Message, true); }
    }
}
