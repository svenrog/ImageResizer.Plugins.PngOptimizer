using System;
using System.Collections.Generic;
using System.Drawing;
using nQuant;

namespace ImageResizer.Plugins.PngOptimizer.Quantization
{
    public class DitheredLuminanceQuantizer : WuQuantizerBase, IWuQuantizer
    {
        private const double _luminance_r = 0.299;
        private const double _luminance_g = 0.587;
        private const double _luminance_b = 0.114;

        private readonly int _width;
        private readonly int _height;

        public DitheredLuminanceQuantizer(int imageWidth, int imageHeight)
        {
            _width = imageWidth;
            _height = imageHeight;
        }

        protected override QuantizedPalette GetQuantizedPalette(int colorCount, ColorData data, IEnumerable<Box> cubes, int alphaThreshold)
        {
            int imageSize = data.PixelsCount;
            LookupData lookups = BuildLookups(cubes, data);

            IList<int> quantizedPixels = data.QuantizedPixels;
            for (var index = 0; index < imageSize; ++index)
            {
                var indexParts = BitConverter.GetBytes(quantizedPixels[index]);
                quantizedPixels[index] =
                    lookups.Tags[indexParts[Alpha], indexParts[Red], indexParts[Green], indexParts[Blue]];
            }

            var alphas = new int[colorCount + 1];
            var reds = new int[colorCount + 1];
            var greens = new int[colorCount + 1];
            var blues = new int[colorCount + 1];
            var sums = new int[colorCount + 1];
            var palette = new QuantizedPalette(imageSize);

            IList<Pixel> pixels = data.Pixels;
            int pixelsCount = data.PixelsCount;
            IList<Lookup> lookupsList = lookups.Lookups;
            int lookupsCount = lookupsList.Count;

            Dictionary<int, MappedPixel> cachedMaches = new Dictionary<int, MappedPixel>();

            for (int pixelIndex = 0; pixelIndex < pixelsCount; pixelIndex++)
            {
                Pixel pixel = pixels[pixelIndex];
                palette.PixelIndex[pixelIndex] = -1;

                if (pixel.Alpha <= alphaThreshold)
                    continue;

                MappedPixel bestMatch;
                int argb = pixel.Argb;

                if (!cachedMaches.TryGetValue(argb, out bestMatch))
                {
                    int match = quantizedPixels[pixelIndex];

                    bestMatch = new MappedPixel
                    {
                        Index = match
                    };

                    int bestDistance = int.MaxValue;

                    for (int lookupIndex = 0; lookupIndex < lookupsCount; lookupIndex++)
                    {
                        Lookup lookup = lookupsList[lookupIndex];

                        var deltaAlpha = pixel.Alpha - lookup.Alpha;
                        var deltaRed = pixel.Red - lookup.Red;
                        var deltaGreen = pixel.Green - lookup.Green;
                        var deltaBlue = pixel.Blue - lookup.Blue;

                        // Take luminance into account when calculating distance
                        int distance =
                            (int)
                                (deltaAlpha * deltaAlpha + 
                                 deltaRed * deltaRed * _luminance_r * 3 +
                                 deltaGreen * deltaGreen * _luminance_g * 3 + 
                                 deltaBlue * deltaBlue * _luminance_b * 3);

                        if (distance >= bestDistance)
                            continue;

                        bestDistance = distance;

                        bestMatch.Error = new DeltaPixel(deltaAlpha, deltaRed, deltaGreen, deltaBlue);
                        bestMatch.Index = lookupIndex;
                    }

                    cachedMaches[argb] = bestMatch;
                }

                pixels = ApplyError(bestMatch, pixels);

                alphas[bestMatch.Index] += pixel.Alpha;
                reds[bestMatch.Index] += pixel.Red;
                greens[bestMatch.Index] += pixel.Green;
                blues[bestMatch.Index] += pixel.Blue;
                sums[bestMatch.Index]++;

                palette.PixelIndex[pixelIndex] = bestMatch.Index;
            }

            for (var paletteIndex = 0; paletteIndex < colorCount; paletteIndex++)
            {
                if (sums[paletteIndex] > 0)
                {
                    alphas[paletteIndex] /= sums[paletteIndex];
                    reds[paletteIndex] /= sums[paletteIndex];
                    greens[paletteIndex] /= sums[paletteIndex];
                    blues[paletteIndex] /= sums[paletteIndex];
                }

                var color = Color.FromArgb(alphas[paletteIndex], reds[paletteIndex], greens[paletteIndex],
                    blues[paletteIndex]);
                palette.Colors.Add(color);
            }

            palette.Colors.Add(Color.FromArgb(0, 0, 0, 0));

            return palette;
        }

        protected virtual IList<Pixel> ApplyError(MappedPixel mapped, IList<Pixel> pixels)
        {
            // Apply error to surrounding pixels
            var x = mapped.Index % _width;
            var y = (mapped.Index - x) / _width;

            // Sierra-3 dithering
            var divisor = 1;

            pixels = ApplyErrorAtPosition(x + 1, y    , 5, divisor, mapped.Error, pixels);
            pixels = ApplyErrorAtPosition(x + 2, y    , 3, divisor, mapped.Error, pixels);
            pixels = ApplyErrorAtPosition(x - 2, y + 1, 2, divisor, mapped.Error, pixels);
            pixels = ApplyErrorAtPosition(x - 1, y + 1, 4, divisor, mapped.Error, pixels);
            pixels = ApplyErrorAtPosition(x    , y + 1, 5, divisor, mapped.Error, pixels);
            pixels = ApplyErrorAtPosition(x + 1, y + 1, 4, divisor, mapped.Error, pixels);
            pixels = ApplyErrorAtPosition(x + 2, y + 1, 2, divisor, mapped.Error, pixels);
            pixels = ApplyErrorAtPosition(x - 1, y + 2, 2, divisor, mapped.Error, pixels);
            pixels = ApplyErrorAtPosition(x    , y + 2, 3, divisor, mapped.Error, pixels);
            pixels = ApplyErrorAtPosition(x + 1, y + 2, 2, divisor, mapped.Error, pixels);
            return pixels;
        }

        protected virtual IList<Pixel> ApplyErrorAtPosition(int x, int y, int numerator, int divisor, DeltaPixel delta, IList<Pixel> pixels)
        {
            if (x < 0) return pixels;
            if (x >= _width) return pixels;
            if (y >= _height) return pixels;
            if (y < 0) return pixels;

            var index = x + y * _width;
            var pixel = pixels[index];
            double factor = (double)numerator / divisor;

            var a = pixel.Alpha + Math.Round(delta.Alpha * factor);
            var r = pixel.Red + Math.Round(delta.Red * factor);
            var g = pixel.Green + Math.Round(delta.Green * factor);
            var b = pixel.Blue + Math.Round(delta.Blue * factor);

            pixels[index] = new Pixel((byte)a, (byte)r, (byte)g, (byte)b);

            return pixels;
        }
    }
}