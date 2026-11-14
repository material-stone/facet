using System.Windows.Media.Imaging;

namespace Facet.Design;

internal static class AppIcon
{
    private const string ResourcePath = "/Facet;component/Design/AppIcon/Facet.ico";
    internal static ImageSource Image { get; } = LoadImage();

    private static Stream Open() => Application.GetResourceStream(new Uri(ResourcePath, UriKind.Relative))?.Stream
        ?? throw new InvalidDataException("Facet 앱 아이콘을 찾을 수 없습니다.");

    private static ImageSource LoadImage()
    {
        using var stream = Open();
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderByDescending(image => image.PixelWidth).First();
        if (frame.PixelWidth != 256 || frame.PixelHeight != 256)
            throw new InvalidDataException("Facet 앱 아이콘에는 256px 프레임이 필요합니다.");
        frame.Freeze();
        return frame;
    }

    internal static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = Open();
        using var icon = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)icon.Clone();
    }
}
