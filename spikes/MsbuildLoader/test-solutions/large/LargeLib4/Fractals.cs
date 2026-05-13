using System;
using System.Collections.Generic;
using System.Linq;
using LargeLib1;
using LargeLib2;
using LargeLib3;

namespace LargeLib4;

public static class Fractals
{
    public static double[][] CreateFractalTree(double trunkLength, double branchAngle, int depth, int seed = 0)
    {
        var rng = new Random(seed);
        var points = new List<double[]> { new[] { 0.0, 0.0, 0.0 }, new[] { 0.0, trunkLength, 0.0 } };
        void Branch(double[] start, double[] direction, double length, int currentDepth)
        {
            if (currentDepth <= 0) return;
            double[] end = start.Select((x, i) => x + direction[i] * length).ToArray();
            points.Add(end);
            double[] leftDir = Geometry.Rotate3DZ(direction[0], direction[1], direction[2], branchAngle);
            double[] rightDir = Geometry.Rotate3DZ(direction[0], direction[1], direction[2], -branchAngle);
            Branch(end, leftDir, length * 0.7, currentDepth - 1);
            Branch(end, rightDir, length * 0.7, currentDepth - 1);
        }
        Branch(points[1], [0, 1, 0], trunkLength * 0.7, depth);
        return points.ToArray();
    }
    public static double[][] CreateKochSnowflake(double size, int iterations)
    {
        var points = new List<double[]>
        {
            new[] { 0.0, size, 0.0 },
            new[] { size * MathCore.Sin(Math.PI / 3), -size * MathCore.Cos(Math.PI / 3), 0.0 },
            new[] { -size * MathCore.Sin(Math.PI / 3), -size * MathCore.Cos(Math.PI / 3), 0.0 }
        };
        for (int i = 0; i < iterations; i++)
        {
            var newPoints = new List<double[]>();
            for (int j = 0; j < points.Count; j++)
            {
                int next = (j + 1) % points.Count;
                double[] a = points[j];
                double[] b = points[next];
                double[] c = a.Select((x, k) => x + (b[k] - x) / 3).ToArray();
                double[] d = a.Select((x, k) => x + 2 * (b[k] - x) / 3).ToArray();
                double[] e = [
                    (c[0] + d[0]) / 2 - (d[1] - c[1]) * MathCore.Sqrt(3) / 2,
                    (c[1] + d[1]) / 2 + (d[0] - c[0]) * MathCore.Sqrt(3) / 2,
                    0
                ];
                newPoints.Add(a);
                newPoints.Add(c);
                newPoints.Add(e);
                newPoints.Add(d);
            }
            points = newPoints;
        }
        return points.ToArray();
    }
    public static double[][] CreateSierpinskiTriangle(double size, int iterations)
    {
        var points = new List<double[]>
        {
            new[] { 0.0, size, 0.0 },
            new[] { size * MathCore.Sin(Math.PI / 3), -size * MathCore.Cos(Math.PI / 3), 0.0 },
            new[] { -size * MathCore.Sin(Math.PI / 3), -size * MathCore.Cos(Math.PI / 3), 0.0 }
        };
        for (int i = 0; i < iterations; i++)
        {
            var newPoints = new List<double[]>();
            for (int j = 0; j < points.Count; j += 3)
            {
                double[] a = points[j];
                double[] b = points[j + 1];
                double[] c = points[j + 2];
                double[] ab = a.Select((x, k) => (x + b[k]) / 2).ToArray();
                double[] bc = b.Select((x, k) => (x + c[k]) / 2).ToArray();
                double[] ca = c.Select((x, k) => (x + a[k]) / 2).ToArray();
                newPoints.Add(a);
                newPoints.Add(ab);
                newPoints.Add(ca);
                newPoints.Add(ab);
                newPoints.Add(b);
                newPoints.Add(bc);
                newPoints.Add(ca);
                newPoints.Add(bc);
                newPoints.Add(c);
            }
            points = newPoints;
        }
        return points.ToArray();
    }
    public static double[][] CreateDragonCurve(int iterations)
    {
        var sequence = new List<bool> { true };
        for (int i = 0; i < iterations; i++)
        {
            var next = new List<bool>(sequence);
            next.Add(true);
            next.AddRange(sequence.AsEnumerable().Reverse().Select(x => !x));
            sequence = next;
        }
        var points = new List<double[]> { new[] { 0.0, 0.0, 0.0 } };
        double angle = 0;
        foreach (var turn in sequence)
        {
            angle += turn ? Math.PI / 2 : -Math.PI / 2;
            double[] last = points[^1];
            points.Add([last[0] + MathCore.Cos(angle), last[1] + MathCore.Sin(angle), 0]);
        }
        return points.ToArray();
    }
    public static double[][] CreateBarnsleyFern(int iterations)
    {
        var points = new List<double[]>();
        double x = 0, y = 0;
        var rng = new Random(42);
        for (int i = 0; i < iterations; i++)
        {
            double r = rng.NextDouble();
            double nx, ny;
            if (r < 0.01) { nx = 0; ny = 0.16 * y; }
            else if (r < 0.86) { nx = 0.85 * x + 0.04 * y; ny = -0.04 * x + 0.85 * y + 1.6; }
            else if (r < 0.93) { nx = 0.2 * x - 0.26 * y; ny = 0.23 * x + 0.22 * y + 1.6; }
            else { nx = -0.15 * x + 0.28 * y; ny = 0.26 * x + 0.24 * y + 0.44; }
            x = nx; y = ny;
            points.Add([x, y, 0]);
        }
        return points.ToArray();
    }
    public static double[][] CreateMandelbrotSet(int width, int height, double xMin, double xMax, double yMin, double yMax, int maxIter)
    {
        var points = new List<double[]>();
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                double x0 = xMin + px * (xMax - xMin) / width;
                double y0 = yMin + py * (yMax - yMin) / height;
                double x = 0, y = 0;
                int iter = 0;
                while (x * x + y * y <= 4 && iter < maxIter)
                {
                    double xtemp = x * x - y * y + x0;
                    y = 2 * x * y + y0;
                    x = xtemp;
                    iter++;
                }
                if (iter == maxIter) points.Add([x0, y0, 0]);
            }
        }
        return points.ToArray();
    }
    public static double[][] CreateJuliaSet(int width, int height, double cx, double cy, double xMin, double xMax, double yMin, double yMax, int maxIter)
    {
        var points = new List<double[]>();
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                double x = xMin + px * (xMax - xMin) / width;
                double y = yMin + py * (yMax - yMin) / height;
                int iter = 0;
                while (x * x + y * y <= 4 && iter < maxIter)
                {
                    double xtemp = x * x - y * y + cx;
                    y = 2 * x * y + cy;
                    x = xtemp;
                    iter++;
                }
                if (iter == maxIter) points.Add([x, y, 0]);
            }
        }
        return points.ToArray();
    }
    public static double[][] CreateNewtonFractal(int width, int height, double xMin, double xMax, double yMin, double yMax, int maxIter)
    {
        var points = new List<double[]>();
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                double x = xMin + px * (xMax - xMin) / width;
                double y = yMin + py * (yMax - yMin) / height;
                for (int i = 0; i < maxIter; i++)
                {
                    double denom = 3 * (x * x + y * y) * (x * x + y * y);
                    if (denom == 0) break;
                    double nx = (2 * x * x * x + 2 * x * y * y + 1) / denom;
                    double ny = (2 * y * y * y + 2 * x * x * y) / denom;
                    x = nx; y = ny;
                }
                points.Add([x, y, 0]);
            }
        }
        return points.ToArray();
    }
    public static double[][] CreateBurningShipFractal(int width, int height, double xMin, double xMax, double yMin, double yMax, int maxIter)
    {
        var points = new List<double[]>();
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                double x0 = xMin + px * (xMax - xMin) / width;
                double y0 = yMin + py * (yMax - yMin) / height;
                double x = 0, y = 0;
                int iter = 0;
                while (x * x + y * y <= 4 && iter < maxIter)
                {
                    double xtemp = x * x - y * y + x0;
                    y = Math.Abs(2 * x * y) + y0;
                    x = Math.Abs(xtemp);
                    iter++;
                }
                if (iter == maxIter) points.Add([x0, y0, 0]);
            }
        }
        return points.ToArray();
    }
    public static double[][] CreateTricornFractal(int width, int height, double xMin, double xMax, double yMin, double yMax, int maxIter)
    {
        var points = new List<double[]>();
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                double x0 = xMin + px * (xMax - xMin) / width;
                double y0 = yMin + py * (yMax - yMin) / height;
                double x = 0, y = 0;
                int iter = 0;
                while (x * x + y * y <= 4 && iter < maxIter)
                {
                    double xtemp = x * x - y * y + x0;
                    y = -2 * x * y + y0;
                    x = xtemp;
                    iter++;
                }
                if (iter == maxIter) points.Add([x0, y0, 0]);
            }
        }
        return points.ToArray();
    }
    public static double[][] CreateLSystem(string axiom, Dictionary<char, string> rules, int iterations, double angle, double step)
    {
        string current = axiom;
        for (int i = 0; i < iterations; i++)
        {
            current = string.Concat(current.Select(c => rules.TryGetValue(c, out var r) ? r : c.ToString()));
        }
        var points = new List<double[]> { new[] { 0.0, 0.0, 0.0 } };
        double direction = 0;
        foreach (char c in current)
        {
            switch (c)
            {
                case 'F':
                    double[] last = points[^1];
                    points.Add([last[0] + step * MathCore.Cos(direction), last[1] + step * MathCore.Sin(direction), 0]);
                    break;
                case '+':
                    direction += angle;
                    break;
                case '-':
                    direction -= angle;
                    break;
            }
        }
        return points.ToArray();
    }
}
