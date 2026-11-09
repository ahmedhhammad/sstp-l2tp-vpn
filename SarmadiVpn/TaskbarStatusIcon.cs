using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SarmadiVpn.Core;

namespace SarmadiVpn;

internal static class TaskbarStatusIcon
{
    private const int IconSize = 128;

    public static BitmapSource Create(ImageSource baseIcon, VpnState state)
    {
        ArgumentNullException.ThrowIfNull(baseIcon);

        var color = state switch
        {
            VpnState.Connected => Color.FromRgb(34, 220, 95),
            VpnState.Connecting or VpnState.Disconnecting => Color.FromRgb(240, 180, 20),
            _ => Color.FromRgb(220, 53, 69)
        };

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(baseIcon, new Rect(0, 0, IconSize, IconSize));

            // The source logo has clear white space here. At taskbar scale the
            // outlined marker remains visible without covering the lettering.
            const double center = 22;
            drawing.DrawEllipse(Brushes.White, null, new Point(center, center), 21, 21);
            drawing.DrawEllipse(new SolidColorBrush(color), null, new Point(center, center), 17, 17);
        }

        var bitmap = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
