using System.Windows;
using System.Windows.Controls;
using Facet.Plugins;

namespace Facet.SamplePlugin;

public sealed class SamplePlugin : IFacetPlugin
{
    public void Register(IWidgetRegistry registry) => registry.AddWidget(new WidgetDescriptor("greeting", "인사 예제", context => {
        var open = new Button { Content = "플러그인 위젯", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // The modal's content belongs to the plugin; the app only presents it.
        open.Click += (_, _) => context.Modal.Show(new TextBlock { Text = "플러그인이 가진 모달 내용", FontSize = 18 });
        return open;
    })
    { Description = "플러그인 예제 위젯입니다.", DefaultColumns = 3, DefaultRows = 1 });
}
