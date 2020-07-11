using System.Collections.Generic;
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
            var colorRef = new HashSet<int>();
            long colors = 0;

            var bpp = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;

            var h = bitmap.Height;
            var w = bitmap.Width;
            var s = bitmap.Stride;
            var s0 = (byte*)bitmap.Scan0;

            for (var y = 0; y < h; y++)
            {
                var row = s0 + y * s;

                for (var x = 0; x < w; x++)
                {
                    var p = x * bpp;

                    int v = row[p];
                    for (var b = 1; b < bpp; b++)
                    {
                        v |= row[p + b] << (b * 8);
                    }

                    if (colorRef.Contains(v)) continue;

                    colorRef.Add(v);
                    colors++;
                }
            }

            return colors;
        }
    }
}