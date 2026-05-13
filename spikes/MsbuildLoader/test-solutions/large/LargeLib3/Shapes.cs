using System;
using System.Collections.Generic;
using System.Linq;
using LargeLib1;
using LargeLib2;

namespace LargeLib3;

public static class Shapes
{
    public static double[][] SubdivideIcosahedron(double[][] vertices, int[][] faces)
    {
        var newVertices = new List<double[]>(vertices);
        var newFaces = new List<int[]>();
        var midPointCache = new Dictionary<(int, int), int>();
        int GetMidPoint(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (midPointCache.TryGetValue(key, out int idx)) return idx;
            double[] mid = MatrixOps.SubdivideBezier(vertices[a], vertices[b], vertices[b], vertices[a], 0.5)[3];
            newVertices.Add(Geometry.NormalizeVector(mid));
            midPointCache[key] = newVertices.Count - 1;
            return newVertices.Count - 1;
        }
        foreach (var face in faces)
        {
            int a = GetMidPoint(face[0], face[1]);
            int b = GetMidPoint(face[1], face[2]);
            int c = GetMidPoint(face[2], face[0]);
            newFaces.Add([face[0], a, c]);
            newFaces.Add([face[1], b, a]);
            newFaces.Add([face[2], c, b]);
            newFaces.Add([a, b, c]);
        }
        return newVertices.ToArray();
    }
    public static double[][] GenerateUVSphere(int latSegments, int lonSegments)
    {
        return MatrixOps.GenerateSphere(1, latSegments, lonSegments);
    }
    public static double[][] GenerateHeightmap(int width, int height, int seed = 0)
    {
        var map = new double[height][];
        for (int y = 0; y < height; y++)
        {
            map[y] = new double[width];
            for (int x = 0; x < width; x++)
            {
                map[y][x] = Noise.FractalBrownianMotion(x / 50.0, y / 50.0, 8, 0.5, seed);
            }
        }
        return map;
    }
    public static double[][] GenerateNormalMap(double[][] heightmap, double strength = 1.0)
    {
        int h = heightmap.Length;
        int w = heightmap[0].Length;
        var normals = new double[h][];
        for (int y = 0; y < h; y++)
        {
            normals[y] = new double[w];
            for (int x = 0; x < w; x++)
            {
                int xp1 = Math.Min(x + 1, w - 1);
                int yp1 = Math.Min(y + 1, h - 1);
                double sx = heightmap[y][xp1] - heightmap[y][x];
                double sy = heightmap[yp1][x] - heightmap[y][x];
                normals[y][x] = Math.Atan2(sy, sx) * strength;
            }
        }
        return normals;
    }
    public static double[] GenerateVoronoiDiagram(int width, int height, int points, int seed = 0)
    {
        var rng = new Random(seed);
        var sites = new (double x, double y)[points];
        for (int i = 0; i < points; i++)
        {
            sites[i] = (rng.NextDouble() * width, rng.NextDouble() * height);
        }
        var diagram = new double[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double minDist = double.MaxValue;
                for (int i = 0; i < points; i++)
                {
                    double dist = MathCore.Distance2D(x, y, sites[i].x, sites[i].y);
                    if (dist < minDist) minDist = dist;
                }
                diagram[y * width + x] = minDist;
            }
        }
        return diagram;
    }
    public static double[] PoissonDiskSampling(double width, double height, double minDist, int maxAttempts = 30, int seed = 0)
    {
        var rng = new Random(seed);
        var points = new List<double>();
        var active = new List<(double x, double y)>();
        double firstX = rng.NextDouble() * width;
        double firstY = rng.NextDouble() * height;
        active.Add((firstX, firstY));
        points.Add(firstX);
        points.Add(firstY);
        while (active.Count > 0)
        {
            int idx = rng.Next(active.Count);
            var (ax, ay) = active[idx];
            bool found = false;
            for (int i = 0; i < maxAttempts; i++)
            {
                double angle = rng.NextDouble() * 2 * Math.PI;
                double radius = minDist * (1 + rng.NextDouble());
                double nx = ax + radius * Math.Cos(angle);
                double ny = ay + radius * Math.Sin(angle);
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                bool valid = true;
                for (int j = 0; j < points.Count; j += 2)
                {
                    double dist = MathCore.Distance2D(nx, ny, points[j], points[j + 1]);
                    if (dist < minDist) { valid = false; break; }
                }
                if (valid)
                {
                    active.Add((nx, ny));
                    points.Add(nx);
                    points.Add(ny);
                    found = true;
                    break;
                }
            }
            if (!found) active.RemoveAt(idx);
        }
        return points.ToArray();
    }
    public static double[] HaltonSequence(int index, int baseValue)
    {
        double result = 0;
        double f = 1.0 / baseValue;
        int i = index;
        while (i > 0)
        {
            result += f * (i % baseValue);
            i /= baseValue;
            f /= baseValue;
        }
        return [result];
    }
    public static double[] HammersleySequence(int index, int n, int baseValue)
    {
        return [(double)index / n, HaltonSequence(index, baseValue)[0]];
    }
    public static double[] FibonacciSphere(int samples, int index)
    {
        double phi = Math.PI * (3 - Math.Sqrt(5));
        double y = 1 - (index / (double)(samples - 1)) * 2;
        double radius = Math.Sqrt(1 - y * y);
        double theta = phi * index;
        double x = Math.Cos(theta) * radius;
        double z = Math.Sin(theta) * radius;
        return [x, y, z];
    }
    public static double SphericalDistance(double lat1, double lon1, double lat2, double lon2, double radius)
    {
        double dLat = MathCore.DegreesToRadians(lat2 - lat1);
        double dLon = MathCore.DegreesToRadians(lon2 - lon1);
        double a = MathCore.Sin(dLat / 2) * MathCore.Sin(dLat / 2) + MathCore.Cos(MathCore.DegreesToRadians(lat1)) * MathCore.Cos(MathCore.DegreesToRadians(lat2)) * MathCore.Sin(dLon / 2) * MathCore.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return radius * c;
    }
    public static double[] CartesianToSpherical(double x, double y, double z)
    {
        double r = MathCore.Distance3D(0, 0, 0, x, y, z);
        double theta = Math.Acos(z / r);
        double phi = Math.Atan2(y, x);
        return [r, theta, phi];
    }
    public static double[] SphericalToCartesian(double r, double theta, double phi)
    {
        double x = r * MathCore.Sin(theta) * MathCore.Cos(phi);
        double y = r * MathCore.Sin(theta) * MathCore.Sin(phi);
        double z = r * MathCore.Cos(theta);
        return [x, y, z];
    }
    public static double[][] ExtrudePolygon(double[][] polygon, double height)
    {
        var top = polygon.Select(p => p.Select((x, i) => i == 1 ? x + height : x).ToArray()).ToArray();
        var result = new List<double[]>();
        result.AddRange(polygon);
        result.AddRange(top);
        return result.ToArray();
    }
    public static double[][] RevolveProfile(double[][] profile, int segments)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= segments; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            foreach (var p in profile)
            {
                points.Add([p[0] * MathCore.Cos(angle), p[1], p[0] * MathCore.Sin(angle)]);
            }
        }
        return points.ToArray();
    }
    public static double[][] SweepPath(double[][] shape, double[][] path)
    {
        var points = new List<double[]>();
        for (int i = 0; i < path.Length - 1; i++)
        {
            double[] dir = path[i + 1].Select((x, j) => x - path[i][j]).ToArray();
            double[] normal = Geometry.NormalizeVector(dir);
            foreach (var s in shape)
            {
                points.Add(s.Select((x, j) => x + path[i][j] + normal[j] * x).ToArray());
            }
        }
        return points.ToArray();
    }
    public static double[][] LoftShapes(double[][][] shapes, int segments)
    {
        var points = new List<double[]>();
        for (int i = 0; i < shapes.Length; i++)
            points.AddRange(shapes[i]);
        return points.ToArray();
    }
}
