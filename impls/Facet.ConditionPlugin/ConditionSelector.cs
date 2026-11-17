using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;

namespace Facet.ConditionPlugin;

// Fills its grid slot: the whole tile is the button, and the icon takes the height the label leaves.
public sealed class ConditionSelector : UserControl, IDisposable, Facet.Plugins.IWidgetState
{
    private const double MenuWidth = 264;
    private readonly Image icon = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock empty = new() { Text = "+", TextAlignment = TextAlignment.Center };
    private readonly TextBlock label = new() { Text = "기록하기", FontSize = 13, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock error = new() { Text = "저장하지 못했습니다. 다시 선택해 주세요.", FontSize = 11, MaxWidth = 240, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    internal Button Toggle { get; }
    internal Popup Choices { get; }
    internal IReadOnlyList<Button> Options { get; }
    internal Func<string, DateTimeOffset, bool>? Record { get; set; }
    internal ConditionLive? Live { get; set; }

    // A skin can draw this widget from its state (ADR 0110).
    public string Type => ConditionState.CurrentType;
    public string Json() => ConditionState.Current(Live?.Entries ?? [], DateTimeOffset.Now);
    public event Action? Changed { add => Live?.Watch(value, true); remove => Live?.Watch(value, false); }
    public Task<string?> Act(string name, string argsJson) => ConditionState.Record(name, argsJson, Select);

    public ConditionSelector()
    {
        Focusable = false; KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        var face = new Grid(); face.Children.Add(icon); face.Children.Add(new Viewbox { Child = empty, Stretch = Stretch.Uniform });
        var content = new Grid();
        content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.Children.Add(face); Grid.SetRow(label, 1); content.Children.Add(label);
        Toggle = new Button { Padding = new(2), Content = content, ContentTemplate = null, Margin = new(0), Cursor = Cursors.Hand, Focusable = false, IsTabStop = false, Style = ToggleStyle() };
        AutomationProperties.SetName(Toggle, "컨디션 기록하기"); Content = Toggle;
        var menu = new StackPanel(); menu.Children.Add(new TextBlock { Text = "지금의 컨디션", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new(4,0,0,8) });
        var options = new List<Button>(); var tiles = new UniformGrid { Columns = 3 };
        foreach (var style in ConditionIcons.All) {
            var tile = new StackPanel(); tile.Children.Add(new Image { Source = ConditionIcons.Load(style.Id), Width = 48, Height = 48 });
            tile.Children.Add(new TextBlock { Text = style.Name, FontSize = 12, TextAlignment = TextAlignment.Center });
            var button = new Button { Content = tile, ContentTemplate = null, Width = 76, Height = 80, Padding = new(4), Margin = new(2), Cursor = Cursors.Hand, Tag = style.Id, Focusable = false, IsTabStop = false };
            AutomationProperties.SetName(button, style.Name + "으로 기록");
            button.Click += (_, _) => Select(style.Id, DateTimeOffset.Now);
            tiles.Children.Add(button); options.Add(button);
        }
        Options = options.AsReadOnly(); menu.Children.Add(tiles); menu.Children.Add(error);
        var border = new Border { Child = menu, CornerRadius = new(16), Padding = new(12), Width = MenuWidth };
        border.SetResourceReference(Border.BackgroundProperty, "Panel");
        border.SetResourceReference(ForegroundProperty, "Ink");
        KeyboardNavigation.SetTabNavigation(border, KeyboardNavigationMode.None);
        Choices = new Popup { Child = border, PlacementTarget = Toggle, Placement = PlacementMode.Bottom,
            VerticalOffset = 8, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
        PopupKeys.TakeFocus(Choices, border);
        Toggle.Click += (_, _) => { error.Visibility = Visibility.Collapsed; Choices.HorizontalOffset = (Toggle.ActualWidth - MenuWidth) / 2; Choices.IsOpen = !Choices.IsOpen; };
        border.PreviewKeyDown += (_, e) => { if (e.Key != Key.Escape) return; Choices.IsOpen = false; e.Handled = true; };
        Unloaded += (_, _) => Choices.IsOpen = false;
        IsVisibleChanged += (_, _) => { if (!IsVisible) Choices.IsOpen = false; };
        SetResourceReference(ForegroundProperty, "Ink");
    }

    // The app's button templates center their content, which would leave the icon at its natural size.
    private static Style ToggleStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(TemplateProperty, XamlReader.Parse("""
            <ControlTemplate TargetType="Button" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
             <Border Background="{TemplateBinding Background}" CornerRadius="7" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border>
            </ControlTemplate>
            """)));
        var hover = ((Brush)Application.Current.FindResource("Hover")).CloneCurrentValue();
        hover.Opacity *= .4;
        hover.Freeze();
        style.Triggers.Add(new Trigger { Property = IsMouseOverProperty, Value = true, Setters = { new Setter(BackgroundProperty, hover) } });
        return style;
    }

    internal bool Select(string kind, DateTimeOffset at)
    {
        if (Record?.Invoke(kind, at) != true) { error.Visibility = Visibility.Visible; return false; }
        error.Visibility = Visibility.Collapsed; Choices.IsOpen = false; return true;
    }

    internal void Refresh(IReadOnlyList<ConditionEntry> entries, DateTimeOffset now)
    {
        var entry = ConditionTracking.Current(entries, now); var style = ConditionIcons.Find(entry?.Kind);
        icon.Source = style == null ? null : ConditionIcons.Load(style.Id);
        ((FrameworkElement)empty.Parent).Visibility = style == null ? Visibility.Visible : Visibility.Collapsed;
        label.Text = style?.Name ?? "기록하기";
        foreach (var option in Options) option.SetResourceReference(BackgroundProperty, Equals(option.Tag, style?.Id) ? "Selected" : "Input");
        Toggle.ToolTip = style == null ? "아이콘을 눌러 현재 컨디션을 기록하세요" : $"{style.Name} · {entry!.At.ToLocalTime():M월 d일 HH:mm}부터\n클릭하여 컨디션 변경";
        AutomationProperties.SetName(Toggle, "현재 컨디션 " + (style?.Name ?? "미기록") + ", 변경하기");
    }

    public void Dispose() { Choices.IsOpen = false; Live?.Dispose(); }
}
