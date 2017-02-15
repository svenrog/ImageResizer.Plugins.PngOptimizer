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
        //private const double _luminance_r = 0.2126;
        //private const double _luminance_g = 0.7152;
        //private const double _luminance_b = 0.0722;

        //Percieved luminance 
        private const double _luminance_r = 0.299;
        private const double _luminance_g = 0.587;
        private const double _luminance_b = 0.114;

        private const double _byteInverted = 1.0 / 255.0;

        private readonly byte _ditherThreshold;
        private readonly double _ditherColorThreshold;

        private readonly int _width;
        private readonly int _height;
        private readonly bool _debug;

        // Bayer ordered dithering is used because it simply does not appear grainy if images are presented in sequence

        private readonly int[] _bayer = {
            0, 32,  8, 40,  2, 34, 10, 42,   /* 8x8 Bayer ordered dithering  */
            48, 16, 56, 24, 50, 18, 58, 26,  /* pattern.  Each input pixel   */
            12, 44,  4, 36, 14, 46,  6, 38,  /* is scaled to the 0..63 range */
            60, 28, 52, 20, 62, 30, 54, 22,  /* before looking in this table */
            3, 35, 11, 43,  1, 33,  9, 41,   /* to determine the action.     */
            51, 19, 59, 27, 49, 17, 57, 25,
            15, 47,  7, 39, 13, 45,  5, 37,
            63, 31, 55, 23, 61, 29, 53, 21 };

        public DitheredLuminanceQuantizer(int imageWidth, int imageHeight, byte ditherThreshold, bool debug)
        {
            _width = imageWidth;
            _height = imageHeight;
            _ditherThreshold = ditherThreshold;
            _ditherColorThreshold = _ditherThreshold * 0.15;
            _debug = debug;
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

                // Check for semi transparent areas
                if (pixel.Alpha < _ditherThreshold)
                {
                    // Calculate bayer dithering
                    var orderValue = ((double)_bayer[x % 8 + y % 8 * 8] / 65 - 0.5);
                    
                    // Apply dithering with a magnitude of 8 to the and 10% of the alpha intensity (harder dither on more opaque areas).
                    pixel = new Pixel(Fit(pixel.Alpha + pixel.Alpha * orderValue * 0.1 + orderValue * 8), pixel.Red, pixel.Green, pixel.Blue);
                }
                else 
                {
                    // Check for areas with subtle local color variances
                    var distance = GetMaxSurroundingColorDistance(x, y, pixels);
                    if (distance > _ditherColorThreshold * 0.20 && distance < _ditherColorThreshold * 0.8)
                    {
                        // calculate bayer dithering with a magnitude of 4.
                        var orderValue = ((double)_bayer[x % 8 + y % 8 * 8] / 65 - 0.5) * 4;

                        if (_debug)
                        {
                            // Draw fuchsia on solid dither area
                            pixel = new Pixel(255, 255, 0, 128);
                        }
                        else
                        {
                            // Apply dithering to the local color variance.
                            pixel = new Pixel(pixel.Alpha, Fit(pixel.Red + orderValue), Fit(pixel.Green + orderValue), Fit(pixel.Blue + orderValue));
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

        protected virtual double GetMaxSurroundingColorDistance(int x, int y, IList<Pixel> pixels, int size = 3)
        {
            var dmax = 0.0;
            var xn = x > size - 1 ? -size : 0;
            var xm = x < _width - (size - 1) ? size : 0;
            var yn = y > size - 1 ? -size : 0;
            var ym = y < _height - (size - 1) ? size : 0;

            // Get original pixel index
            var o = (x % _width) + y * _width;

            // Calculate current pixel luminance
            var l = (pixels[o].Red * _luminance_r + pixels[o].Blue * _luminance_b + pixels[o].Green * _luminance_g) * _byteInverted;

            // Don't process really bright areas
            if (l > 0.95) return 0.0;

            // Don't process really dark areas
            if (l < 0.05) return 0.0;

            // Otherwise, sweep surrounding pixels
            for (var xd = xn; xd <= xm; xd++)
            {
                for (var yd = yn; yd < ym; yd++)
                {
                    if (x == 0 && y == 0) continue;

                    // Reverse calculate pixel index from x and y.
                    var i = ((x + xd) % _width) + (y + yd) * _width;

                    // Get red distance
                    var dr = Math.Abs(pixels[i].Red - pixels[o].Red);

                    // Get green
                    var dg = Math.Abs(pixels[i].Green - pixels[o].Green);

                    // Get blue distance
                    var db = Math.Abs(pixels[i].Blue - pixels[o].Blue);
                    
                    // Get the maximum individual color difference for the surrounding pixels
                    var d = Math.Max(dr * _luminance_r, Math.Max(db * _luminance_b, dg * _luminance_g)) * pixels[i].Alpha * _byteInverted;
                    if (d > dmax) dmax = d;
                }
            }

            return dmax;
        }  

        protected virtual byte Fit(double value)
        {
            if (value < 1) return 0;
            if (value > 255) return 255;
            return (byte)value;
        }
    }
}