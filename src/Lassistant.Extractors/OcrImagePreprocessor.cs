using Windows.Graphics.Imaging;

namespace Lassistant.Extractors;

internal static class OcrImagePreprocessor
{
    private const uint MinWidth = 2400;
    private const uint MaxWidth = 4500;

    public static async Task<SoftwareBitmap> PrepareAsync(BitmapDecoder decoder)
    {
        var width = decoder.PixelWidth;
        var targetWidth = width < MinWidth
            ? Math.Min(MaxWidth, Math.Max(MinWidth, width * 3))
            : Math.Min(MaxWidth, width * 2);

        var transform = new BitmapTransform
        {
            ScaledWidth = targetWidth,
            InterpolationMode = BitmapInterpolationMode.Fant
        };

        var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask().ConfigureAwait(false);

        return bitmap;
    }
}
