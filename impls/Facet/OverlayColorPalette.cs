using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace Facet;

public sealed class OverlayColorPalette : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(string), typeof(OverlayColorPalette),
        new PropertyMetadata(OverlayAppearance.DefaultColor, (owner, _) => ((OverlayColorPalette)owner).RefreshSelection()));
    public string SelectedColor { get => (string)GetValue(SelectedColorProperty); set => SetValue(SelectedColorProperty, value); }
    public event EventHandler? SelectionChanged;
    private readonly List<(Button Button, string Color)> choices = [];
    private readonly TextBlock selection = new() { FontSize = 12, Margin = new(0, 4, 0, 0) };

    public OverlayColorPalette()
    {
        var layout = new StackPanel();
        var palette = new UniformGrid { Rows = 2, Columns = 8, Width = 288 };
        foreach (var (name, color) in new (string, string)[] {
            ("기본", "#141A1D"), ("검정", "#000000"), ("진회색", "#303030"), ("회색", "#666666"), ("밝은 회색", "#AAAAAA"), ("흰색", "#FFFFFF"),
            ("짙은 남색", "#152238"), ("남색", "#263859"), ("파랑", "#3265A8"), ("하늘", "#91BFFF"), ("청록", "#236B73"), ("연한 청록", "#8BCDD2"),
            ("짙은 초록", "#183D32"), ("초록", "#347D59"), ("민트", "#82CFAD"), ("올리브", "#667341") }) {
            OverlayAppearance.TryColor(color, out var rgb);
            var button = new Button { Width = 30, Height = 30, Padding = new(3), Margin = new(0, 0, 6, 6),
                ContentTemplate = null, Content = new Border { Width = 18, Height = 18, Background = new SolidColorBrush(rgb), CornerRadius = new(3) }, ToolTip = name + " " + color };
            AutomationProperties.SetName(button, "배경색 " + name + " " + color);
            button.Click += (_, _) => SelectedColor = color;
            choices.Add((button, color)); palette.Children.Add(button);
        }
        layout.Children.Add(new Viewbox { Child = palette, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            MaxWidth = 288, HorizontalAlignment = HorizontalAlignment.Left });
        layout.Children.Add(selection); Content = layout;
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        foreach (var (button, color) in choices) {
            bool selected = string.Equals(color, SelectedColor, StringComparison.OrdinalIgnoreCase);
            button.BorderThickness = new(selected ? 3 : 1);
            button.BorderBrush = (Brush)FindResource(selected ? "Mint" : "Edge");
            AutomationProperties.SetItemStatus(button, selected ? "선택됨" : "");
        }
        selection.Text = SelectedColor?.ToUpperInvariant();
        selection.Foreground = (Brush)FindResource("Muted");
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
