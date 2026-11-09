using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SarmadiVpn;
using SarmadiVpn.Core;

namespace SarmadiVpn.Tests;

public sealed class TaskbarStatusIconTests
{
    [Fact]
    public void StatusDotUsesTheUpperLeftWhitespaceAndPreservesTheLogo()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var original = CreateBaseIcon();
                AssertPixel(TaskbarStatusIcon.Create(original, VpnState.Connected), 14, 14, 34, 220, 95);
                AssertPixel(TaskbarStatusIcon.Create(original, VpnState.Disconnected), 14, 14, 220, 53, 69);
                AssertPixel(TaskbarStatusIcon.Create(original, VpnState.Connecting), 14, 14, 240, 180, 20);
                AssertPixel(TaskbarStatusIcon.Create(original, VpnState.Disconnecting), 14, 14, 240, 180, 20);

                var withDot = TaskbarStatusIcon.Create(original, VpnState.Connected);
                AssertPixel(withDot, 64, 76, 0, 0, 0);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static BitmapSource CreateBaseIcon()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 128, 128));
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 70, 128, 12));
        }

        var bitmap = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void AssertPixel(BitmapSource bitmap, int x, int y, byte red, byte green, byte blue)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        Assert.Equal(blue, pixel[0]);
        Assert.Equal(green, pixel[1]);
        Assert.Equal(red, pixel[2]);
        Assert.Equal(255, pixel[3]);
    }
}
