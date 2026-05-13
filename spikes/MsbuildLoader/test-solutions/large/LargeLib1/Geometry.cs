using System;
using System.Collections.Generic;
using System.Linq;

namespace LargeLib1;

public static class Geometry
{
    public static double[] CrossProduct(double[] a, double[] b)
    {
        return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    }
    public static double DotProduct(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }
    public static double VectorLength(double[] v) => MathCore.Sqrt(v.Sum(x => x * x));
    public static double[] NormalizeVector(double[] v)
    {
        double len = VectorLength(v);
        return len == 0 ? v.Select(_ => 0.0).ToArray() : v.Select(x => x / len).ToArray();
    }
    public static double[] ReflectVector(double[] incident, double[] normal)
    {
        double dot = DotProduct(incident, normal);
        return incident.Select((x, i) => x - 2 * dot * normal[i]).ToArray();
    }
    public static double[] RefractVector(double[] incident, double[] normal, double eta)
    {
        double dot = DotProduct(incident, normal);
        double k = 1 - eta * eta * (1 - dot * dot);
        if (k < 0) return [0, 0, 0];
        return incident.Select((x, i) => eta * x - (eta * dot + Math.Sqrt(k)) * normal[i]).ToArray();
    }
    public static double[] LerpVector(double[] a, double[] b, double t)
    {
        return a.Select((x, i) => x + t * (b[i] - x)).ToArray();
    }
    public static double[] Rotate2D(double x, double y, double angle)
    {
        double cos = MathCore.Cos(angle);
        double sin = MathCore.Sin(angle);
        return [x * cos - y * sin, x * sin + y * cos];
    }
    public static double[] Rotate3DX(double x, double y, double z, double angle)
    {
        double cos = MathCore.Cos(angle);
        double sin = MathCore.Sin(angle);
        return [x, y * cos - z * sin, y * sin + z * cos];
    }
    public static double[] Rotate3DY(double x, double y, double z, double angle)
    {
        double cos = MathCore.Cos(angle);
        double sin = MathCore.Sin(angle);
        return [x * cos + z * sin, y, -x * sin + z * cos];
    }
    public static double[] Rotate3DZ(double x, double y, double z, double angle)
    {
        double cos = MathCore.Cos(angle);
        double sin = MathCore.Sin(angle);
        return [x * cos - y * sin, x * sin + y * cos, z];
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
    public static double[] BarycentricCoordinates(double[] p, double[] a, double[] b, double[] c)
    {
        double[] v0 = b.Select((x, i) => x - a[i]).ToArray();
        double[] v1 = c.Select((x, i) => x - a[i]).ToArray();
        double[] v2 = p.Select((x, i) => x - a[i]).ToArray();
        double d00 = DotProduct(v0, v0);
        double d01 = DotProduct(v0, v1);
        double d11 = DotProduct(v1, v1);
        double d20 = DotProduct(v2, v0);
        double d21 = DotProduct(v2, v1);
        double denom = d00 * d11 - d01 * d01;
        double v = (d11 * d20 - d01 * d21) / denom;
        double w = (d00 * d21 - d01 * d20) / denom;
        double u = 1 - v - w;
        return [u, v, w];
    }
    public static double[] TriangleNormal(double[] v0, double[] v1, double[] v2)
    {
        double[] edge1 = v1.Select((x, i) => x - v0[i]).ToArray();
        double[] edge2 = v2.Select((x, i) => x - v0[i]).ToArray();
        return NormalizeVector(CrossProduct(edge1, edge2));
    }
    public static double TriangleArea(double[] v0, double[] v1, double[] v2)
    {
        double[] edge1 = v1.Select((x, i) => x - v0[i]).ToArray();
        double[] edge2 = v2.Select((x, i) => x - v0[i]).ToArray();
        return VectorLength(CrossProduct(edge1, edge2)) * 0.5;
    }
    public static double[] TriangleCentroid(double[] v0, double[] v1, double[] v2)
    {
        return v0.Select((x, i) => (x + v1[i] + v2[i]) / 3).ToArray();
    }
    public static bool PointInTriangle(double[] p, double[] a, double[] b, double[] c)
    {
        double d1 = Cross2D(a, b, p);
        double d2 = Cross2D(b, c, p);
        double d3 = Cross2D(c, a, p);
        return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
    }
    public static double Cross2D(double[] o, double[] a, double[] b)
    {
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
    }
    public static double[][] BoundingBox(double[][] vertices)
    {
        int dim = vertices[0].Length;
        var min = new double[dim];
        var max = new double[dim];
        for (int d = 0; d < dim; d++)
        {
            min[d] = vertices.Min(v => v[d]);
            max[d] = vertices.Max(v => v[d]);
        }
        return [min, max];
    }
    public static double[] BoundingSphereCenter(double[][] vertices)
    {
        var box = BoundingBox(vertices);
        return box[0].Select((x, i) => (x + box[1][i]) * 0.5).ToArray();
    }
    public static double BoundingSphereRadius(double[][] vertices)
    {
        var center = BoundingSphereCenter(vertices);
        return vertices.Max(v => MathCore.Distance3D(v[0], v[1], v[2], center[0], center[1], center[2]));
    }
    public static double[] ClosestPointOnLine(double[] p, double[] a, double[] b)
    {
        double[] ab = b.Select((x, i) => x - a[i]).ToArray();
        double[] ap = p.Select((x, i) => x - a[i]).ToArray();
        double t = DotProduct(ap, ab) / DotProduct(ab, ab);
        t = MathCore.Clamp(t, 0, 1);
        return a.Select((x, i) => x + t * ab[i]).ToArray();
    }
    public static double[] ClosestPointOnTriangle(double[] p, double[] a, double[] b, double[] c)
    {
        double[] ab = b.Select((x, i) => x - a[i]).ToArray();
        double[] ac = c.Select((x, i) => x - a[i]).ToArray();
        double[] ap = p.Select((x, i) => x - a[i]).ToArray();
        double d1 = DotProduct(ab, ap);
        double d2 = DotProduct(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        double[] bp = p.Select((x, i) => x - b[i]).ToArray();
        double d3 = DotProduct(ab, bp);
        double d4 = DotProduct(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            double v = d1 / (d1 - d3);
            return a.Select((x, i) => x + v * ab[i]).ToArray();
        }
        double[] cp = p.Select((x, i) => x - c[i]).ToArray();
        double d5 = DotProduct(ab, cp);
        double d6 = DotProduct(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            double w = d2 / (d2 - d6);
            return a.Select((x, i) => x + w * ac[i]).ToArray();
        }
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
        {
            double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b.Select((x, i) => x + w * (c[i] - b[i])).ToArray();
        }
        double denom = 1.0 / (va + vb + vc);
        double v2 = vb * denom;
        double w2 = vc * denom;
        return a.Select((x, i) => x + ab[i] * v2 + ac[i] * w2).ToArray();
    }
    public static double DistancePointToPlane(double[] p, double[] normal, double d)
    {
        return Math.Abs(DotProduct(p, normal) + d) / VectorLength(normal);
    }
    public static double DistancePointToLine(double[] p, double[] a, double[] b)
    {
        double[] closest = ClosestPointOnLine(p, a, b);
        return MathCore.Distance3D(p[0], p[1], p[2], closest[0], closest[1], closest[2]);
    }
    public static double DistancePointToTriangle(double[] p, double[] a, double[] b, double[] c)
    {
        double[] closest = ClosestPointOnTriangle(p, a, b, c);
        return MathCore.Distance3D(p[0], p[1], p[2], closest[0], closest[1], closest[2]);
    }
    public static bool PointInsideAABB(double[] p, double[] min, double[] max)
    {
        for (int i = 0; i < p.Length; i++)
            if (p[i] < min[i] || p[i] > max[i]) return false;
        return true;
    }
    public static bool PointInsideSphere(double[] p, double[] center, double radius)
    {
        return MathCore.Distance3D(p[0], p[1], p[2], center[0], center[1], center[2]) <= radius;
    }
    public static bool RayIntersectsTriangle(double[] origin, double[] direction, double[] v0, double[] v1, double[] v2, out double t)
    {
        t = 0;
        double[] edge1 = v1.Select((x, i) => x - v0[i]).ToArray();
        double[] edge2 = v2.Select((x, i) => x - v0[i]).ToArray();
        double[] h = CrossProduct(direction, edge2);
        double a = DotProduct(edge1, h);
        if (a > -1e-6 && a < 1e-6) return false;
        double f = 1.0 / a;
        double[] s = origin.Select((x, i) => x - v0[i]).ToArray();
        double u = f * DotProduct(s, h);
        if (u < 0 || u > 1) return false;
        double[] q = CrossProduct(s, edge1);
        double v = f * DotProduct(direction, q);
        if (v < 0 || u + v > 1) return false;
        t = f * DotProduct(edge2, q);
        return t > 1e-6;
    }
    public static bool RayIntersectsSphere(double[] origin, double[] direction, double[] center, double radius, out double t)
    {
        t = 0;
        double[] oc = origin.Select((x, i) => x - center[i]).ToArray();
        double a = DotProduct(direction, direction);
        double b = 2 * DotProduct(oc, direction);
        double c = DotProduct(oc, oc) - radius * radius;
        double discriminant = b * b - 4 * a * c;
        if (discriminant < 0) return false;
        t = (-b - MathCore.Sqrt(discriminant)) / (2 * a);
        return t > 0;
    }
    public static bool RayIntersectsAABB(double[] origin, double[] direction, double[] min, double[] max, out double t)
    {
        t = 0;
        double tmin = double.MinValue;
        double tmax = double.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(direction[i]) < 1e-6)
            {
                if (origin[i] < min[i] || origin[i] > max[i]) return false;
            }
            else
            {
                double t1 = (min[i] - origin[i]) / direction[i];
                double t2 = (max[i] - origin[i]) / direction[i];
                if (t1 > t2) { double temp = t1; t1 = t2; t2 = temp; }
                tmin = Math.Max(tmin, t1);
                tmax = Math.Min(tmax, t2);
                if (tmin > tmax) return false;
            }
        }
        t = tmin;
        return t > 0;
    }
    public static double[][] ConvexHull(double[][] points)
    {
        if (points.Length < 3) return points;
        var sorted = points.OrderBy(p => p[0]).ThenBy(p => p[1]).ToArray();
        var lower = new List<double[]>();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross2D(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<double[]>();
        for (int i = sorted.Length - 1; i >= 0; i--)
        {
            while (upper.Count >= 2 && Cross2D(upper[^2], upper[^1], sorted[i]) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(sorted[i]);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        return [..lower, ..upper];
    }
    public static double PolygonArea(double[][] polygon)
    {
        double area = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            int j = (i + 1) % polygon.Length;
            area += polygon[i][0] * polygon[j][1] - polygon[j][0] * polygon[i][1];
        }
        return Math.Abs(area) * 0.5;
    }
    public static bool PointInPolygon(double[] p, double[][] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            if (((polygon[i][1] > p[1]) != (polygon[j][1] > p[1])) &&
                (p[0] < (polygon[j][0] - polygon[i][0]) * (p[1] - polygon[i][1]) / (polygon[j][1] - polygon[i][1]) + polygon[i][0]))
                inside = !inside;
        }
        return inside;
    }
    public static double[][] GenerateCircle(double radius, int segments)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= segments; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            points.Add([radius * MathCore.Cos(angle), radius * MathCore.Sin(angle), 0]);
        }
        return points.ToArray();
    }
    public static double[][] GenerateSphere(double radius, int latSegments, int lonSegments)
    {
        var points = new List<double[]>();
        for (int lat = 0; lat <= latSegments; lat++)
        {
            double theta = Math.PI * lat / latSegments;
            double sinTheta = MathCore.Sin(theta);
            double cosTheta = MathCore.Cos(theta);
            for (int lon = 0; lon <= lonSegments; lon++)
            {
                double phi = 2 * Math.PI * lon / lonSegments;
                double sinPhi = MathCore.Sin(phi);
                double cosPhi = MathCore.Cos(phi);
                points.Add([radius * sinTheta * cosPhi, radius * cosTheta, radius * sinTheta * sinPhi]);
            }
        }
        return points.ToArray();
    }
    public static double[][] GenerateCube(double size)
    {
        double h = size / 2;
        return new double[][]
        {
            [-h, -h, -h], [h, -h, -h], [h, h, -h], [-h, h, -h],
            [-h, -h, h], [h, -h, h], [h, h, h], [-h, h, h]
        };
    }
    public static double[][] GenerateGrid(int width, int height, double spacing)
    {
        var points = new List<double[]>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                points.Add([x * spacing, 0, y * spacing]);
        return points.ToArray();
    }
}
