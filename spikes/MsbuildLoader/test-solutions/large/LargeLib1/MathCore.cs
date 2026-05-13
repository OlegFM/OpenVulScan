using System;
using System.Collections.Generic;
using System.Linq;

namespace LargeLib1;

public static class MathCore
{
    public static double Sqrt(double x) => Math.Sqrt(x);
    public static double Pow(double x, double y) => Math.Pow(x, y);
    public static double Abs(double x) => Math.Abs(x);
    public static double Log(double x) => Math.Log(x);
    public static double Exp(double x) => Math.Exp(x);
    public static double Sin(double x) => Math.Sin(x);
    public static double Cos(double x) => Math.Cos(x);
    public static double Tan(double x) => Math.Tan(x);
    public static double Asin(double x) => Math.Asin(x);
    public static double Acos(double x) => Math.Acos(x);
    public static double Atan(double x) => Math.Atan(x);
    public static double Ceiling(double x) => Math.Ceiling(x);
    public static double Floor(double x) => Math.Floor(x);
    public static double Round(double x, int digits) => Math.Round(x, digits);
    public static int Sign(double x) => Math.Sign(x);
    public static double Max(double a, double b) => Math.Max(a, b);
    public static double Min(double a, double b) => Math.Min(a, b);
    public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
    public static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
    public static double Hypotenuse(double a, double b) => Sqrt(a * a + b * b);
    public static double Distance2D(double x1, double y1, double x2, double y2) => Hypotenuse(x2 - x1, y2 - y1);
    public static double Distance3D(double x1, double y1, double z1, double x2, double y2, double z2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double dz = z2 - z1;
        return Sqrt(dx * dx + dy * dy + dz * dz);
    }
    public static bool IsApproximatelyEqual(double a, double b, double epsilon = 1e-10) => Abs(a - b) < epsilon;
    public static double MapRange(double value, double fromMin, double fromMax, double toMin, double toMax)
    {
        double normalized = (value - fromMin) / (fromMax - fromMin);
        return toMin + normalized * (toMax - toMin);
    }
    public static double[] SolveQuadratic(double a, double b, double c)
    {
        double discriminant = b * b - 4 * a * c;
        if (discriminant < 0) return [];
        double sqrtD = Sqrt(discriminant);
        double x1 = (-b + sqrtD) / (2 * a);
        double x2 = (-b - sqrtD) / (2 * a);
        return [x1, x2];
    }
    public static double[] GenerateSineWave(double frequency, double amplitude, int samples, double sampleRate)
    {
        var wave = new double[samples];
        for (int i = 0; i < samples; i++)
        {
            double t = i / sampleRate;
            wave[i] = amplitude * Sin(2 * Math.PI * frequency * t);
        }
        return wave;
    }
    public static double[] MovingAverage(double[] data, int windowSize)
    {
        var result = new double[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            int start = Math.Max(0, i - windowSize + 1);
            int count = i - start + 1;
            double sum = 0;
            for (int j = start; j <= i; j++) sum += data[j];
            result[i] = sum / count;
        }
        return result;
    }
    public static double StandardDeviation(double[] data)
    {
        double avg = data.Average();
        double sumSq = data.Sum(x => (x - avg) * (x - avg));
        return Sqrt(sumSq / data.Length);
    }
    public static double Variance(double[] data)
    {
        double avg = data.Average();
        return data.Sum(x => (x - avg) * (x - avg)) / data.Length;
    }
    public static double Median(double[] data)
    {
        var sorted = data.OrderBy(x => x).ToArray();
        int n = sorted.Length;
        if (n % 2 == 1) return sorted[n / 2];
        return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
    public static double[] Normalize(double[] data)
    {
        double min = data.Min();
        double max = data.Max();
        double range = max - min;
        if (range == 0) return data.Select(_ => 0.0).ToArray();
        return data.Select(x => (x - min) / range).ToArray();
    }
    public static int[] Histogram(double[] data, int bins)
    {
        double min = data.Min();
        double max = data.Max();
        double binWidth = (max - min) / bins;
        var hist = new int[bins];
        foreach (var x in data)
        {
            int bin = Math.Min((int)((x - min) / binWidth), bins - 1);
            hist[bin]++;
        }
        return hist;
    }
    public static double Correlation(double[] x, double[] y)
    {
        double avgX = x.Average();
        double avgY = y.Average();
        double num = x.Zip(y, (a, b) => (a - avgX) * (b - avgY)).Sum();
        double denX = x.Sum(a => (a - avgX) * (a - avgX));
        double denY = y.Sum(b => (b - avgY) * (b - avgY));
        return num / Sqrt(denX * denY);
    }
    public static double[] LinearRegression(double[] x, double[] y)
    {
        double avgX = x.Average();
        double avgY = y.Average();
        double slope = x.Zip(y, (a, b) => (a - avgX) * (b - avgY)).Sum() / x.Sum(a => (a - avgX) * (a - avgX));
        double intercept = avgY - slope * avgX;
        return [slope, intercept];
    }
    public static double[] FastFourierTransform(double[] real)
    {
        int n = real.Length;
        var result = new double[n];
        for (int k = 0; k < n; k++)
        {
            double sumReal = 0;
            for (int t = 0; t < n; t++)
            {
                double angle = -2 * Math.PI * t * k / n;
                sumReal += real[t] * Cos(angle);
            }
            result[k] = sumReal;
        }
        return result;
    }
    public static double Entropy(double[] probabilities)
    {
        double sum = 0;
        foreach (var p in probabilities)
        {
            if (p > 0) sum -= p * Log(p);
        }
        return sum;
    }
    public static double SoftmaxMax(double[] logits)
    {
        double max = logits.Max();
        var expScores = logits.Select(x => Exp(x - max)).ToArray();
        double sum = expScores.Sum();
        return expScores.Max() / sum;
    }
    public static int ArgMax(double[] values)
    {
        int maxIndex = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > values[maxIndex]) maxIndex = i;
        }
        return maxIndex;
    }
    public static int ArgMin(double[] values)
    {
        int minIndex = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < values[minIndex]) minIndex = i;
        }
        return minIndex;
    }
    public static double[][] Transpose(double[][] matrix)
    {
        int rows = matrix.Length;
        int cols = matrix[0].Length;
        var result = new double[cols][];
        for (int j = 0; j < cols; j++)
        {
            result[j] = new double[rows];
            for (int i = 0; i < rows; i++) result[j][i] = matrix[i][j];
        }
        return result;
    }
    public static double[][] MatrixMultiply(double[][] a, double[][] b)
    {
        int rows = a.Length;
        int cols = b[0].Length;
        int inner = b.Length;
        var result = new double[rows][];
        for (int i = 0; i < rows; i++)
        {
            result[i] = new double[cols];
            for (int j = 0; j < cols; j++)
            {
                double sum = 0;
                for (int k = 0; k < inner; k++) sum += a[i][k] * b[k][j];
                result[i][j] = sum;
            }
        }
        return result;
    }
    public static double[][] IdentityMatrix(int size)
    {
        var result = new double[size][];
        for (int i = 0; i < size; i++)
        {
            result[i] = new double[size];
            result[i][i] = 1.0;
        }
        return result;
    }
    public static double Trace(double[][] matrix)
    {
        double sum = 0;
        for (int i = 0; i < matrix.Length; i++) sum += matrix[i][i];
        return sum;
    }
    public static double Determinant2x2(double[][] matrix)
    {
        return matrix[0][0] * matrix[1][1] - matrix[0][1] * matrix[1][0];
    }
    public static double[] SolveLinearSystem2x2(double[][] a, double[] b)
    {
        double det = Determinant2x2(a);
        double x = (b[0] * a[1][1] - b[1] * a[0][1]) / det;
        double y = (b[1] * a[0][0] - b[0] * a[1][0]) / det;
        return [x, y];
    }
    public static double[] Eigenvalues2x2(double[][] matrix)
    {
        double trace = matrix[0][0] + matrix[1][1];
        double det = Determinant2x2(matrix);
        double discriminant = trace * trace - 4 * det;
        if (discriminant < 0) return [];
        double sqrtD = Sqrt(discriminant);
        return [(trace + sqrtD) / 2, (trace - sqrtD) / 2];
    }
    public static double[] GradientDescent(Func<double[], double> f, Func<double[], double[]> grad, double[] initial, double learningRate, int iterations)
    {
        double[] x = (double[])initial.Clone();
        for (int i = 0; i < iterations; i++)
        {
            double[] g = grad(x);
            for (int j = 0; j < x.Length; j++) x[j] -= learningRate * g[j];
        }
        return x;
    }
    public static double MonteCarloPi(int samples)
    {
        var rng = new Random(42);
        int inside = 0;
        for (int i = 0; i < samples; i++)
        {
            double x = rng.NextDouble();
            double y = rng.NextDouble();
            if (x * x + y * y <= 1) inside++;
        }
        return 4.0 * inside / samples;
    }
    public static double IntegrateSimpson(Func<double, double> f, double a, double b, int n)
    {
        double h = (b - a) / n;
        double sum = f(a) + f(b);
        for (int i = 1; i < n; i += 2) sum += 4 * f(a + i * h);
        for (int i = 2; i < n; i += 2) sum += 2 * f(a + i * h);
        return sum * h / 3;
    }
    public static double IntegrateTrapezoidal(Func<double, double> f, double a, double b, int n)
    {
        double h = (b - a) / n;
        double sum = (f(a) + f(b)) / 2;
        for (int i = 1; i < n; i++) sum += f(a + i * h);
        return sum * h;
    }
    public static double[] RungeKutta4(Func<double, double[], double[]> f, double[] y0, double t0, double h, int steps)
    {
        double[] y = (double[])y0.Clone();
        double t = t0;
        for (int i = 0; i < steps; i++)
        {
            double[] k1 = f(t, y);
            double[] k2 = f(t + h / 2, y.Select((yi, j) => yi + h * k1[j] / 2).ToArray());
            double[] k3 = f(t + h / 2, y.Select((yi, j) => yi + h * k2[j] / 2).ToArray());
            double[] k4 = f(t + h, y.Select((yi, j) => yi + h * k3[j]).ToArray());
            for (int j = 0; j < y.Length; j++)
            {
                y[j] += h / 6 * (k1[j] + 2 * k2[j] + 2 * k3[j] + k4[j]);
            }
            t += h;
        }
        return y;
    }
    public static double BilinearInterpolation(double q11, double q12, double q21, double q22, double x1, double x2, double y1, double y2, double x, double y)
    {
        double x2x1 = x2 - x1;
        double y2y1 = y2 - y1;
        double x2x = x2 - x;
        double y2y = y2 - y;
        double xy1 = x - x1;
        double xy = y - y1;
        return 1.0 / (x2x1 * y2y1) * (q11 * x2x * y2y + q21 * xy1 * y2y + q12 * x2x * xy + q22 * xy1 * xy);
    }
    public static double[] Convolution1D(double[] signal, double[] kernel)
    {
        int n = signal.Length + kernel.Length - 1;
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int j = 0; j < kernel.Length; j++)
            {
                int k = i - j;
                if (k >= 0 && k < signal.Length) sum += signal[k] * kernel[j];
            }
            result[i] = sum;
        }
        return result;
    }
    public static double[] GaussianKernel(int size, double sigma)
    {
        var kernel = new double[size];
        double sum = 0;
        int half = size / 2;
        for (int i = 0; i < size; i++)
        {
            double x = i - half;
            kernel[i] = Exp(-(x * x) / (2 * sigma * sigma));
            sum += kernel[i];
        }
        return kernel.Select(k => k / sum).ToArray();
    }
    public static double[][] SobelKernelX() => [[-1, 0, 1], [-2, 0, 2], [-1, 0, 1]];
    public static double[][] SobelKernelY() => [[-1, -2, -1], [0, 0, 0], [1, 2, 1]];
    public static double[] BoxBlurKernel(int size)
    {
        double value = 1.0 / (size * size);
        return Enumerable.Repeat(value, size * size).ToArray();
    }
}

public static class Noise
{
    public static double PerlinNoise(double x, double y, int seed = 0)
    {
        var rng = new Random(seed);
        int xi = (int)Math.Floor(x);
        int yi = (int)Math.Floor(y);
        double xf = x - xi;
        double yf = y - yi;
        double u = xf * xf * (3 - 2 * xf);
        double v = yf * yf * (3 - 2 * yf);
        double n00 = rng.NextDouble();
        double n01 = rng.NextDouble();
        double n10 = rng.NextDouble();
        double n11 = rng.NextDouble();
        double x00 = n00 * xf + n00 * yf;
        double x10 = n10 * (xf - 1) + n10 * yf;
        double x01 = n01 * xf + n01 * (yf - 1);
        double x11 = n11 * (xf - 1) + n11 * (yf - 1);
        return MathCore.Lerp(MathCore.Lerp(x00, x10, u), MathCore.Lerp(x01, x11, u), v);
    }
    public static double SimplexNoise(double x, double y, int seed = 0)
    {
        return PerlinNoise(x * 0.5, y * 0.5, seed);
    }
    public static double WorleyNoise(double x, double y, int seed = 0)
    {
        var rng = new Random(seed);
        double minDist = double.MaxValue;
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                double px = Math.Floor(x) + i + rng.NextDouble();
                double py = Math.Floor(y) + j + rng.NextDouble();
                double dist = MathCore.Distance2D(x, y, px, py);
                if (dist < minDist) minDist = dist;
            }
        }
        return minDist;
    }
    public static double ValueNoise(double x, double y, int seed = 0)
    {
        var rng = new Random(seed);
        int xi = (int)Math.Floor(x);
        int yi = (int)Math.Floor(y);
        double xf = x - xi;
        double yf = y - yi;
        double n00 = rng.NextDouble();
        double n10 = rng.NextDouble();
        double n01 = rng.NextDouble();
        double n11 = rng.NextDouble();
        return MathCore.Lerp(MathCore.Lerp(n00, n10, xf), MathCore.Lerp(n01, n11, xf), yf);
    }
    public static double FractalBrownianMotion(double x, double y, int octaves, double persistence, int seed = 0)
    {
        double total = 0;
        double frequency = 1;
        double amplitude = 1;
        double maxValue = 0;
        for (int i = 0; i < octaves; i++)
        {
            total += PerlinNoise(x * frequency, y * frequency, seed + i) * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= 2;
        }
        return total / maxValue;
    }
    public static double MarblePattern(double x, double y, double scale, int seed = 0)
    {
        return Math.Sin(x * scale + FractalBrownianMotion(x, y, 4, 0.5, seed) * 5);
    }
    public static double WoodPattern(double x, double y, double scale, int seed = 0)
    {
        double dist = Math.Sqrt(x * x + y * y) * scale;
        return Math.Sin(dist + FractalBrownianMotion(x, y, 2, 0.5, seed) * 2);
    }
    public static double CloudPattern(double x, double y, int seed = 0)
    {
        return FractalBrownianMotion(x, y, 6, 0.5, seed);
    }
}
