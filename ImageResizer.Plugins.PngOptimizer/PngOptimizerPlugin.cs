using System;
using System.Collections.Generic;
using System.Drawing;
using ImageResizer.Configuration;
using ImageResizer.Plugins.PngOptimizer.Quantization;
using ImageResizer.Resizing;

namespace ImageResizer.Plugins.PngOptimizer
{
    public class PngOptimizerPlugin : BuilderExtension, IPlugin, IQuerystringPlugin
    {
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

            var quantizer = new DitheredLuminanceQuantizer(state.destBitmap.Width, state.destBitmap.Height, 210, DetermineDebug(state));

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
            //todo: Determine if image is a png, if not, skip.

            if (state.destBitmap == null) return false;
            if (state.settings == null) return false;

            var setting = state.settings["optimized"];

            if (string.IsNullOrEmpty(setting)) return false;
            if (setting == "0") return false;
            if (setting.Equals("false", StringComparison.InvariantCultureIgnoreCase)) return false;

            return true;
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