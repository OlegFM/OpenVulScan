using System;
using System.Linq;
using LargeLib1;

namespace LargeLib3;

public static class ColorUtils
{
    public static double[] RgbToHsv(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double h = 0;
        if (delta != 0)
        {
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * (((b - r) / delta) + 2);
            else h = 60 * (((r - g) / delta) + 4);
        }
        double s = max == 0 ? 0 : delta / max;
        return [h, s, max];
    }
    public static double[] HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;
        double[] rgb = h < 60 ? [c, x, 0] :
                       h < 120 ? [x, c, 0] :
                       h < 180 ? [0, c, x] :
                       h < 240 ? [0, x, c] :
                       h < 300 ? [x, 0, c] :
                       [c, 0, x];
        return rgb.Select(v => v + m).ToArray();
    }
    public static double[] RgbToCmyk(double r, double g, double b)
    {
        double k = 1 - Math.Max(r, Math.Max(g, b));
        double c = (1 - r - k) / (1 - k);
        double m = (1 - g - k) / (1 - k);
        double y = (1 - b - k) / (1 - k);
        return [c, m, y, k];
    }
    public static double[] CmykToRgb(double c, double m, double y, double k)
    {
        return [1 - c * (1 - k) - k, 1 - m * (1 - k) - k, 1 - y * (1 - k) - k];
    }
    public static double[] RgbToXyz(double r, double g, double b)
    {
        return [0.4124564 * r + 0.3575761 * g + 0.1804375 * b,
                0.2126729 * r + 0.7151522 * g + 0.0721750 * b,
                0.0193339 * r + 0.1191920 * g + 0.9503041 * b];
    }
    public static double[] XyzToLab(double x, double y, double z)
    {
        double fx = x > 0.008856 ? Math.Pow(x, 1.0 / 3) : 7.787 * x + 16.0 / 116;
        double fy = y > 0.008856 ? Math.Pow(y, 1.0 / 3) : 7.787 * y + 16.0 / 116;
        double fz = z > 0.008856 ? Math.Pow(z, 1.0 / 3) : 7.787 * z + 16.0 / 116;
        return [116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)];
    }
    public static double[] RgbToLab(double r, double g, double b)
    {
        var xyz = RgbToXyz(r, g, b);
        return XyzToLab(xyz[0] / 0.95047, xyz[1], xyz[2] / 1.08883);
    }
    public static double ColorDistance(double[] lab1, double[] lab2)
    {
        return MathCore.Distance3D(lab1[0], lab1[1], lab1[2], lab2[0], lab2[1], lab2[2]);
    }
    public static double[] MixColors(double[] c1, double[] c2, double t)
    {
        return c1.Select((x, i) => x + (c2[i] - x) * t).ToArray();
    }
    public static double[] InvertColor(double[] c)
    {
        return c.Select(x => 1 - x).ToArray();
    }
    public static double[] Grayscale(double r, double g, double b)
    {
        return [0.299 * r + 0.587 * g + 0.114 * b];
    }
    public static double[] Sepia(double r, double g, double b)
    {
        return [0.393 * r + 0.769 * g + 0.189 * b,
                0.349 * r + 0.686 * g + 0.168 * b,
                0.272 * r + 0.534 * g + 0.131 * b];
    }
    public static double[] TemperatureToRgb(double kelvin)
    {
        double temp = kelvin / 100;
        double r = temp <= 66 ? 255 : 329.698727446 * Math.Pow(temp - 60, -0.1332047592);
        double g = temp <= 66 ? 99.4708025861 * Math.Log(temp) - 161.1195681661 : 288.1221695283 * Math.Pow(temp - 60, -0.0755148492);
        double b = temp >= 66 ? 255 : temp <= 19 ? 0 : 138.5177312231 * Math.Log(temp - 10) - 305.0447927307;
        return [MathCore.Clamp(r / 255, 0, 1), MathCore.Clamp(g / 255, 0, 1), MathCore.Clamp(b / 255, 0, 1)];
    }
    public static double[] SrgbToLinear(double[] srgb)
    {
        return srgb.Select(x => x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4)).ToArray();
    }
    public static double[] LinearToSrgb(double[] linear)
    {
        return linear.Select(x => x <= 0.0031308 ? x * 12.92 : 1.055 * Math.Pow(x, 1 / 2.4) - 0.055).ToArray();
    }
    public static double[] GammaCorrect(double[] color, double gamma)
    {
        return color.Select(x => Math.Pow(x, 1 / gamma)).ToArray();
    }
    public static double[] Exposure(double[] color, double exposure)
    {
        return color.Select(x => 1 - Math.Exp(-x * exposure)).ToArray();
    }
    public static double[] ToneMapReinhard(double[] color)
    {
        return color.Select(x => x / (1 + x)).ToArray();
    }
    public static double[] ToneMapACES(double[] color)
    {
        double a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
        return color.Select(x => MathCore.Clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0, 1)).ToArray();
    }
    public static double[] Vibrance(double[] color, double vibrance)
    {
        double max = color.Max();
        double avg = color.Average();
        double amount = (max - avg) * 2 * vibrance;
        return color.Select(x => MathCore.Clamp(x + amount, 0, 1)).ToArray();
    }
    public static double[] Saturation(double[] color, double saturation)
    {
        double gray = Grayscale(color[0], color[1], color[2])[0];
        return color.Select(x => MathCore.Clamp(gray + (x - gray) * saturation, 0, 1)).ToArray();
    }
    public static double[] Contrast(double[] color, double contrast)
    {
        double factor = (259 * (contrast * 255 + 255)) / (255 * (259 - contrast * 255));
        return color.Select(x => MathCore.Clamp(factor * (x - 0.5) + 0.5, 0, 1)).ToArray();
    }
    public static double[] Brightness(double[] color, double brightness)
    {
        return color.Select(x => MathCore.Clamp(x + brightness, 0, 1)).ToArray();
    }
    public static double[] HueShift(double[] color, double shift)
    {
        var hsv = RgbToHsv(color[0], color[1], color[2]);
        hsv[0] = (hsv[0] + shift) % 360;
        if (hsv[0] < 0) hsv[0] += 360;
        return HsvToRgb(hsv[0], hsv[1], hsv[2]);
    }
    public static double[] ColorBalance(double[] color, double shadows, double midtones, double highlights)
    {
        return color.Select(x => x < 0.5 ? x + shadows * (0.5 - x) : x + highlights * (x - 0.5)).ToArray();
    }
    public static double[] Levels(double[] color, double inputMin, double inputMax, double outputMin, double outputMax)
    {
        return color.Select(x => MathCore.Clamp((x - inputMin) / (inputMax - inputMin), 0, 1) * (outputMax - outputMin) + outputMin).ToArray();
    }
    public static double[] Posterize(double[] color, int levels)
    {
        return color.Select(x => Math.Floor(x * levels) / levels).ToArray();
    }
    public static double[] Threshold(double[] color, double threshold)
    {
        return color.Select(x => x >= threshold ? 1.0 : 0.0).ToArray();
    }
    public static double[] Solarize(double[] color, double threshold)
    {
        return color.Select(x => x > threshold ? 1 - x : x).ToArray();
    }
    public static double[] Bloom(double[] color, double intensity)
    {
        return color.Select(x => MathCore.Clamp(x * (1 + intensity), 0, 1)).ToArray();
    }
    public static double[] FilmGrain(double[] color, double intensity, int seed)
    {
        var rng = new Random(seed);
        return color.Select(x => MathCore.Clamp(x + (rng.NextDouble() - 0.5) * intensity, 0, 1)).ToArray();
    }
}
