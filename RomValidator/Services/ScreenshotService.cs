using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RomValidator.Services;

public static class ScreenshotService
{
    public static string CaptureWindowScreenshot(Window window)
    {
        var screenshotsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshot");
        Directory.CreateDirectory(screenshotsDir);

        var filename = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        var filePath = Path.Combine(screenshotsDir, filename);

        var width = (int)Math.Max(window.ActualWidth, 1);
        var height = (int)Math.Max(window.ActualHeight, 1);

        var renderTarget = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        renderTarget.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));

        using var stream = File.Create(filePath);
        encoder.Save(stream);

        LoggerService.LogInfo("Screenshot", $"Screenshot saved: {filePath}");

        return filePath;
    }
}
