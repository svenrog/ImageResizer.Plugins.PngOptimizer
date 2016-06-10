ImageResizer.Plugins.PngOptimizer
====================================
PNG Optimizer for ImageResizer.NET

![bike example](https://raw.githubusercontent.com/Svenrog/ImageResizer.Plugins.PngOptimizer/master/content/demo.png)

nQuant based optimizer for reducing png file sizes by palette quantization. 
Uses a Bayer matrix ordered dithering on alpha channel for smoother shadows.

Error diffusion dithering can sometimes give better results, but give noisy results during animation.

### Parameters

* **optimize** - values '1' or 'true', specifies if plugin should be used.
