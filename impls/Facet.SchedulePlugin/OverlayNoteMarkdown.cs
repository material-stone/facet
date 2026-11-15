using System.Windows.Documents;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TableRow = Markdig.Extensions.Tables.TableRow;
using TableCell = Markdig.Extensions.Tables.TableCell;

namespace Facet.SchedulePlugin;

// Render local note syntax as WPF text. Links/images never load external content.
internal static class OverlayNoteMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough).DisableHtml().Build();

    public static FrameworkElement Render(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return new TextBlock { Text = "클릭하거나 Enter를 눌러 노트를 작성하세요", Foreground = Brush("Muted"), IsHitTestVisible = false };
        var result = RenderBlocks(Markdown.Parse(source,Pipeline));
        result.IsHitTestVisible = false;
        return result;
    }

    private static StackPanel RenderBlocks(ContainerBlock blocks)
    {
        var panel = new StackPanel();
        foreach (var block in blocks) {
            FrameworkElement element;
            switch (block) {
                case Markdig.Extensions.Tables.Table table:
                    element = RenderTable(table); break;
                case ListBlock list:
                    var items = new StackPanel(); int index = int.TryParse(list.OrderedStart,out int start) ? start : 1;
                    foreach (var item in list.OfType<ListItemBlock>()) {
                        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new(28) }); row.ColumnDefinitions.Add(new());
                        row.Children.Add(new TextBlock { Text = list.IsOrdered ? $"{index++}." : "•", Foreground = Brush("Muted") });
                        var content = RenderBlocks(item); Grid.SetColumn(content,1); row.Children.Add(content); items.Children.Add(row);
                    }
                    element = items; break;
                case QuoteBlock quote:
                    element = new Border { BorderThickness = new(3,0,0,0), BorderBrush = Brush("Mint"), Padding = new(12,4,0,0), Child = RenderBlocks(quote) }; break;
                case CodeBlock code:
                    element = new Border { Background = Brush("Panel"), CornerRadius = new(4), Padding = new(10),
                        Child = new TextBlock { Text = code.Lines.ToString(), FontFamily = ScheduleText.Family, Foreground = Brush("Ink"), TextWrapping = TextWrapping.Wrap } }; break;
                case ThematicBreakBlock:
                    element = new Border { Height = 1, Background = Brush("Edge"), Margin = new(0,8,0,8) }; break;
                case LeafBlock leaf:
                    var text = new TextBlock { Foreground = Brush("Ink"), TextWrapping = TextWrapping.Wrap, FontSize = 14 };
                    if (leaf is HeadingBlock heading) { text.FontSize = heading.Level switch { 1 => 24, 2 => 21, 3 => 18, _ => 16 }; text.FontWeight = FontWeights.SemiBold; }
                    if (leaf.Inline != null) AddInlines(text.Inlines,leaf.Inline);
                    else text.Text = leaf.Lines.ToString();
                    element = text; break;
                case ContainerBlock container:
                    element = RenderBlocks(container); break;
                default: continue;
            }
            element.Margin = new(0,0,0,8); panel.Children.Add(element);
        }
        if (panel.Children.Count > 0) ((FrameworkElement)panel.Children[^1]).Margin = new(0);
        return panel;
    }

    private static Grid RenderTable(Markdig.Extensions.Tables.Table table)
    {
        var grid = new Grid(); int columns = table.OfType<TableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max();
        for (int column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new());
        foreach (var row in table.OfType<TableRow>()) {
            int rowIndex = grid.RowDefinitions.Count; grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int column = 0; column < row.Count; column++) {
                var content = RenderBlocks((TableCell)row[column]);
                if (row.IsHeader) TextElement.SetFontWeight(content,FontWeights.SemiBold);
                var cell = new Border { Padding = new(8), BorderBrush = Brush("Edge"), BorderThickness = new(.5), Child = content };
                Grid.SetRow(cell,rowIndex); Grid.SetColumn(cell,column); grid.Children.Add(cell);
            }
        }
        return grid;
    }

    private static void AddInlines(InlineCollection target, ContainerInline container)
    {
        foreach (var inline in container) {
            switch (inline) {
                case LiteralInline literal: target.Add(new Run(literal.Content.ToString())); break;
                case CodeInline code: target.Add(new Run(code.Content) { FontFamily = ScheduleText.Family, Background = Brush("Panel") }); break;
                case LineBreakInline: target.Add(new LineBreak()); break;
                case TaskList task: target.Add(new Run(task.Checked ? "☑ " : "☐ ")); break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                    else if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.Bold;
                    else span.FontStyle = FontStyles.Italic;
                    AddInlines(span.Inlines,emphasis); target.Add(span); break;
                case LinkInline link:
                    var label = new Span { Foreground = Brush("CalendarSaturday"), TextDecorations = TextDecorations.Underline };
                    AddInlines(label.Inlines,link); target.Add(label); break;
                case AutolinkInline link: target.Add(new Run(link.Url) { Foreground = Brush("CalendarSaturday"), TextDecorations = TextDecorations.Underline }); break;
                case ContainerInline nested: AddInlines(target,nested); break;
            }
        }
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
