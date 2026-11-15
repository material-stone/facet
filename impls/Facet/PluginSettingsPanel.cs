using System.Diagnostics;
using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Facet.Plugins;

namespace Facet;

// Enables plugins and draws each enabled plugin's own settings section from the fields it declared (ADR 0075).
internal sealed class PluginSettingsPanel : StackPanel
{
    private OverlayTabs? owner;
    private bool refreshing;
    private readonly Dictionary<string, string> reloadNotes = [];
    private readonly HashSet<string> expanded = [];
    private string? confirmingRemoval, removalNote;

    // Plugin folders can change while the app runs, so showing the panel reads them again.
    internal PluginSettingsPanel() => IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };

    internal void Connect(OverlayTabs tabs) { owner = tabs; Refresh(); }

    // Opens a plugin's section, for a plugin that sends the user to its own settings.
    internal void Expand(string pluginId) { expanded.Add(pluginId); Refresh(); }

    internal void Refresh()
    {
        if (owner == null || refreshing) return;
        refreshing = true;
        Children.Clear();
        // Sits level with the row's own label, which the wide settings row draws over the panel's top-left.
        var folder = IconButton(Design.IconKind.Folder, "플러그인 폴더 열기");
        folder.HorizontalAlignment = HorizontalAlignment.Right;
        folder.Click += (_, _) => {
            Directory.CreateDirectory(owner.PluginFolder);
            Process.Start(new ProcessStartInfo(owner.PluginFolder) { UseShellExecute = true })?.Dispose();
        };
        Children.Add(folder);
        Children.Add(Note("스위치로 켜고 끕니다. 플러그인은 Facet과 같은 권한으로 실행됩니다."));
        if (removalNote != null) { Children.Add(Note(removalNote)); removalNote = null; }
        var plugins = owner.DiscoverPlugins();
        if (plugins.Count == 0) Children.Add(Note("설치된 플러그인이 없습니다. 아래 \"플러그인 받기\"에서 설치하세요."));
        foreach (var plugin in plugins) Children.Add(Card(plugin));
        Children.Add(new FrameworkElement { Height = 12 });
        refreshing = false;
    }

    // A button with an icon and, unless hidden, a caption, sized like the settings tab's other command buttons.
    internal static Button IconButton(Design.IconKind kind, string text, bool showText = true)
    {
        var icon = new Design.Icon { Kind = kind, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(Design.Icon.ForegroundProperty, "Ink");
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        if (showText) content.Children.Add(new TextBlock { Text = text, Margin = new(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, ContentTemplate = null, Height = 34, Padding = new(12, 0, 12, 0), Margin = new(0), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        if (!showText) button.ToolTip = text;
        return button;
    }

    private PluginCard Card(PluginManifest plugin)
    {
        bool granted = owner!.HasPluginGrant(plugin.Id);
        var section = owner.IsPluginEnabled(plugin.Id) ? owner.PluginRuntime?.Section(plugin.Id) : null;
        string title = plugin.Name + (plugin.Version.Length > 0 ? " " + plugin.Version : "");
        var toggle = new CheckBox { IsChecked = granted, VerticalAlignment = VerticalAlignment.Center, ToolTip = "플러그인 ID: " + plugin.Id, Style = (Style)FindResource("Switch") };
        toggle.Margin = new(8, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(toggle, title);
        toggle.Click += (_, _) => owner.SetPluginEnabled(plugin, toggle.IsChecked == true);
        var frame = section == null ? null : Section(plugin.Id, section);
        var tile = new Border { Width = 36, Height = 36, CornerRadius = new(9), Margin = new(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, "Input");
        var tileIcon = new Design.Icon { Kind = Design.IconKind.Code, Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        tileIcon.SetResourceReference(Design.Icon.ForegroundProperty, granted ? "Mint" : "Muted");
        tile.Child = tileIcon;
        var expander = Expander(plugin, granted, frame);
        Grid.SetColumn(expander, 1);
        Grid.SetColumnSpan(expander, 4);
        // The reload slot keeps its width when the plugin is off so every card's buttons line up.
        var reload = SlotButton(Design.IconKind.Refresh, plugin.Name + " 다시 불러오기", "다시 불러오기");
        reload.IsEnabled = granted;
        if (!granted) reload.Visibility = Visibility.Hidden;
        reload.Click += (_, _) => {
            reloadNotes[plugin.Id] = owner.ReloadPlugin(plugin.Id) ? "다시 불러왔습니다." : "다시 불러오지 못했습니다.";
            Refresh();
        };
        Grid.SetColumn(reload, 2);
        var remove = SlotButton(Design.IconKind.Trash, plugin.Name + " 삭제", "삭제");
        remove.Click += (_, _) => { confirmingRemoval = confirmingRemoval == plugin.Id ? null : plugin.Id; Refresh(); };
        Grid.SetColumn(remove, 3);
        Grid.SetColumn(toggle, 5);
        var header = new Grid();
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new());
        header.ColumnDefinitions.Add(new() { Width = new GridLength(34) });
        header.ColumnDefinitions.Add(new() { Width = new GridLength(34) });
        header.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(tile);
        header.Children.Add(expander);
        header.Children.Add(reload);
        header.Children.Add(remove);
        header.Children.Add(toggle);
        var body = new StackPanel();
        body.Children.Add(header);
        if (confirmingRemoval == plugin.Id) body.Children.Add(RemovalChoice(plugin));
        if (reloadNotes.Remove(plugin.Id, out var note)) body.Children.Add(Note(note, new(48, 6, 0, 0)));
        if (owner.PluginState(plugin.Id) is { Length: > 0 } state) body.Children.Add(Note(state, new(48, 6, 0, 0)));
        if (frame != null) body.Children.Add(frame);
        var card = new PluginCard(header, frame) { Child = body, BorderThickness = new(1), CornerRadius = new(10), Padding = new(12, 10, 12, 10), Margin = new(0, 8, 0, 0) };
        card.SetResourceReference(Border.BorderBrushProperty, "Edge");
        card.SetResourceReference(Border.BackgroundProperty, "Panel");
        return card;
    }

    private Button SlotButton(Design.IconKind kind, string name, string tip)
    {
        var button = new Button { Content = new Design.Icon { Kind = kind, Width = 16, Height = 16, Foreground = (Brush)FindResource("Muted") }, ContentTemplate = null, Width = 30, Height = 30, Padding = new(0), Margin = new(2, 0, 2, 0), Background = Brushes.Transparent, BorderThickness = new(0), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        return button;
    }

    // Asks before deleting: the folder always goes, the plugin's records and settings only when chosen (ADR 0097).
    private UIElement RemovalChoice(PluginManifest plugin)
    {
        var row = new WrapPanel { Margin = new(48, 8, 0, 0) };
        row.Children.Add(new TextBlock { Text = plugin.Name + " 플러그인을 삭제할까요? 위젯도 보드에서 사라집니다.", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 4) });
        void Choice(string label, Action run) {
            var button = new Button { Content = label, Margin = new(0, 0, 6, 4) };
            button.Click += (_, _) => run();
            row.Children.Add(button);
        }
        void Remove(bool data) { confirmingRemoval = null; removalNote = plugin.Name + ": " + owner!.UninstallPlugin(plugin, data); Refresh(); }
        Choice("삭제", () => Remove(false));
        Choice("데이터까지 삭제", () => Remove(true));
        Choice("취소", () => { confirmingRemoval = null; Refresh(); });
        return row;
    }

    // The name and state, spanning to the fold slot; the chevron shows only when the plugin is on and has settings.
    private ToggleButton Expander(PluginManifest plugin, bool granted, Border? frame)
    {
        string pluginId = plugin.Id;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Inlines.Add(new System.Windows.Documents.Run(plugin.Name));
        if (plugin.Version.Length > 0) {
            var version = new System.Windows.Documents.Run("  " + plugin.Version) { FontSize = 12, FontWeight = FontWeights.Normal };
            version.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Muted");
            name.Inlines.Add(version);
        }
        name.SetResourceReference(TextBlock.ForegroundProperty, granted ? "Ink" : "Muted");
        text.Children.Add(name);
        text.Children.Add(new TextBlock { Text = granted ? "켜짐" : "꺼짐", FontSize = 12, Foreground = (Brush)FindResource("Muted") });
        var content = new Grid { Background = Brushes.Transparent };
        content.ColumnDefinitions.Add(new());
        content.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        content.Children.Add(text);
        string title = plugin.Name + (plugin.Version.Length > 0 ? " " + plugin.Version : "");
        var button = new ToggleButton { Content = content, IsEnabled = frame != null, IsChecked = frame != null && expanded.Contains(pluginId), Focusable = frame != null, Template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) } };
        System.Windows.Automation.AutomationProperties.SetName(button, title + " 설정 접기/펼치기");
        if (frame == null) return button;
        button.Cursor = Cursors.Hand;
        var rotation = new RotateTransform();
        var chevron = new Design.Icon { Kind = Design.IconKind.Chevron, Width = 16, Height = 16, Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new(0.5, 0.5), RenderTransform = rotation };
        Grid.SetColumn(chevron, 1);
        content.Children.Add(chevron);
        void Apply() {
            bool open = button.IsChecked == true;
            if (open) expanded.Add(pluginId); else expanded.Remove(pluginId);
            frame.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            rotation.Angle = open ? 0 : -90;
        }
        button.Checked += (_, _) => Apply();
        button.Unchecked += (_, _) => Apply();
        Apply();
        return button;
    }

    private Border Section(string pluginId, PluginSettingsSection section)
    {
        var fields = new StackPanel();
        string group = "";
        foreach (var field in section.Fields) {
            if (field.Group != group && field.Group.Length > 0) {
                var heading = new TextBlock { Text = field.Group, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new(0, fields.Children.Count == 0 ? 6 : 18, 0, 2) };
                heading.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
                fields.Children.Add(heading);
            }
            group = field.Group;
            // Descriptions stay one hover away, like the app's own setting names.
            var label = new TextBlock { Text = field.Label, Margin = new(0, 6, 0, 4), TextWrapping = TextWrapping.Wrap };
            if (field.Description.Length > 0) { label.ToolTip = field.Description; ToolTipService.SetInitialShowDelay(label, 350); ToolTipService.SetShowDuration(label, 20000); }
            fields.Children.Add(label);
            var message = Note("");
            var editor = Editor(pluginId, field, message);
            System.Windows.Automation.AutomationProperties.SetName(editor, field.Label);
            fields.Children.Add(editor);
            fields.Children.Add(message);
        }
        var frame = new Border { BorderThickness = new(0, 1, 0, 0), Padding = new(0, 4, 0, 0), Margin = new(0, 10, 0, 0), Child = fields };
        frame.SetResourceReference(Border.BorderBrushProperty, "Edge");
        return frame;
    }

    private FrameworkElement Editor(string pluginId, SettingField field, TextBlock message)
    {
        var host = owner!.PluginRuntime!;
        string current = host.ReadSetting(pluginId, field.Key);
        void Write(string value) => message.Text = host.WriteSetting(pluginId, field, value) ? "" : owner.PluginRuntime == null ? "" : "저장하지 못했습니다. 값을 확인해 주세요.";
        switch (field) {
            case SliderSetting slider: {
                double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out double value);
                string Format(double number) => number.ToString("0.##", CultureInfo.CurrentCulture);
                var bar = new Slider { Minimum = slider.Minimum, Maximum = slider.Maximum, SmallChange = slider.Step, LargeChange = slider.Step, TickFrequency = slider.Step, IsSnapToTickEnabled = slider.Step > 0, Value = value };
                var shown = new TextBlock { Text = Format(bar.Value), HorizontalAlignment = HorizontalAlignment.Right };
                // Dragging would otherwise write the encrypted records on every tick.
                var pending = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                pending.Tick += (_, _) => { pending.Stop(); Write(bar.Value.ToString(CultureInfo.InvariantCulture)); };
                bar.ValueChanged += (_, _) => { shown.Text = Format(bar.Value); pending.Stop(); pending.Start(); };
                var stack = new StackPanel(); stack.Children.Add(bar); stack.Children.Add(shown);
                return stack;
            }
            case ColorSetting: {
                var palette = new OverlayColorPalette { SelectedColor = current };
                palette.SelectionChanged += (_, _) => Write(palette.SelectedColor);
                return palette;
            }
            case TextListSetting: {
                var box = new TextBox { Text = current, AcceptsReturn = true, MinHeight = 72, TextWrapping = TextWrapping.Wrap };
                box.LostKeyboardFocus += (_, _) => Write(string.Join("\n", box.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                return box;
            }
            case ChoiceSetting choice: {
                var combo = new ComboBox { ItemsSource = choice.Options, DisplayMemberPath = nameof(SettingOption.Label), SelectedValuePath = nameof(SettingOption.Value), IsTextSearchEnabled = true, MaxDropDownHeight = 300, SelectedValue = current };
                TextSearch.SetTextPath(combo, nameof(SettingOption.Label));
                combo.SelectionChanged += (_, _) => { if (combo.SelectedValue is string value) Write(value); };
                return combo;
            }
            case ToggleSetting: {
                var check = new CheckBox { IsChecked = current == "true", HorizontalAlignment = HorizontalAlignment.Left };
                check.Click += (_, _) => Write(check.IsChecked == true ? "true" : "false");
                return check;
            }
            case CustomSetting custom:
                // Plugin code can throw anything; one bad view must not take the settings tab down.
                try { return custom.Create(); }
                catch (Exception ex) { message.Text = ex.Message; return new Border(); }
            case ActionSetting action: {
                var button = new Button { Content = action.ButtonText, HorizontalAlignment = HorizontalAlignment.Stretch };
                button.Click += async (_, _) => {
                    button.IsEnabled = false;
                    // Plugin code can throw anything; report it here instead of taking the app down.
                    try { message.Text = await action.Run(); }
                    catch (Exception ex) { message.Text = ex.Message; }
                    finally { button.IsEnabled = true; }
                };
                return button;
            }
            default: {
                var box = new TextBox { Text = current };
                box.LostKeyboardFocus += (_, _) => {
                    if (box.Text == host.ReadSetting(pluginId, field.Key)) return;
                    // Plugin code can throw anything; a failed check keeps the typed text unsaved.
                    string? problem;
                    try { problem = (field as TextSetting)?.Check?.Invoke(box.Text); }
                    catch (Exception ex) { problem = ex.Message; }
                    if (problem != null) message.Text = problem; else Write(box.Text);
                };
                return box;
            }
        }
    }

    private TextBlock Note(string text, Thickness? margin = null) => new() { Text = text, FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = margin ?? new(0, 6, 0, 0) };
}

// One installed plugin: its header row and, when it has any, its folding settings.
internal sealed class PluginCard(Grid header, Border? settings) : Border
{
    internal Grid Header { get; } = header;
    internal Border? Settings { get; } = settings;
}
