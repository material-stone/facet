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
        Children.Add(Note("체크하면 켜지고, 휴지통 버튼으로 삭제합니다. 플러그인은 Facet과 같은 권한으로 실행됩니다."));
        if (removalNote != null) { Children.Add(Note(removalNote)); removalNote = null; }
        var plugins = owner.DiscoverPlugins();
        if (plugins.Count == 0) Children.Add(Note("설치된 플러그인이 없습니다. 아래 \"플러그인 받기\"에서 설치하세요."));
        foreach (var plugin in plugins) {
            bool granted = owner.HasPluginGrant(plugin.Id);
            var section = owner.IsPluginEnabled(plugin.Id) ? owner.PluginRuntime?.Section(plugin.Id) : null;
            string title = plugin.Name + (plugin.Version.Length > 0 ? " " + plugin.Version : "");
            var toggle = new CheckBox { IsChecked = granted, VerticalAlignment = VerticalAlignment.Center, ToolTip = "플러그인 ID: " + plugin.Id };
            System.Windows.Automation.AutomationProperties.SetName(toggle, title);
            toggle.Click += (_, _) => owner.SetPluginEnabled(plugin, toggle.IsChecked == true);
            var frame = section == null ? null : Section(plugin.Id, section);
            var expander = Expander(plugin.Id, title, granted, frame);
            Grid.SetColumn(expander, 1);
            var reload = new Button { Content = new Design.Icon { Kind = Design.IconKind.Refresh, Width = 16, Height = 16, Foreground = (Brush)FindResource("Ink") }, ContentTemplate = null, Width = 32, Height = 32, Padding = new(0), Margin = new(8, 0, 0, 0), IsEnabled = granted, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(reload, plugin.Name + " 다시 불러오기");
            reload.ToolTip = "다시 불러오기";
            reload.Click += (_, _) => {
                reloadNotes[plugin.Id] = owner.ReloadPlugin(plugin.Id) ? "다시 불러왔습니다." : "다시 불러오지 못했습니다.";
                Refresh();
            };
            Grid.SetColumn(reload, 2);
            var remove = new Button { Content = new Design.Icon { Kind = Design.IconKind.Trash, Width = 16, Height = 16, Foreground = (Brush)FindResource("Ink") }, ContentTemplate = null, Width = 32, Height = 32, Padding = new(0), Margin = new(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "삭제" };
            System.Windows.Automation.AutomationProperties.SetName(remove, plugin.Name + " 삭제");
            remove.Click += (_, _) => { confirmingRemoval = confirmingRemoval == plugin.Id ? null : plugin.Id; Refresh(); };
            Grid.SetColumn(remove, 3);
            var header = new Grid { Margin = new(0, 4, 0, 2) };
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new());
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            header.Children.Add(toggle);
            header.Children.Add(expander);
            header.Children.Add(reload);
            header.Children.Add(remove);
            Children.Add(header);
            if (confirmingRemoval == plugin.Id) Children.Add(RemovalChoice(plugin));
            if (reloadNotes.Remove(plugin.Id, out var note)) Children.Add(Note(note));
            if (owner.PluginState(plugin.Id) is { Length: > 0 } state) Children.Add(Note(state));
            if (frame != null) Children.Add(frame);
        }
        var folder = new Button { Content = "플러그인 폴더 열기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 10, 0, 0) };
        folder.Click += (_, _) => {
            Directory.CreateDirectory(owner.PluginFolder);
            Process.Start(new ProcessStartInfo(owner.PluginFolder) { UseShellExecute = true })?.Dispose();
        };
        Children.Add(folder);
        refreshing = false;
    }

    // Asks before deleting: the folder always goes, the plugin's records and settings only when chosen (ADR 0097).
    private UIElement RemovalChoice(PluginManifest plugin)
    {
        var row = new WrapPanel { Margin = new(40, 2, 0, 6) };
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

    // The row beside the checkbox: its name, and a fold control only when the plugin is on and has settings.
    private ToggleButton Expander(string pluginId, string title, bool granted, Border? frame)
    {
        var name = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, granted ? "Ink" : "Muted");
        var content = new Grid { Background = Brushes.Transparent, Margin = new(8, 0, 0, 0) };
        content.ColumnDefinitions.Add(new());
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.Children.Add(name);
        var button = new ToggleButton { Content = content, IsEnabled = frame != null, IsChecked = frame != null && expanded.Contains(pluginId), Focusable = frame != null, Template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) } };
        System.Windows.Automation.AutomationProperties.SetName(button, title + " 설정 접기/펼치기");
        if (frame == null) return button;
        button.Cursor = Cursors.Hand;
        var rotation = new RotateTransform();
        var chevron = new Design.Icon { Kind = Design.IconKind.Chevron, Width = 16, Height = 16, Foreground = (Brush)FindResource("Muted"), Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new(0.5, 0.5), RenderTransform = rotation };
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
        var frame = new Border { BorderThickness = new(1), CornerRadius = new(8), Padding = new(12, 6, 12, 10), Margin = new(0, 4, 0, 4), Child = fields };
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

    private TextBlock Note(string text) => new() { Text = text, FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 4) };
}
