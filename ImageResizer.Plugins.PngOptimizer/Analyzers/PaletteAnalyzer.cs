using System.Drawing;
using System.Drawing.Imaging;

namespace ImageResizer.Plugins.PngOptimizer.Analyzers
{
    public class PaletteAnalyzer
    {
        public readonly long Colors;

        public PaletteAnalyzer(BitmapData bitmap)
        {
            Colors = GetColorCount(bitmap);
        }

        private unsafe long GetColorCount(BitmapData bitmap)
        {
            var colorRef = new bool[256 * 256 * 256];
            long colors = 0;

            var bpp = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
            var h = bitmap.Height;
            var w = bitmap.Width;
            var s = bitmap.Stride;
            var s0 = (byte*)bitmap.Scan0;

            unchecked
            {
                for (var y = 0; y < h; y++)
                {
                    var row = s0 + y * s;

                    for (var x = 0; x < w; x++)
                    {
                        var p = x * bpp;
                        var v = (row[p + 2] << 16) | (row[p + 1] << 8) | (row[p]);
                        
                        if (colorRef[v]) continue;

                        colorRef[v] = true;
                        colors++;
                    }
                }
            }

            return colors;
        }
    }
}