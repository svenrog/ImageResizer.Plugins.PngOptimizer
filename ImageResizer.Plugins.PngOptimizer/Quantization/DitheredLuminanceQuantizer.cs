using System;
using System.Collections.Generic;
using System.Drawing;
using ImageResizer.Plugins.PngOptimizer.Models;
using nQuant;

namespace ImageResizer.Plugins.PngOptimizer.Quantization
{
    public class DitheredLuminanceQuantizer : WuQuantizerBase, IWuQuantizer
    {
        //Colorspace centric luminance
        private const double _luminance_a = 0.1;
        private const double _luminance_r = 0.2126;
        private const double _luminance_g = 0.7152;
        private const double _luminance_b = 0.0722;

        //Percieved luminance 
        //private const double _luminance_r = 3 * 0.299;
        //private const double _luminance_g = 3 * 0.587;
        //private const double _luminance_b = 3 * 0.114;

        private readonly byte _ditherThreshold;

        private readonly int _width;
        private readonly int _height;

        private readonly int[] _bayer = {
            0, 32,  8, 40,  2, 34, 10, 42,   /* 8x8 Bayer ordered dithering  */
            48, 16, 56, 24, 50, 18, 58, 26,  /* pattern.  Each input pixel   */
            12, 44,  4, 36, 14, 46,  6, 38,  /* is scaled to the 0..63 range */
            60, 28, 52, 20, 62, 30, 54, 22,  /* before looking in this table */
            3, 35, 11, 43,  1, 33,  9, 41,   /* to determine the action.     */
            51, 19, 59, 27, 49, 17, 57, 25,
            15, 47,  7, 39, 13, 45,  5, 37,
            63, 31, 55, 23, 61, 29, 53, 21 };

        public DitheredLuminanceQuantizer(int imageWidth, int imageHeight, byte ditherThreshold)
        {
            _width = imageWidth;
            _height = imageHeight;
            _ditherThreshold = ditherThreshold;
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

            Dictionary<int, MappedError> cachedMatches = new Dictionary<int, MappedError>();

            for (int pixelIndex = 0; pixelIndex < pixelsCount; pixelIndex++)
            {
                Pixel pixel = pixels[pixelIndex];
                palette.PixelIndex[pixelIndex] = -1;

                if (pixel.Alpha <= alphaThreshold)
                    continue;

                var x = pixelIndex % _width;
                var y = (pixelIndex - x) / _width;

                //Ordered dither
                if (pixel.Alpha < _ditherThreshold)
                {
                    var orderValue = ((double)_bayer[x % 8 + y % 8 * 8] / 65 - 0.5);
                    
                    pixel = new Pixel(Fit(pixel.Alpha + pixel.Alpha * orderValue * 0.1 + orderValue * 16), pixel.Red, pixel.Green, pixel.Blue);
                }

                MappedError bestMatch;
                int argb = pixel.Argb;

                if (!cachedMatches.TryGetValue(argb, out bestMatch))
                {
                    int match = quantizedPixels[pixelIndex];

                    bestMatch = new MappedError
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
                        var distance =
                            (int)
                                ((double)deltaAlpha * deltaAlpha * _luminance_a +
                                 (double)deltaRed * deltaRed * _luminance_r +
                                 (double)deltaGreen * deltaGreen * _luminance_g +
                                 (double)deltaBlue * deltaBlue * _luminance_b);

                        if (distance >= bestDistance)
                            continue;

                        bestDistance = distance;

                        bestMatch.AlphaError = deltaAlpha;
                        bestMatch.Index = lookupIndex;
                    }

                    cachedMatches[argb] = bestMatch;
                }

                //pixels = ApplyErrorDiffusionDither(x, y, bestMatch, pixels);

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

                var color = Color.FromArgb(alphas[paletteIndex], reds[paletteIndex], greens[paletteIndex], blues[paletteIndex]);
                palette.Colors.Add(color);
            }

            palette.Colors.Add(Color.FromArgb(0, 0, 0, 0));

            return palette;
        }

        #region Error diffusion dithering

        protected virtual IList<Pixel> ApplyErrorDiffusionDither(int x, int y, MappedError mapped, IList<Pixel> pixels)
        {
            //False Floyd-Steinberg
            var twoEightsError = mapped.AlphaError * (2.0 / 8);
            var threeEightsError = mapped.AlphaError * (3.0 / 8);

            pixels = ApplyError(x + 1, y, threeEightsError, pixels);
            pixels = ApplyError(x, y + 1, threeEightsError, pixels);
            pixels = ApplyError(x + 1, y + 1, twoEightsError, pixels);

            // Sierra-3
            //var divisor = 32;
            //var fifth = bestMatch.AlphaError * (5.0 / 32);
            //var third = bestMatch.AlphaError * (3.0 / 32);
            //var second = bestMatch.AlphaError * (2.0 / 32);
            //var fourth = bestMatch.AlphaError * (2.0 / 32);

            //pixels = ApplyError(x + 1, y, fifth, pixels);
            //pixels = ApplyError(x + 2, y, third, pixels);
            //pixels = ApplyError(x - 2, y + 1, second, pixels);
            //pixels = ApplyError(x - 1, y + 1, fourth, pixels);
            //pixels = ApplyError(x, y + 1, fifth, pixels);
            //pixels = ApplyError(x + 1, y + 1, fourth, pixels);
            //pixels = ApplyError(x + 2, y + 1, second, pixels);
            //pixels = ApplyError(x - 1, y + 2, second, pixels);
            //pixels = ApplyError(x, y + 2, third, pixels);
            //pixels = ApplyError(x + 1, y + 2, second, pixels);

            return pixels;
        }

        protected virtual IList<Pixel> ApplyError(int x, int y, double error, IList<Pixel> pixels)
        {
            // Avoid applying dither outside of bounds of image
            // Index calculation will wrap position around to next row

            if (x < 0) return pixels;
            if (x >= _width) return pixels;
            if (y >= _height) return pixels;
            if (y < 0) return pixels;

            // Get the new position inside the array
            var index = x + y * _width;
            var pixel = pixels[index];

            // If alpha is above the threshold, don't dither
            if (pixel.Alpha > _ditherThreshold) return pixels;

            pixels[index] = new Pixel(Fit(pixel.Alpha + error), pixel.Red, pixel.Green, pixel.Blue);

            return pixels;
        }

        protected virtual byte Fit(double value)
        {
            if (value < 1) return 0;
            if (value > 255) return 255;
            return (byte)value;
        }
    }

    #endregion
}