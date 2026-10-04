// Program.cs
//
// Dependency-free test harness for the 2-point similarity math shared with the
// plugin's GeoReferenceTransform. No test framework, no NuGet package, no
// Autodesk.* reference: just assertions that print PASS/FAIL and set the
// process exit code (0 = all pass, 1 = at least one failure).
//
// The reference values come from schema/samples/sample-lajosmizse.drawingspec.json:
//   georeference[0]: image (0,0)   -> model (0,0)
//   georeference[1]: image (200,0) -> model (100,0)
// so the raster scale must be 0.5 and the rotation must be 0.
//
// Run with:  dotnet run -c Release

using System;
using System.Collections.Generic;

namespace Helyszinrajz.Tests
{
    internal static class Program
    {
        private const double Tol = 1e-9;

        private static readonly List<string> Failures = new();

        private static int Main()
        {
            // Sample georeference points.
            var image0 = new Pt(0.0, 0.0);
            var model0 = new Pt(0.0, 0.0);
            var image1 = new Pt(200.0, 0.0);
            var model1 = new Pt(100.0, 0.0);

            PlanarSimilarity t = PlanarSimilarity.From(image0, model0, image1, model1);

            // (a) Scale and rotation from the sample points.
            AssertClose("sample scale is 0.5", t.Scale, 0.5, Tol);
            AssertClose("sample rotation is 0", t.RotationRad, 0.0, Tol);

            // (b) Round trip: each reference image point maps onto its model point.
            Pt r0 = t.ToModel(image0);
            AssertClose("round-trip P0.x", r0.X, model0.X, 1e-6);
            AssertClose("round-trip P0.y", r0.Y, model0.Y, 1e-6);

            Pt r1 = t.ToModel(image1);
            AssertClose("round-trip P1.x", r1.X, model1.X, 1e-6);
            AssertClose("round-trip P1.y", r1.Y, model1.Y, 1e-6);

            // A midpoint image coordinate maps to the scaled model coordinate.
            Pt rmid = t.ToModel(new Pt(100.0, 40.0));
            AssertClose("midpoint maps with scale 0.5 (x)", rmid.X, 50.0, 1e-6);
            AssertClose("midpoint maps with scale 0.5 (y)", rmid.Y, 20.0, 1e-6);

            // A rotated case: image +x axis mapped onto model +y axis => 90 deg,
            // and a 2x scale. Confirms rotation and scale are both recovered.
            PlanarSimilarity rot = PlanarSimilarity.From(
                new Pt(0.0, 0.0), new Pt(0.0, 0.0),
                new Pt(1.0, 0.0), new Pt(0.0, 2.0));
            AssertClose("rotated case scale is 2", rot.Scale, 2.0, 1e-9);
            AssertClose("rotated case rotation is 90 deg", rot.RotationRad, Math.PI / 2.0, 1e-9);
            Pt rr = rot.ToModel(new Pt(1.0, 0.0));
            AssertClose("rotated case maps (1,0)->(0,2) x", rr.X, 0.0, 1e-6);
            AssertClose("rotated case maps (1,0)->(0,2) y", rr.Y, 2.0, 1e-6);

            if (Failures.Count == 0)
            {
                Console.WriteLine("\nAll GeoReferenceTransform math tests passed.");
                return 0;
            }

            Console.WriteLine($"\n{Failures.Count} assertion(s) FAILED:");
            foreach (string f in Failures)
            {
                Console.WriteLine("  - " + f);
            }

            return 1;
        }

        private static void AssertClose(string name, double actual, double expected, double tol)
        {
            bool ok = Math.Abs(actual - expected) <= tol;
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}: actual={actual:0.############}, expected={expected:0.############}");
            if (!ok)
            {
                Failures.Add($"{name}: actual={actual}, expected={expected}");
            }
        }
    }
}
