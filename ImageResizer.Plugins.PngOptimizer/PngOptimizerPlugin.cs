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
        private const int _maxColors = 5120;
        private const int _maxDither = 24;
        private const int _defaultDither = 6;
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
                "optimizePng",
                "optimizePngDebug",
                "dither"
            };
        }
        
        protected override RequestedAction PostRenderImage(ImageState state)
        {
            var enabled = DetermineEnabled(state);
            if (!enabled)
                return RequestedAction.None;

            long colors = 0;
            byte dither = GetDitherSetting(state);
            bool debug = DetermineDebug(state);

            var bitmap = state.destBitmap;

            try
            {
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
                var analyzer = new PaletteAnalyzer(data);

                bitmap.UnlockBits(data);
                colors = analyzer.Colors;
            }
            catch (Exception)
            {
                // ignored
            }

            if (colors > byte.MaxValue)
            {
                var ditherMax = (byte)(_maxDither * (Math.Min(_maxColors, colors) / _maxColors));
                dither = Math.Min(ditherMax, dither);
                colors = byte.MaxValue;
            }
            else
            {
                dither = 0;
            }

            var quantizer = new DitheredLuminanceQuantizer(bitmap.Width, (byte)colors, _ditherThreshold, dither, debug);

            try
            {
                var processedBitmap = (Bitmap)quantizer.QuantizeImage(bitmap, 1, 1);
                state.destBitmap = processedBitmap;
                bitmap.Dispose();
            }
            catch (Exception)
            {
               
            }

            return RequestedAction.None;
        }

        protected virtual byte GetDitherSetting(ImageState state)
        {
            if (state.settings == null) return _defaultDither;

            var setting = state.settings.Get<byte>("dither", _defaultDither);

            if (setting > _maxDither) return _maxDither;
            
            return setting;
        }

        protected virtual bool DetermineEnabled(ImageState state)
        {
            if (state.destBitmap == null) return false;
            if (state.settings == null) return false;

            var setting = state.settings["optimizePng"];

            if (string.IsNullOrEmpty(setting)) return IsPngFile(state);
            if (setting == "0") return false;
            if (setting.Equals("false", StringComparison.InvariantCultureIgnoreCase)) return false;

            return true;
        }

        protected virtual bool IsPngFile(ImageState state)
        {
            if (state.Job == null) return false;

            var extension = state.Job.ResultFileExtension ?? string.Empty;

            return extension.Equals("png", StringComparison.InvariantCultureIgnoreCase);
        }

        protected virtual bool DetermineDebug(ImageState state)
        {
            var setting = state.settings["optimizePngDebug"];

            if (string.IsNullOrEmpty(setting)) return false;
            if (setting == "0") return false;
            if (setting.Equals("false", StringComparison.InvariantCultureIgnoreCase)) return false;

            return true;
        }
    }
}