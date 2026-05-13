using System;
using System.Collections.Generic;
using LargeLib1;
using LargeLib2;
using LargeLib3;

namespace LargeLib4;

public static class SceneBuilder
{
    public static double[][] CreateTerrain(int width, int height, int seed = 0)
    {
        return Shapes.GenerateHeightmap(width, height, seed);
    }
    public static double[][] CreateNormalMapForTerrain(double[][] terrain, double strength = 1.0)
    {
        return Shapes.GenerateNormalMap(terrain, strength);
    }
    public static double[][] CreateSkySphere(int latSegments, int lonSegments)
    {
        return Shapes.GenerateUVSphere(latSegments, lonSegments);
    }
    public static double[][] CreateGroundPlane(double width, double depth, int wSegments, int dSegments)
    {
        return MatrixOps.GeneratePlane(width, depth, wSegments, dSegments);
    }
    public static double[][] CreateTreeTrunk(double height, double radius, int segments)
    {
        return MatrixOps.GenerateCylinder(radius, height, segments);
    }
    public static double[][] CreateTreeCanopy(double radius, int latSegments, int lonSegments)
    {
        return MatrixOps.GenerateSphere(radius, latSegments, lonSegments);
    }
    public static double[][] CreateRock(double size)
    {
        return MatrixOps.GenerateCube(size);
    }
    public static double[][] CreateWaterPlane(double width, double depth, int wSegments, int dSegments)
    {
        return MatrixOps.GeneratePlane(width, depth, wSegments, dSegments);
    }
    public static double[][] CreateBuilding(double width, double height, double depth)
    {
        var baseShape = new double[][]
        {
            [-width / 2, 0, -depth / 2],
            [width / 2, 0, -depth / 2],
            [width / 2, 0, depth / 2],
            [-width / 2, 0, depth / 2]
        };
        return Shapes.ExtrudePolygon(baseShape, height);
    }
    public static double[][] CreateRoad(double width, double length, int segments)
    {
        return MatrixOps.GeneratePlane(width, length, segments, segments);
    }
    public static double[][] CreateBridge(double length, double width, double height, int segments)
    {
        var arch = new double[segments + 1][];
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            arch[i] = [t * length - length / 2, Math.Sin(t * Math.PI) * height, 0];
        }
        return Shapes.SweepPath(new double[][] { [-width / 2, 0], [width / 2, 0] }, arch);
    }
    public static double[][] CreateTunnel(double length, double radius, int segments)
    {
        return MatrixOps.GenerateCylinder(radius, length, segments);
    }
    public static double[][] CreateStairs(double width, double height, double depth, int steps)
    {
        var stairPoints = new List<double[]>();
        for (int i = 0; i < steps; i++)
        {
            double y = i * height / steps;
            double z = i * depth / steps;
            stairPoints.Add([-width / 2, y, z]);
            stairPoints.Add([width / 2, y, z]);
            stairPoints.Add([width / 2, y + height / steps, z]);
            stairPoints.Add([-width / 2, y + height / steps, z]);
        }
        return stairPoints.ToArray();
    }
    public static double[][] CreateWall(double width, double height, int segments)
    {
        return MatrixOps.GeneratePlane(width, height, segments, segments);
    }
    public static double[][] CreateFence(double length, double height, int posts)
    {
        var fencePoints = new List<double[]>();
        for (int i = 0; i <= posts; i++)
        {
            double x = i * length / posts - length / 2;
            fencePoints.Add([x, 0, 0]);
            fencePoints.Add([x, height, 0]);
        }
        return fencePoints.ToArray();
    }
    public static double[][] CreatePillar(double radius, double height, int segments)
    {
        return MatrixOps.GenerateCylinder(radius, height, segments);
    }
    public static double[][] CreateDome(double radius, int latSegments, int lonSegments)
    {
        return MatrixOps.GenerateSphere(radius, latSegments, lonSegments);
    }
    public static double[][] CreatePyramid(double baseSize, double height, int segments)
    {
        var baseShape = new double[][]
        {
            [-baseSize / 2, 0, -baseSize / 2],
            [baseSize / 2, 0, -baseSize / 2],
            [baseSize / 2, 0, baseSize / 2],
            [-baseSize / 2, 0, baseSize / 2]
        };
        var apex = new double[][] { [0, height, 0] };
        return [..baseShape, ..apex];
    }
    public static double[][] CreateTorusKnot(double p, double q, double radius, int segments)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= segments; i++)
        {
            double t = 2 * Math.PI * i / segments;
            double r = MathCore.Cos(q * t) + 2;
            double x = r * MathCore.Cos(p * t);
            double y = r * MathCore.Sin(p * t);
            double z = -MathCore.Sin(q * t);
            points.Add([x * radius, y * radius, z * radius]);
        }
        return points.ToArray();
    }
    public static double[][] CreateMaze(int width, int height, double cellSize, int seed = 0)
    {
        var rng = new Random(seed);
        var grid = new bool[height, width];
        var stack = new Stack<(int x, int y)>();
        stack.Push((0, 0));
        grid[0, 0] = true;
        var walls = new List<double[]>();
        while (stack.Count > 0)
        {
            var (x, y) = stack.Peek();
            var neighbors = new List<(int nx, int ny)>();
            if (x > 0 && !grid[y, x - 1]) neighbors.Add((x - 1, y));
            if (x < width - 1 && !grid[y, x + 1]) neighbors.Add((x + 1, y));
            if (y > 0 && !grid[y - 1, x]) neighbors.Add((x, y - 1));
            if (y < height - 1 && !grid[y + 1, x]) neighbors.Add((x, y + 1));
            if (neighbors.Count > 0)
            {
                var (nx, ny) = neighbors[rng.Next(neighbors.Count)];
                grid[ny, nx] = true;
                stack.Push((nx, ny));
            }
            else
            {
                stack.Pop();
            }
        }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!grid[y, x])
                {
                    walls.Add([x * cellSize, 0, y * cellSize]);
                    walls.Add([x * cellSize + cellSize, 0, y * cellSize]);
                    walls.Add([x * cellSize + cellSize, 0, y * cellSize + cellSize]);
                    walls.Add([x * cellSize, 0, y * cellSize + cellSize]);
                }
            }
        }
        return walls.ToArray();
    }
    public static double[][] CreateLabyrinth(int width, int height, double cellSize, int seed = 0)
    {
        return CreateMaze(width, height, cellSize, seed);
    }
    public static double[][] CreateCityGrid(int blocksX, int blocksZ, double blockSize, double streetWidth)
    {
        var points = new List<double[]>();
        for (int x = 0; x <= blocksX; x++)
        {
            for (int z = 0; z <= blocksZ; z++)
            {
                points.Add([x * (blockSize + streetWidth) - streetWidth / 2, 0, z * (blockSize + streetWidth) - streetWidth / 2]);
            }
        }
        return points.ToArray();
    }
    public static double[][] CreateSpiralStairs(double radius, double height, int steps, int segments)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= steps; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            double y = i * height / steps;
            points.Add([radius * MathCore.Cos(angle), y, radius * MathCore.Sin(angle)]);
        }
        return points.ToArray();
    }
    public static double[][] CreateHelix(double radius, double height, int turns, int segments)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            double angle = 2 * Math.PI * turns * t;
            double y = height * t;
            points.Add([radius * MathCore.Cos(angle), y, radius * MathCore.Sin(angle)]);
        }
        return points.ToArray();
    }
    public static double[][] CreateSpring(double radius, double height, int turns, int segments, double coilRadius)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            double angle = 2 * Math.PI * turns * t;
            double y = height * t;
            points.Add([(radius + coilRadius * MathCore.Cos(angle * 10)) * MathCore.Cos(angle),
                        y,
                        (radius + coilRadius * MathCore.Cos(angle * 10)) * MathCore.Sin(angle)]);
        }
        return points.ToArray();
    }
    public static double[][] CreateDNA(double radius, double height, int basePairs, int segments)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            double angle = 2 * Math.PI * basePairs * t;
            double y = height * t;
            points.Add([radius * MathCore.Cos(angle), y, radius * MathCore.Sin(angle)]);
            points.Add([radius * MathCore.Cos(angle + Math.PI), y, radius * MathCore.Sin(angle + Math.PI)]);
        }
        return points.ToArray();
    }
    public static double[][] CreateGalaxy(double radius, int arms, int particlesPerArm, int seed = 0)
    {
        var rng = new Random(seed);
        var points = new List<double[]>();
        for (int arm = 0; arm < arms; arm++)
        {
            double armAngle = 2 * Math.PI * arm / arms;
            for (int i = 0; i < particlesPerArm; i++)
            {
                double t = (double)i / particlesPerArm;
                double angle = armAngle + t * 4 * Math.PI;
                double r = radius * t;
                double x = r * MathCore.Cos(angle);
                double z = r * MathCore.Sin(angle);
                double y = (rng.NextDouble() - 0.5) * radius * 0.1;
                points.Add([x, y, z]);
            }
        }
        return points.ToArray();
    }
    public static double[][] CreateSolarSystem(double[] planetDistances, double[] planetSizes, int segments)
    {
        var points = new List<double[]>();
        for (int i = 0; i < planetDistances.Length; i++)
        {
            points.AddRange(MatrixOps.GenerateSphere(planetSizes[i], segments / 2, segments));
        }
        return points.ToArray();
    }
    public static double[][] CreateConstellation(double[][] starPositions)
    {
        return starPositions;
    }
    public static double[][] CreateStarField(int count, double spread, int seed = 0)
    {
        var rng = new Random(seed);
        var points = new List<double[]>();
        for (int i = 0; i < count; i++)
        {
            points.Add([(rng.NextDouble() - 0.5) * spread,
                        (rng.NextDouble() - 0.5) * spread,
                        (rng.NextDouble() - 0.5) * spread]);
        }
        return points.ToArray();
    }
    public static double[][] CreateNebula(int count, double spread, int seed = 0)
    {
        return CreateStarField(count, spread, seed);
    }
    public static double[][] CreateAsteroidBelt(double innerRadius, double outerRadius, int count, int seed = 0)
    {
        var rng = new Random(seed);
        var points = new List<double[]>();
        for (int i = 0; i < count; i++)
        {
            double angle = rng.NextDouble() * 2 * Math.PI;
            double radius = innerRadius + rng.NextDouble() * (outerRadius - innerRadius);
            points.Add([radius * MathCore.Cos(angle),
                        (rng.NextDouble() - 0.5) * (outerRadius - innerRadius) * 0.1,
                        radius * MathCore.Sin(angle)]);
        }
        return points.ToArray();
    }
    public static double[][] CreateAccretionDisk(double innerRadius, double outerRadius, int segments)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= segments; i++)
        {
            double angle = 2 * Math.PI * i / segments;
            points.Add([innerRadius * MathCore.Cos(angle), 0, innerRadius * MathCore.Sin(angle)]);
            points.Add([outerRadius * MathCore.Cos(angle), 0, outerRadius * MathCore.Sin(angle)]);
        }
        return points.ToArray();
    }
    public static double[][] CreateBlackHole(double radius, int segments)
    {
        return MatrixOps.GenerateSphere(radius, segments, segments);
    }
    public static double[][] CreateWormhole(double radius, double length, int segments)
    {
        return MatrixOps.GenerateCylinder(radius, length, segments);
    }
    public static double[][] CreateSpaceStation(double radius, int modules, int segments)
    {
        var points = new List<double[]>();
        for (int i = 0; i < modules; i++)
        {
            double angle = 2 * Math.PI * i / modules;
            points.Add([radius * MathCore.Cos(angle), 0, radius * MathCore.Sin(angle)]);
        }
        return points.ToArray();
    }
    public static double[][] CreateSatellite(double bodySize, double panelWidth, double panelHeight)
    {
        var body = MatrixOps.GenerateCube(bodySize);
        var panel1 = MatrixOps.GeneratePlane(panelWidth, panelHeight, 1, 1);
        var panel2 = MatrixOps.GeneratePlane(panelWidth, panelHeight, 1, 1);
        return [..body, ..panel1, ..panel2];
    }
    public static double[][] CreateRocket(double bodyRadius, double bodyHeight, double noseHeight, int segments)
    {
        var body = MatrixOps.GenerateCylinder(bodyRadius, bodyHeight, segments);
        var nose = MatrixOps.GenerateCone(bodyRadius, noseHeight, segments);
        return [..body, ..nose];
    }
    public static double[][] CreateUFO(double radius, double height, int segments)
    {
        var saucer = MatrixOps.GenerateTorus(radius, height, segments, segments);
        var dome = MatrixOps.GenerateSphere(height, segments / 2, segments);
        return [..saucer, ..dome];
    }
    public static double[][] CreateCar(double length, double width, double height)
    {
        var body = new double[][]
        {
            [-length / 2, 0, -width / 2],
            [length / 2, 0, -width / 2],
            [length / 2, 0, width / 2],
            [-length / 2, 0, width / 2]
        };
        return Shapes.ExtrudePolygon(body, height);
    }
    public static double[][] CreateAirplane(double wingspan, double length, double height)
    {
        var wing = MatrixOps.GeneratePlane(wingspan, length / 4, 2, 2);
        var body = MatrixOps.GenerateCylinder(height / 4, length, 8);
        return [..wing, ..body];
    }
    public static double[][] CreateBoat(double length, double width, double height)
    {
        var hull = new double[][]
        {
            [-length / 2, 0, -width / 2],
            [length / 2, 0, -width / 4],
            [length / 2, 0, width / 4],
            [-length / 2, 0, width / 2]
        };
        return Shapes.ExtrudePolygon(hull, height);
    }
    public static double[][] CreateSubmarine(double length, double radius, int segments)
    {
        var body = MatrixOps.GenerateCylinder(radius, length, segments);
        var nose = MatrixOps.GenerateSphere(radius, segments / 2, segments);
        var tail = MatrixOps.GenerateCone(radius, radius, segments);
        return [..body, ..nose, ..tail];
    }
    public static double[][] CreateTrain(double length, double width, double height)
    {
        var car = new double[][]
        {
            [-length / 2, 0, -width / 2],
            [length / 2, 0, -width / 2],
            [length / 2, 0, width / 2],
            [-length / 2, 0, width / 2]
        };
        return Shapes.ExtrudePolygon(car, height);
    }
    public static double[][] CreateBicycle(double frameLength, double wheelRadius, int segments)
    {
        var frontWheel = MatrixOps.GenerateTorus(wheelRadius, wheelRadius * 0.05, segments, segments);
        var backWheel = MatrixOps.GenerateTorus(wheelRadius, wheelRadius * 0.05, segments, segments);
        return [..frontWheel, ..backWheel];
    }
    public static double[][] CreateWheel(double radius, double width, int segments)
    {
        return MatrixOps.GenerateTorus(radius, width / 2, segments, segments);
    }
    public static double[][] CreateGear(double radius, int teeth, double thickness)
    {
        var points = new List<double[]>();
        for (int i = 0; i <= teeth * 2; i++)
        {
            double angle = Math.PI * i / teeth;
            double r = i % 2 == 0 ? radius : radius * 0.9;
            points.Add([r * MathCore.Cos(angle), 0, r * MathCore.Sin(angle)]);
        }
        return Shapes.ExtrudePolygon(points.ToArray(), thickness);
    }
}
