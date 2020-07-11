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

        private const double _byteInverted = 1.0 / 256.0;

        //Percieved luminance 
        private const double _luminance_r = 0.299;
        private const double _luminance_g = 0.587;
        private const double _luminance_b = 0.114;

        private readonly byte _ditherThreshold;
        private readonly byte _ditherAmount;
        private readonly byte _targetColorCount;

        private readonly int _width;
        private readonly bool _debug;

        // Bayer ordered dithering is used because it simply does not appear grainy if images are presented in sequence

        private readonly int[] _bayerPattern = {
            0, 32,  8, 40,  2, 34, 10, 42,   /* 8x8 Bayer ordered dithering  */
            48, 16, 56, 24, 50, 18, 58, 26,  /* pattern.  Each input pixel   */
            12, 44,  4, 36, 14, 46,  6, 38,  /* is scaled to the 0..63 range */
            60, 28, 52, 20, 62, 30, 54, 22,  /* before looking in this table */
            3, 35, 11, 43,  1, 33,  9, 41,   /* to determine the action.     */
            51, 19, 59, 27, 49, 17, 57, 25,
            15, 47,  7, 39, 13, 45,  5, 37,
            63, 31, 55, 23, 61, 29, 53, 21 };

        private readonly double[] _adjustedPattern = new double[64];

        public DitheredLuminanceQuantizer(int width, byte targetColorCount, byte ditherThreshold, byte ditherAmount = 12, bool debug = false)
        {
            _width = width;
            _ditherThreshold = ditherThreshold;
            _ditherAmount = ditherAmount;
            _targetColorCount = targetColorCount;

            _debug = debug;

            for (var i = 0; i < 64; i++)
                _adjustedPattern[i] = _bayerPattern[i] / 65.0 - 0.5;
        }

        protected override QuantizedPalette GetQuantizedPalette(int colorCount, ColorData data, IEnumerable<Box> cubes, int alphaThreshold)
        {
            if (_targetColorCount > 0)
                colorCount = _targetColorCount;

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

                if (pixel.Alpha > 1 && _ditherAmount > 0)
                {
                    // Get bayer dithering.
                    var ov = _adjustedPattern[x % 8 + y % 8 * 8] * _ditherAmount;

                    // Check for semi transparent areas
                    if (pixel.Alpha < _ditherThreshold)
                    {
                        ov *= 1.5;

                        // Apply dithering with a magnitude of 2 to the alpha
                        pixel = new Pixel(Fit(pixel.Alpha + ov), pixel.Red, pixel.Green, pixel.Blue);
                    }
                    else
                    {
                        var l = (pixel.Red * _luminance_r + pixel.Blue * _luminance_b + pixel.Green * _luminance_g) * _byteInverted;

                        if (l > 0.05 && l < 0.95)
                        {
                            if (_debug)
                            {
                                // Draw fuchsia on solid dither area
                                pixel = new Pixel(255, 255, 0, 128);
                            }
                            else
                            {
                                // Apply dithering to the color layer
                                pixel = new Pixel(pixel.Alpha, Fit(pixel.Red + ov), Fit(pixel.Green + ov), Fit(pixel.Blue + ov));
                            }    
                        }
                    }
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

                        // Take luminance into account when calculating color distance (green is always the most percievable, blue is the least).
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

        protected virtual byte Fit(double value)
        {
            if (value < byte.MinValue) return byte.MinValue;
            if (value > byte.MaxValue) return byte.MaxValue;
            return (byte)value;
        }
    }
}