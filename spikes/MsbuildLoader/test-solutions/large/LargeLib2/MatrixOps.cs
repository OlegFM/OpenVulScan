using System;
using System.Collections.Generic;
using System.Linq;
using LargeLib1;

namespace LargeLib2;

public static class MatrixOps
{
    public static double[][] LookAtMatrix(double[] eye, double[] target, double[] up)
    {
        double[] z = Geometry.NormalizeVector(target.Select((x, i) => x - eye[i]).ToArray());
        double[] x = Geometry.NormalizeVector(Geometry.CrossProduct(up, z));
        double[] y = Geometry.CrossProduct(z, x);
        return new double[][]
        {
            [x[0], x[1], x[2], -Geometry.DotProduct(x, eye)],
            [y[0], y[1], y[2], -Geometry.DotProduct(y, eye)],
            [z[0], z[1], z[2], -Geometry.DotProduct(z, eye)],
            [0, 0, 0, 1]
        };
    }
    public static double[][] PerspectiveMatrix(double fov, double aspect, double near, double far)
    {
        double f = 1.0 / Math.Tan(fov / 2);
        double nf = 1.0 / (near - far);
        return new double[][]
        {
            [f / aspect, 0, 0, 0],
            [0, f, 0, 0],
            [0, 0, (far + near) * nf, 2 * far * near * nf],
            [0, 0, -1, 0]
        };
    }
    public static double[][] OrthographicMatrix(double left, double right, double bottom, double top, double near, double far)
    {
        double rl = 1.0 / (right - left);
        double tb = 1.0 / (top - bottom);
        double fn = 1.0 / (far - near);
        return new double[][]
        {
            [2 * rl, 0, 0, -(right + left) * rl],
            [0, 2 * tb, 0, -(top + bottom) * tb],
            [0, 0, -2 * fn, -(far + near) * fn],
            [0, 0, 0, 1]
        };
    }
    public static double[][] TranslationMatrix(double x, double y, double z)
    {
        return new double[][]
        {
            [1, 0, 0, x],
            [0, 1, 0, y],
            [0, 0, 1, z],
            [0, 0, 0, 1]
        };
    }
    public static double[][] ScaleMatrix(double x, double y, double z)
    {
        return new double[][]
        {
            [x, 0, 0, 0],
            [0, y, 0, 0],
            [0, 0, z, 0],
            [0, 0, 0, 1]
        };
    }
    public static double[][] RotationMatrixX(double angle)
    {
        double c = MathCore.Cos(angle);
        double s = MathCore.Sin(angle);
        return new double[][]
        {
            [1, 0, 0, 0],
            [0, c, -s, 0],
            [0, s, c, 0],
            [0, 0, 0, 1]
        };
    }
    public static double[][] RotationMatrixY(double angle)
    {
        double c = MathCore.Cos(angle);
        double s = MathCore.Sin(angle);
        return new double[][]
        {
            [c, 0, s, 0],
            [0, 1, 0, 0],
            [-s, 0, c, 0],
            [0, 0, 0, 1]
        };
    }
    public static double[][] RotationMatrixZ(double angle)
    {
        double c = MathCore.Cos(angle);
        double s = MathCore.Sin(angle);
        return new double[][]
        {
            [c, -s, 0, 0],
            [s, c, 0, 0],
            [0, 0, 1, 0],
            [0, 0, 0, 1]
        };
    }
    public static double[] MultiplyMatrixVector(double[][] matrix, double[] vector)
    {
        var result = new double[4];
        for (int i = 0; i < 4; i++)
        {
            double sum = 0;
            for (int j = 0; j < 4; j++) sum += matrix[i][j] * vector[j];
            result[i] = sum;
        }
        return result;
    }
    public static double[][] MultiplyMatrices(double[][] a, double[][] b)
    {
        var result = new double[4][];
        for (int i = 0; i < 4; i++)
        {
            result[i] = new double[4];
            for (int j = 0; j < 4; j++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++) sum += a[i][k] * b[k][j];
                result[i][j] = sum;
            }
        }
        return result;
    }
    public static double[] TransformPoint(double[][] matrix, double[] point)
    {
        double[] homogeneous = [point[0], point[1], point[2], 1];
        var transformed = MultiplyMatrixVector(matrix, homogeneous);
        double w = transformed[3];
        return [transformed[0] / w, transformed[1] / w, transformed[2] / w];
    }
    public static double[] TransformDirection(double[][] matrix, double[] direction)
    {
        double[] homogeneous = [direction[0], direction[1], direction[2], 0];
        var transformed = MultiplyMatrixVector(matrix, homogeneous);
        return [transformed[0], transformed[1], transformed[2]];
    }
    public static double[][] TransposeMatrix(double[][] matrix)
    {
        var result = new double[4][];
        for (int i = 0; i < 4; i++)
        {
            result[i] = new double[4];
            for (int j = 0; j < 4; j++) result[i][j] = matrix[j][i];
        }
        return result;
    }
    public static double MatrixDeterminant3x3(double[][] m)
    {
        return m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1])
             - m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0])
             + m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0]);
    }
    public static double[][] MatrixInverse3x3(double[][] m)
    {
        double det = MatrixDeterminant3x3(m);
        var result = new double[3][];
        result[0] = [(m[1][1] * m[2][2] - m[1][2] * m[2][1]) / det, -(m[0][1] * m[2][2] - m[0][2] * m[2][1]) / det, (m[0][1] * m[1][2] - m[0][2] * m[1][1]) / det];
        result[1] = [-(m[1][0] * m[2][2] - m[1][2] * m[2][0]) / det, (m[0][0] * m[2][2] - m[0][2] * m[2][0]) / det, -(m[0][0] * m[1][2] - m[0][2] * m[1][0]) / det];
        result[2] = [(m[1][0] * m[2][1] - m[1][1] * m[2][0]) / det, -(m[0][0] * m[2][1] - m[0][1] * m[2][0]) / det, (m[0][0] * m[1][1] - m[0][1] * m[1][0]) / det];
        return result;
    }
    public static double[] ProjectToScreen(double[] point, double[][] mvp, int screenWidth, int screenHeight)
    {
        var clip = MultiplyMatrixVector(mvp, [point[0], point[1], point[2], 1]);
        double ndcX = clip[0] / clip[3];
        double ndcY = clip[1] / clip[3];
        double screenX = (ndcX + 1) * 0.5 * screenWidth;
        double screenY = (1 - ndcY) * 0.5 * screenHeight;
        return [screenX, screenY];
    }
    public static double[] UnprojectFromScreen(double screenX, double screenY, double depth, double[][] invMvp, int screenWidth, int screenHeight)
    {
        double ndcX = screenX / screenWidth * 2 - 1;
        double ndcY = 1 - screenY / screenHeight * 2;
        var clip = MultiplyMatrixVector(invMvp, [ndcX, ndcY, depth, 1]);
        return [clip[0] / clip[3], clip[1] / clip[3], clip[2] / clip[3]];
    }
    public static double[] RayFromScreen(double screenX, double screenY, double[][] invViewProj, int screenWidth, int screenHeight)
    {
        var near = UnprojectFromScreen(screenX, screenY, -1, invViewProj, screenWidth, screenHeight);
        var far = UnprojectFromScreen(screenX, screenY, 1, invViewProj, screenWidth, screenHeight);
        return far.Select((x, i) => x - near[i]).ToArray();
    }
    public static bool RayIntersectsTriangle(double[] origin, double[] direction, double[] v0, double[] v1, double[] v2, out double t)
    {
        t = 0;
        double[] edge1 = v1.Select((x, i) => x - v0[i]).ToArray();
        double[] edge2 = v2.Select((x, i) => x - v0[i]).ToArray();
        double[] h = Geometry.CrossProduct(direction, edge2);
        double a = Geometry.DotProduct(edge1, h);
        if (a > -1e-6 && a < 1e-6) return false;
        double f = 1.0 / a;
        double[] s = origin.Select((x, i) => x - v0[i]).ToArray();
        double u = f * Geometry.DotProduct(s, h);
        if (u < 0 || u > 1) return false;
        double[] q = Geometry.CrossProduct(s, edge1);
        double v = f * Geometry.DotProduct(direction, q);
        if (v < 0 || u + v > 1) return false;
        t = f * Geometry.DotProduct(edge2, q);
        return t > 1e-6;
    }
    public static bool RayIntersectsSphere(double[] origin, double[] direction, double[] center, double radius, out double t)
    {
        t = 0;
        double[] oc = origin.Select((x, i) => x - center[i]).ToArray();
        double a = Geometry.DotProduct(direction, direction);
        double b = 2 * Geometry.DotProduct(oc, direction);
        double c = Geometry.DotProduct(oc, oc) - radius * radius;
        double discriminant = b * b - 4 * a * c;
        if (discriminant < 0) return false;
        t = (-b - MathCore.Sqrt(discriminant)) / (2 * a);
        return t > 0;
    }
    public static bool RayIntersectsPlane(double[] origin, double[] direction, double[] normal, double d, out double t)
    {
        t = 0;
        double denom = Geometry.DotProduct(direction, normal);
        if (Math.Abs(denom) < 1e-6) return false;
        double[] p = [0, 0, -d / normal[2]];
        if (normal[2] == 0) p = [0, -d / normal[1], 0];
        if (normal[1] == 0 && normal[2] == 0) p = [-d / normal[0], 0, 0];
        t = Geometry.DotProduct(p.Select((x, i) => x - origin[i]).ToArray(), normal) / denom;
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
    public static double[] BarycentricCoordinates(double[] p, double[] a, double[] b, double[] c)
    {
        return Geometry.BarycentricCoordinates(p, a, b, c);
    }
    public static double[] TriangleNormal(double[] v0, double[] v1, double[] v2)
    {
        return Geometry.TriangleNormal(v0, v1, v2);
    }
    public static double TriangleArea(double[] v0, double[] v1, double[] v2)
    {
        return Geometry.TriangleArea(v0, v1, v2);
    }
    public static double[] TriangleCentroid(double[] v0, double[] v1, double[] v2)
    {
        return Geometry.TriangleCentroid(v0, v1, v2);
    }
    public static double[][] BoundingBox(double[][] vertices)
    {
        return Geometry.BoundingBox(vertices);
    }
    public static double[] BoundingSphereCenter(double[][] vertices)
    {
        return Geometry.BoundingSphereCenter(vertices);
    }
    public static double BoundingSphereRadius(double[][] vertices)
    {
        return Geometry.BoundingSphereRadius(vertices);
    }
    public static double[] ClosestPointOnLine(double[] p, double[] a, double[] b)
    {
        return Geometry.ClosestPointOnLine(p, a, b);
    }
    public static double[] ClosestPointOnTriangle(double[] p, double[] a, double[] b, double[] c)
    {
        return Geometry.ClosestPointOnTriangle(p, a, b, c);
    }
    public static double DistancePointToPlane(double[] p, double[] normal, double d)
    {
        return Geometry.DistancePointToPlane(p, normal, d);
    }
    public static double DistancePointToLine(double[] p, double[] a, double[] b)
    {
        return Geometry.DistancePointToLine(p, a, b);
    }
    public static double DistancePointToTriangle(double[] p, double[] a, double[] b, double[] c)
    {
        return Geometry.DistancePointToTriangle(p, a, b, c);
    }
    public static bool PointInsideAABB(double[] p, double[] min, double[] max)
    {
        return Geometry.PointInsideAABB(p, min, max);
    }
    public static bool PointInsideSphere(double[] p, double[] center, double radius)
    {
        return Geometry.PointInsideSphere(p, center, radius);
    }
    public static bool PointInTriangle(double[] p, double[] a, double[] b, double[] c)
    {
        return Geometry.PointInTriangle(p, a, b, c);
    }
    public static bool TrianglesIntersect(double[] a0, double[] a1, double[] a2, double[] b0, double[] b1, double[] b2)
    {
        double[] n1 = TriangleNormal(a0, a1, a2);
        double[] n2 = TriangleNormal(b0, b1, b2);
        if (Geometry.VectorLength(Geometry.CrossProduct(n1, n2)) < 1e-6) return false;
        double d1 = -Geometry.DotProduct(n1, a0);
        double d2 = -Geometry.DotProduct(n2, b0);
        double[] distA = [Geometry.DotProduct(b0, n1) + d1, Geometry.DotProduct(b1, n1) + d1, Geometry.DotProduct(b2, n1) + d1];
        double[] distB = [Geometry.DotProduct(a0, n2) + d2, Geometry.DotProduct(a1, n2) + d2, Geometry.DotProduct(a2, n2) + d2];
        if (distA.All(d => d > 0) || distA.All(d => d < 0)) return false;
        if (distB.All(d => d > 0) || distB.All(d => d < 0)) return false;
        return true;
    }
    public static double[] PlaneIntersection(double[] n1, double d1, double[] n2, double d2, double[] n3, double d3)
    {
        double det = Geometry.DotProduct(n1, Geometry.CrossProduct(n2, n3));
        if (Math.Abs(det) < 1e-6) return [];
        double[] p = Geometry.CrossProduct(n2, n3).Select(x => x * d1).ToArray();
        double[] q = Geometry.CrossProduct(n3, n1).Select(x => x * d2).ToArray();
        double[] r = Geometry.CrossProduct(n1, n2).Select(x => x * d3).ToArray();
        return p.Select((x, i) => (x + q[i] + r[i]) / det).ToArray();
    }
    public static double[] LinePlaneIntersection(double[] linePoint, double[] lineDir, double[] planeNormal, double planeD)
    {
        double denom = Geometry.DotProduct(lineDir, planeNormal);
        if (Math.Abs(denom) < 1e-6) return [];
        double t = -(Geometry.DotProduct(linePoint, planeNormal) + planeD) / denom;
        return linePoint.Select((x, i) => x + t * lineDir[i]).ToArray();
    }
    public static double[] LineLineClosestPoints(double[] p1, double[] d1, double[] p2, double[] d2)
    {
        double[] r = p1.Select((x, i) => x - p2[i]).ToArray();
        double a = Geometry.DotProduct(d1, d1);
        double b = Geometry.DotProduct(d1, d2);
        double c = Geometry.DotProduct(d2, d2);
        double d = Geometry.DotProduct(d1, r);
        double e = Geometry.DotProduct(d2, r);
        double denom = a * c - b * b;
        if (Math.Abs(denom) < 1e-6) return [];
        double s = (b * e - c * d) / denom;
        double t = (a * e - b * d) / denom;
        double[] closest1 = p1.Select((x, i) => x + s * d1[i]).ToArray();
        double[] closest2 = p2.Select((x, i) => x + t * d2[i]).ToArray();
        return [..closest1, ..closest2];
    }
    public static double LineLineDistance(double[] p1, double[] d1, double[] p2, double[] d2)
    {
        double[] closest = LineLineClosestPoints(p1, d1, p2, d2);
        if (closest.Length == 0) return 0;
        return MathCore.Distance3D(closest[0], closest[1], closest[2], closest[3], closest[4], closest[5]);
    }
    public static bool SegmentSegmentIntersect2D(double[] a1, double[] a2, double[] b1, double[] b2)
    {
        double d1 = (a2[0] - a1[0]) * (b1[1] - a1[1]) - (a2[1] - a1[1]) * (b1[0] - a1[0]);
        double d2 = (a2[0] - a1[0]) * (b2[1] - a1[1]) - (a2[1] - a1[1]) * (b2[0] - a1[0]);
        double d3 = (b2[0] - b1[0]) * (a1[1] - b1[1]) - (b2[1] - b1[1]) * (a1[0] - b1[0]);
        double d4 = (b2[0] - b1[0]) * (a2[1] - b1[1]) - (b2[1] - b1[1]) * (a2[0] - b1[0]);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
        if (d1 == 0 && OnSegment(a1, a2, b1)) return true;
        if (d2 == 0 && OnSegment(a1, a2, b2)) return true;
        if (d3 == 0 && OnSegment(b1, b2, a1)) return true;
        if (d4 == 0 && OnSegment(b1, b2, a2)) return true;
        return false;
    }
    public static bool OnSegment(double[] a, double[] b, double[] p)
    {
        return Math.Min(a[0], b[0]) <= p[0] && p[0] <= Math.Max(a[0], b[0]) && Math.Min(a[1], b[1]) <= p[1] && p[1] <= Math.Max(a[1], b[1]);
    }
    public static double[] PolygonCentroid(double[][] polygon)
    {
        double cx = 0, cy = 0;
        double area = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            int j = (i + 1) % polygon.Length;
            double cross = polygon[i][0] * polygon[j][1] - polygon[j][0] * polygon[i][1];
            cx += (polygon[i][0] + polygon[j][0]) * cross;
            cy += (polygon[i][1] + polygon[j][1]) * cross;
            area += cross;
        }
        area *= 0.5;
        cx /= 6 * area;
        cy /= 6 * area;
        return [cx, cy];
    }
    public static double PolygonArea(double[][] polygon)
    {
        return Geometry.PolygonArea(polygon);
    }
    public static bool PointInPolygon(double[] p, double[][] polygon)
    {
        return Geometry.PointInPolygon(p, polygon);
    }
    public static double[] PolygonBounds(double[][] polygon)
    {
        double minX = polygon.Min(p => p[0]);
        double minY = polygon.Min(p => p[1]);
        double maxX = polygon.Max(p => p[0]);
        double maxY = polygon.Max(p => p[1]);
        return [minX, minY, maxX, maxY];
    }
    public static double[][] ConvexHull(double[][] points)
    {
        return Geometry.ConvexHull(points);
    }
    public static double[][] SimplifyPolygon(double[][] polygon, double tolerance)
    {
        if (polygon.Length <= 2) return polygon;
        double dmax = 0;
        int index = 0;
        for (int i = 1; i < polygon.Length - 1; i++)
        {
            double d = PerpendicularDistance(polygon[i], polygon[0], polygon[^1]);
            if (d > dmax) { index = i; dmax = d; }
        }
        if (dmax > tolerance)
        {
            var left = SimplifyPolygon(polygon[..(index + 1)], tolerance);
            var right = SimplifyPolygon(polygon[index..], tolerance);
            return [..left[..^1], ..right];
        }
        return [polygon[0], polygon[^1]];
    }
    public static double PerpendicularDistance(double[] p, double[] lineStart, double[] lineEnd)
    {
        double[] diff = lineEnd.Select((x, i) => x - lineStart[i]).ToArray();
        if (diff[0] == 0 && diff[1] == 0) return MathCore.Distance2D(p[0], p[1], lineStart[0], lineStart[1]);
        double t = ((p[0] - lineStart[0]) * diff[0] + (p[1] - lineStart[1]) * diff[1]) / (diff[0] * diff[0] + diff[1] * diff[1]);
        t = Math.Max(0, Math.Min(1, t));
        double[] closest = lineStart.Select((x, i) => x + t * diff[i]).ToArray();
        return MathCore.Distance2D(p[0], p[1], closest[0], closest[1]);
    }
    public static double[] CatmullRomSpline(double t, double[] p0, double[] p1, double[] p2, double[] p3)
    {
        double t2 = t * t;
        double t3 = t2 * t;
        return p1.Select((x, i) => 0.5 * ((2 * x) +
            (-p0[i] + p2[i]) * t +
            (2 * p0[i] - 5 * x + 4 * p2[i] - p3[i]) * t2 +
            (-p0[i] + 3 * x - 3 * p2[i] + p3[i]) * t3)).ToArray();
    }
    public static double[] BezierQuadratic(double t, double[] p0, double[] p1, double[] p2)
    {
        double u = 1 - t;
        return p0.Select((x, i) => u * u * x + 2 * u * t * p1[i] + t * t * p2[i]).ToArray();
    }
    public static double[] BezierCubic(double t, double[] p0, double[] p1, double[] p2, double[] p3)
    {
        double u = 1 - t;
        double u2 = u * u;
        double t2 = t * t;
        return p0.Select((x, i) => u2 * u * x + 3 * u2 * t * p1[i] + 3 * u * t2 * p2[i] + t2 * t * p3[i]).ToArray();
    }
    public static double[][] BezierCubicControlPoints(double[] start, double[] end, double[] tangentStart, double[] tangentEnd)
    {
        double[] p1 = start.Select((x, i) => x + tangentStart[i] / 3).ToArray();
        double[] p2 = end.Select((x, i) => x - tangentEnd[i] / 3).ToArray();
        return [start, p1, p2, end];
    }
    public static double ArcLength(Func<double, double[]> curve, double t0, double t1, int segments)
    {
        double length = 0;
        double dt = (t1 - t0) / segments;
        var prev = curve(t0);
        for (int i = 1; i <= segments; i++)
        {
            var curr = curve(t0 + i * dt);
            length += MathCore.Distance3D(prev[0], prev[1], prev[2], curr[0], curr[1], curr[2]);
            prev = curr;
        }
        return length;
    }
    public static double[] SampleArcLength(Func<double, double[]> curve, double t0, double t1, int segments, double targetLength)
    {
        double dt = (t1 - t0) / segments;
        double length = 0;
        var prev = curve(t0);
        for (int i = 1; i <= segments; i++)
        {
            var curr = curve(t0 + i * dt);
            double segLen = MathCore.Distance3D(prev[0], prev[1], prev[2], curr[0], curr[1], curr[2]);
            if (length + segLen >= targetLength)
            {
                double t = (targetLength - length) / segLen;
                return prev.Select((x, i) => x + t * (curr[i] - x)).ToArray();
            }
            length += segLen;
            prev = curr;
        }
        return prev;
    }
    public static double[][] SubdivideBezier(double[] p0, double[] p1, double[] p2, double[] p3, double t)
    {
        double[] q0 = Geometry.LerpVector(p0, p1, t);
        double[] q1 = Geometry.LerpVector(p1, p2, t);
        double[] q2 = Geometry.LerpVector(p2, p3, t);
        double[] r0 = Geometry.LerpVector(q0, q1, t);
        double[] r1 = Geometry.LerpVector(q1, q2, t);
        double[] s = Geometry.LerpVector(r0, r1, t);
        return [p0, q0, r0, s, r1, q2, p3];
    }
    public static double[][] TessellateBezier(double[] p0, double[] p1, double[] p2, double[] p3, double tolerance)
    {
        var points = new List<double[]> { p0 };
        void Subdivide(double[] a0, double[] a1, double[] a2, double[] a3)
        {
            double[] mid = BezierCubic(0.5, a0, a1, a2, a3);
            double[] chord = a3.Select((x, i) => x - a0[i]).ToArray();
            double[] diff = mid.Select((x, i) => x - (a0[i] + chord[i] * 0.5)).ToArray();
            if (Geometry.VectorLength(diff) > tolerance)
            {
                var sub = SubdivideBezier(a0, a1, a2, a3, 0.5);
                Subdivide(sub[0], sub[1], sub[2], sub[3]);
                Subdivide(sub[3], sub[4], sub[5], sub[6]);
            }
            else
            {
                points.Add(mid);
                points.Add(a3);
            }
        }
        Subdivide(p0, p1, p2, p3);
        return points.ToArray();
    }
    public static double[][] GenerateCircle(double radius, int segments)
    {
        return Geometry.GenerateCircle(radius, segments);
    }
    public static double[][] GenerateSphere(double radius, int latSegments, int lonSegments)
    {
        return Geometry.GenerateSphere(radius, latSegments, lonSegments);
    }
    public static double[][] GenerateTorus(double majorRadius, double minorRadius, int majorSegments, int minorSegments)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= majorSegments; i++)
        {
            double u = 2 * Math.PI * i / majorSegments;
            for (int j = 0; j <= minorSegments; j++)
            {
                double v = 2 * Math.PI * j / minorSegments;
                points.Add([(majorRadius + minorRadius * MathCore.Cos(v)) * MathCore.Cos(u),
                            minorRadius * MathCore.Sin(v),
                            (majorRadius + minorRadius * MathCore.Cos(v)) * MathCore.Sin(u)]);
            }
        }
        return points.ToArray();
    }
    public static double[][] GenerateCylinder(double radius, double height, int segments)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= segments; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            points.Add([radius * MathCore.Cos(angle), -height / 2, radius * MathCore.Sin(angle)]);
            points.Add([radius * MathCore.Cos(angle), height / 2, radius * MathCore.Sin(angle)]);
        }
        return points.ToArray();
    }
    public static double[][] GenerateCone(double radius, double height, int segments)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= segments; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            points.Add([radius * MathCore.Cos(angle), 0, radius * MathCore.Sin(angle)]);
            points.Add([0, height, 0]);
        }
        return points.ToArray();
    }
    public static double[][] GeneratePlane(double width, double depth, int wSegments, int dSegments)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= wSegments; i++)
        {
            for (int j = 0; j <= dSegments; j++)
            {
                double x = width * i / wSegments - width / 2;
                double z = depth * j / dSegments - depth / 2;
                points.Add([x, 0, z]);
            }
        }
        return points.ToArray();
    }
    public static double[][] GenerateIcosahedron()
    {
        double phi = (1 + Math.Sqrt(5)) / 2;
        return new double[][]
        {
            [-1, phi, 0], [1, phi, 0], [-1, -phi, 0], [1, -phi, 0],
            [0, -1, phi], [0, 1, phi], [0, -1, -phi], [0, 1, -phi],
            [phi, 0, -1], [phi, 0, 1], [-phi, 0, -1], [-phi, 0, 1]
        };
    }
    public static double[][] GenerateUVSphere(int latSegments, int lonSegments)
    {
        return GenerateSphere(1, latSegments, lonSegments);
    }
    public static double[][] GenerateCube(double size)
    {
        return Geometry.GenerateCube(size);
    }
    public static int[][] CubeFaces() =>
    [
        [0, 1, 2, 3], [1, 5, 6, 2], [5, 4, 7, 6],
        [4, 0, 3, 7], [3, 2, 6, 7], [4, 5, 1, 0]
    ];
    public static double[][] GenerateGrid(int width, int height, double spacing)
    {
        return Geometry.GenerateGrid(width, height, spacing);
    }
    public static double[][] DisplaceVertices(double[][] vertices, Func<double[], double> displacement)
    {
        return vertices.Select(v =>
        {
            double d = displacement(v);
            var n = Geometry.NormalizeVector(v);
            return v.Select((x, i) => x + n[i] * d).ToArray();
        }).ToArray();
    }
    public static double[] SmoothVertex(double[][] vertices, int[][] neighbors, int index)
    {
        var sum = new double[vertices[0].Length];
        foreach (var n in neighbors[index])
        {
            for (int i = 0; i < sum.Length; i++) sum[i] += vertices[n][i];
        }
        return sum.Select(x => x / neighbors[index].Length).ToArray();
    }
    public static double[][] LaplacianSmooth(double[][] vertices, int[][] neighbors, int iterations)
    {
        var result = vertices;
        for (int iter = 0; iter < iterations; iter++)
        {
            result = result.Select((v, i) => Geometry.LerpVector(v, SmoothVertex(result, neighbors, i), 0.5)).ToArray();
        }
        return result;
    }
}
