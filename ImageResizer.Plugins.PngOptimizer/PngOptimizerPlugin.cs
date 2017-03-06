using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using ImageResizer.Configuration;
using ImageResizer.Plugins.PngOptimizer.Analyzers;
using ImageResizer.Plugins.PngOptimizer.Quantization;
using ImageResizer.Resizing;

namespace ImageResizer.Plugins.PngOptimizer
{
    public class PngOptimizerPlugin : BuilderExtension, IPlugin, IQuerystringPlugin
    {
        private const int _maxcolors = 5120;
        private const int _maxdither = 12;
        private const byte _ditherThreshold = 210;

        public IPlugin Install(Config c)
        {
            c.Plugins.add_plugin(this);
            return this;
        }

        public bool Uninstall(Config c)
        {
            c.Plugins.remove_plugin(this);
            return true;
        }

        public IEnumerable<string> GetSupportedQuerystringKeys()
        {
            return new[]
            {
                "optimized",
                "debugoptimizer"
            };
        }
        
        protected override RequestedAction PostRenderImage(ImageState state)
        {
            var enabled = DetermineEnabled(state);
            if (!enabled)
                return RequestedAction.None;

            // WuQuantizer leads to unwanted effects
            // Color vibrancy loss in saturated colors (fixed by introducing luminance in palette calculation)
            // Smooth transparent areas are jarry (adjusted by dithering below set threshold on alpha channel only)

            long colors = 0;
            byte ditherAmount = 0;
            var bitmap = state.destBitmap;

            try
            {
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
                var analyzer = new PaletteAnalyzer(data);
                bitmap.UnlockBits(data);
                colors = analyzer.Colors;
            }
            catch (Exception)
            {
                // ignored
            }

            if (colors > 256)
            {
                ditherAmount = (byte)(_maxdither * (Math.Min(_maxcolors, colors) / _maxcolors));
                colors = 256;
            }

            var quantizer = new DitheredLuminanceQuantizer(state.destBitmap.Width, state.destBitmap.Height, (byte)colors, _ditherThreshold, ditherAmount, DetermineDebug(state));

            try
            {
                state.destBitmap = (Bitmap) quantizer.QuantizeImage(state.destBitmap, 1, 1);
            }
            catch (Exception)
            {
                // ignored
            }

            return RequestedAction.None;
        }

        protected bool DetermineEnabled(ImageState state)
        {
            if (state.destBitmap == null) return false;
            if (state.settings == null) return false;

            var setting = state.settings["optimized"];

            if (string.IsNullOrEmpty(setting)) return IsPngFile(state);
            if (setting == "0") return false;
            if (setting.Equals("false", StringComparison.InvariantCultureIgnoreCase)) return false;

            return true;
        }

        protected bool IsPngFile(ImageState state)
        {
            if (state.Job == null) return false;
            return state.Job.ResultFileExtension.Equals("png", StringComparison.InvariantCultureIgnoreCase);
        }

        protected bool DetermineDebug(ImageState state)
        {
            var setting = state.settings["debugoptimizer"];

            if (string.IsNullOrEmpty(setting)) return false;
            if (setting == "0") return false;
            if (setting.Equals("false", StringComparison.InvariantCultureIgnoreCase)) return false;

            return true;
        }
    }
}