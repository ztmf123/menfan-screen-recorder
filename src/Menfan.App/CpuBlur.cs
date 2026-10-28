using System.Buffers;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Menfan.App;

// Three sliding-window box filters approximate a Gaussian without per-pixel kernel loops.
internal static class CpuBlur
{
    public static BitmapSource Apply(BitmapSource source, double radius, double? focus = null)
    {
        int w = source.PixelWidth, h = source.PixelHeight, size = checked(w * h * 4);
        var pool = ArrayPool<byte>.Shared;
        var first = pool.Rent(size); var second = pool.Rent(size); byte[]? original = focus.HasValue ? pool.Rent(size) : null;
        try
        {
            var input = source.Format == PixelFormats.Pbgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            input.CopyPixels(first, w * 4, 0); if (original != null) Buffer.BlockCopy(first, 0, original, 0, size);
            double sigma = Math.Clamp(radius, 0, 100) / 3;
            int lower = (int)Math.Floor(Math.Sqrt(4 * sigma * sigma + 1)); if (lower % 2 == 0) lower--; lower = Math.Max(1, lower);
            int count = (int)Math.Round((12 * sigma * sigma - 3 * lower * lower - 12 * lower - 9) / (-4d * lower - 4)); count = Math.Clamp(count, 0, 3);
            for (int pass = 0; pass < 3; pass++)
            {
                int r = ((pass < count ? lower : lower + 2) - 1) / 2;
                Box(first, second, w, h, r, true); Box(second, first, w, h, r, false);
            }
            if (original != null)
            {
                double top = Math.Clamp(focus!.Value - .15, .01, .99), bottom = Math.Clamp(focus.Value + .15, .01, .99);
                for (int y = 0; y < h; y++)
                {
                    double position = (y + .5) / h;
                    double mask = position < top ? 1 - position / top : position > bottom ? (position - bottom) / (1 - bottom) : 0;
                    for (int x = 0, index = y * w * 4; x < w; x++, index += 4)
                    {
                        double alpha = first[index + 3] / 255d * mask;
                        for (int c = 0; c < 4; c++) first[index + c] = (byte)Math.Clamp((int)Math.Round(first[index + c] * mask + original[index + c] * (1 - alpha)), 0, 255);
                    }
                }
            }
            var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, first, w * 4); result.Freeze(); return result;
        }
        finally { pool.Return(first); pool.Return(second); if (original != null) pool.Return(original); }
    }
    private static void Box(byte[] input, byte[] output, int w, int h, int radius, bool horizontal)
    {
        if (radius == 0) { Buffer.BlockCopy(input, 0, output, 0, w * h * 4); return; }
        int lines = horizontal ? h : w, length = horizontal ? w : h, step = horizontal ? 4 : w * 4, divisor = 2 * radius + 1;
        Parallel.For(0, lines, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4) }, line => {
            int start = horizontal ? line * w * 4 : line * 4;
            int b = 0, g = 0, r = 0, a = 0;
            for (int j = -radius; j <= radius; j++) { int index = start + Math.Clamp(j, 0, length - 1) * step; b += input[index]; g += input[index + 1]; r += input[index + 2]; a += input[index + 3]; }
            for (int i = 0, index = start; i < length; i++, index += step)
            {
                output[index] = (byte)((b + divisor / 2) / divisor); output[index + 1] = (byte)((g + divisor / 2) / divisor);
                output[index + 2] = (byte)((r + divisor / 2) / divisor); output[index + 3] = (byte)((a + divisor / 2) / divisor);
                int remove = start + Math.Clamp(i - radius, 0, length - 1) * step, add = start + Math.Clamp(i + radius + 1, 0, length - 1) * step;
                b += input[add] - input[remove]; g += input[add + 1] - input[remove + 1]; r += input[add + 2] - input[remove + 2]; a += input[add + 3] - input[remove + 3];
            }
        });
    }
}
