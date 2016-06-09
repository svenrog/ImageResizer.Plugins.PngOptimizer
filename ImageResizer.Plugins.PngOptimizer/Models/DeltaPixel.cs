namespace ImageResizer.Plugins.PngOptimizer.Models
{
    public struct DeltaPixel
    {
        public int Alpha;
        public int Red;
        public int Green;
        public int Blue;

        public DeltaPixel(int alpha, int red, int green, int blue)
        {
            Alpha = alpha;
            Red = red;
            Green = green;
            Blue = blue;
        }
    }
}