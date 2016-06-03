using System;
using System.Collections.Generic;
using System.Drawing;
using ImageResizer.Configuration;
using ImageResizer.Resizing;
using nQuant;

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
                "optimized"
            };
        }


        protected override RequestedAction PostRenderImage(ImageState state)
        {
            var enabled = DetermineEnabled(state);
            if (!enabled)
                return RequestedAction.None;

            // todo: Implement optimized quantizer
            // WuQuantizer leads to unwanted effects
            // Color vibrancy loss in saturated colors
            // Smooth transparent edges are jarry

            var quantizer = new WuQuantizer();

            try
            {
                state.destBitmap = (Bitmap) quantizer.QuantizeImage(state.destBitmap, 3, 3);
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
    }
}