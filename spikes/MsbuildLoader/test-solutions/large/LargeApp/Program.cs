using System;
using System.Collections.Generic;
using LargeLib1;
using LargeLib2;
using LargeLib3;
using LargeLib4;

namespace LargeApp;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("LargeApp starting...");
        RunMathDemos();
        RunGeometryDemos();
        RunMatrixDemos();
        RunShapeDemos();
        RunColorDemos();
        RunSceneDemos();
        RunFractalDemos();
        Console.WriteLine("LargeApp finished.");
    }

    static void RunMathDemos()
    {
        Console.WriteLine("\n=== Math Demos ===");
        Console.WriteLine($"Sqrt(16) = {MathCore.Sqrt(16)}");
        Console.WriteLine($"Pow(2, 10) = {MathCore.Pow(2, 10)}");
        Console.WriteLine($"Sin(PI/2) = {MathCore.Sin(Math.PI / 2)}");
        Console.WriteLine($"Distance2D(0,0,3,4) = {MathCore.Distance2D(0, 0, 3, 4)}");
        Console.WriteLine($"Distance3D(0,0,0,1,1,1) = {MathCore.Distance3D(0, 0, 0, 1, 1, 1)}");
        Console.WriteLine($"Lerp(0, 100, 0.5) = {MathCore.Lerp(0, 100, 0.5)}");
        Console.WriteLine($"Clamp(150, 0, 100) = {MathCore.Clamp(150, 0, 100)}");
        Console.WriteLine($"MonteCarloPi(100000) = {MathCore.MonteCarloPi(100000)}");
    }

    static void RunGeometryDemos()
    {
        Console.WriteLine("\n=== Geometry Demos ===");
        double[] a = [1, 0, 0];
        double[] b = [0, 1, 0];
        var cross = Geometry.CrossProduct(a, b);
        Console.WriteLine($"Cross([1,0,0], [0,1,0]) = [{cross[0]}, {cross[1]}, {cross[2]}]");
        Console.WriteLine($"Dot([1,0,0], [0,1,0]) = {Geometry.DotProduct(a, b)}");
        double[] v = [3, 4, 0];
        Console.WriteLine($"Length([3,4,0]) = {Geometry.VectorLength(v)}");
        double[] p = [0.5, 0.5, 0];
        double[] v0 = [0, 0, 0];
        double[] v1 = [1, 0, 0];
        double[] v2 = [0, 1, 0];
        var bc = Geometry.BarycentricCoordinates(p, v0, v1, v2);
        Console.WriteLine($"Barycentric([0.5,0.5,0], triangle) = [{bc[0]:F2}, {bc[1]:F2}, {bc[2]:F2}]");
        Console.WriteLine($"TriangleArea(unit triangle) = {Geometry.TriangleArea(v0, v1, v2)}");
        var circle = Geometry.GenerateCircle(1, 8);
        Console.WriteLine($"Circle points: {circle.Length}");
    }

    static void RunMatrixDemos()
    {
        Console.WriteLine("\n=== Matrix Demos ===");
        var eye = new double[] { 0, 0, 5 };
        var target = new double[] { 0, 0, 0 };
        var up = new double[] { 0, 1, 0 };
        var lookAt = MatrixOps.LookAtMatrix(eye, target, up);
        Console.WriteLine($"LookAt matrix computed, rows: {lookAt.Length}");
        var persp = MatrixOps.PerspectiveMatrix(Math.PI / 4, 1.0, 0.1, 100);
        Console.WriteLine($"Perspective matrix computed, rows: {persp.Length}");
        var ortho = MatrixOps.OrthographicMatrix(-1, 1, -1, 1, 0.1, 100);
        Console.WriteLine($"Orthographic matrix computed, rows: {ortho.Length}");
        var trans = MatrixOps.TranslationMatrix(1, 2, 3);
        Console.WriteLine($"Translation matrix computed, rows: {trans.Length}");
        var scale = MatrixOps.ScaleMatrix(2, 2, 2);
        Console.WriteLine($"Scale matrix computed, rows: {scale.Length}");
        var rotX = MatrixOps.RotationMatrixX(Math.PI / 4);
        Console.WriteLine($"RotationX matrix computed, rows: {rotX.Length}");
        var rotY = MatrixOps.RotationMatrixY(Math.PI / 4);
        Console.WriteLine($"RotationY matrix computed, rows: {rotY.Length}");
        var rotZ = MatrixOps.RotationMatrixZ(Math.PI / 4);
        Console.WriteLine($"RotationZ matrix computed, rows: {rotZ.Length}");
        var point = new double[] { 1, 0, 0 };
        var transformed = MatrixOps.TransformPoint(rotZ, point);
        Console.WriteLine($"TransformPoint([1,0,0], rotZ45) = [{transformed[0]:F3}, {transformed[1]:F3}, {transformed[2]:F3}]");
    }

    static void RunShapeDemos()
    {
        Console.WriteLine("\n=== Shape Demos ===");
        var heightmap = Shapes.GenerateHeightmap(32, 32, 42);
        Console.WriteLine($"Heightmap size: {heightmap.Length}x{heightmap[0].Length}");
        var normalMap = Shapes.GenerateNormalMap(heightmap, 2.0);
        Console.WriteLine($"Normal map size: {normalMap.Length}x{normalMap[0].Length}");
        var voronoi = Shapes.GenerateVoronoiDiagram(64, 64, 10, 42);
        Console.WriteLine($"Voronoi diagram points: {voronoi.Length}");
        var halton = Shapes.HaltonSequence(10, 2);
        Console.WriteLine($"Halton(10, 2) = {halton[0]:F4}");
        var sphere = Shapes.FibonacciSphere(100, 50);
        Console.WriteLine($"Fibonacci sphere point: [{sphere[0]:F3}, {sphere[1]:F3}, {sphere[2]:F3}]");
        var disk = Shapes.PoissonDiskSampling(10, 10, 1.0, 30, 42);
        Console.WriteLine($"Poisson disk samples: {disk.Length / 2}");
    }

    static void RunColorDemos()
    {
        Console.WriteLine("\n=== Color Demos ===");
        var hsv = ColorUtils.RgbToHsv(1, 0, 0);
        Console.WriteLine($"RGB(1,0,0) -> HSV: [{hsv[0]:F1}, {hsv[1]:F2}, {hsv[2]:F2}]");
        var rgb = ColorUtils.HsvToRgb(120, 1, 1);
        Console.WriteLine($"HSV(120,1,1) -> RGB: [{rgb[0]:F2}, {rgb[1]:F2}, {rgb[2]:F2}]");
        var cmyk = ColorUtils.RgbToCmyk(0.5, 0.3, 0.2);
        Console.WriteLine($"RGB(0.5,0.3,0.2) -> CMYK: [{cmyk[0]:F2}, {cmyk[1]:F2}, {cmyk[2]:F2}, {cmyk[3]:F2}]");
        var lab = ColorUtils.RgbToLab(0.5, 0.3, 0.2);
        Console.WriteLine($"RGB(0.5,0.3,0.2) -> Lab: [{lab[0]:F2}, {lab[1]:F2}, {lab[2]:F2}]");
        var gray = ColorUtils.Grayscale(0.3, 0.5, 0.2);
        Console.WriteLine($"Grayscale(0.3,0.5,0.2) = {gray[0]:F3}");
        var sepia = ColorUtils.Sepia(0.5, 0.4, 0.3);
        Console.WriteLine($"Sepia(0.5,0.4,0.3) = [{sepia[0]:F3}, {sepia[1]:F3}, {sepia[2]:F3}]");
        var temp = ColorUtils.TemperatureToRgb(6500);
        Console.WriteLine($"Temperature(6500K) = [{temp[0]:F3}, {temp[1]:F3}, {temp[2]:F3}]");
        var exposure = ColorUtils.Exposure([0.5, 0.5, 0.5], 2.0);
        Console.WriteLine($"Exposure([0.5,0.5,0.5], 2.0) = [{exposure[0]:F3}, {exposure[1]:F3}, {exposure[2]:F3}]");
        var reinhard = ColorUtils.ToneMapReinhard([2.0, 1.0, 0.5]);
        Console.WriteLine($"Reinhard([2,1,0.5]) = [{reinhard[0]:F3}, {reinhard[1]:F3}, {reinhard[2]:F3}]");
        var aces = ColorUtils.ToneMapACES([2.0, 1.0, 0.5]);
        Console.WriteLine($"ACES([2,1,0.5]) = [{aces[0]:F3}, {aces[1]:F3}, {aces[2]:F3}]");
    }

    static void RunSceneDemos()
    {
        Console.WriteLine("\n=== Scene Demos ===");
        var terrain = SceneBuilder.CreateTerrain(64, 64, 42);
        Console.WriteLine($"Terrain size: {terrain.Length}x{terrain[0].Length}");
        var building = SceneBuilder.CreateBuilding(10, 20, 10);
        Console.WriteLine($"Building vertices: {building.Length}");
        var maze = SceneBuilder.CreateMaze(10, 10, 1.0, 42);
        Console.WriteLine($"Maze walls: {maze.Length}");
        var city = SceneBuilder.CreateCityGrid(5, 5, 10, 2);
        Console.WriteLine($"City grid points: {city.Length}");
        var galaxy = SceneBuilder.CreateGalaxy(100, 4, 50, 42);
        Console.WriteLine($"Galaxy particles: {galaxy.Length}");
        var stars = SceneBuilder.CreateStarField(100, 200, 42);
        Console.WriteLine($"Star field particles: {stars.Length}");
        var asteroidBelt = SceneBuilder.CreateAsteroidBelt(50, 100, 200, 42);
        Console.WriteLine($"Asteroid belt particles: {asteroidBelt.Length}");
        var rocket = SceneBuilder.CreateRocket(1, 10, 3, 16);
        Console.WriteLine($"Rocket vertices: {rocket.Length}");
        var car = SceneBuilder.CreateCar(4, 2, 1.5);
        Console.WriteLine($"Car vertices: {car.Length}");
        var boat = SceneBuilder.CreateBoat(10, 4, 2);
        Console.WriteLine($"Boat vertices: {boat.Length}");
    }

    static void RunFractalDemos()
    {
        Console.WriteLine("\n=== Fractal Demos ===");
        var tree = Fractals.CreateFractalTree(10, Math.PI / 6, 4, 42);
        Console.WriteLine($"Fractal tree points: {tree.Length}");
        var snowflake = Fractals.CreateKochSnowflake(10, 3);
        Console.WriteLine($"Koch snowflake points: {snowflake.Length}");
        var sierpinski = Fractals.CreateSierpinskiTriangle(10, 3);
        Console.WriteLine($"Sierpinski triangle points: {sierpinski.Length}");
        var dragon = Fractals.CreateDragonCurve(10);
        Console.WriteLine($"Dragon curve points: {dragon.Length}");
        var fern = Fractals.CreateBarnsleyFern(10000);
        Console.WriteLine($"Barnsley fern points: {fern.Length}");
        var mandelbrot = Fractals.CreateMandelbrotSet(64, 64, -2, 1, -1.5, 1.5, 100);
        Console.WriteLine($"Mandelbrot set points: {mandelbrot.Length}");
        var julia = Fractals.CreateJuliaSet(64, 64, -0.8, 0.156, -2, 2, -2, 2, 100);
        Console.WriteLine($"Julia set points: {julia.Length}");
        var lsystem = Fractals.CreateLSystem("F", new Dictionary<char, string> { ['F'] = "F+F-F-F+F" }, 3, Math.PI / 2, 1);
        Console.WriteLine($"L-system points: {lsystem.Length}");
    }
}
