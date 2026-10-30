using System;
using System.Collections.Generic;
using System.Linq;
using KulAI.Core.Geometry;

static class Program
{
    static int failures;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "GEÇTİ " : "HATALI")}  {name}: {detail}");
        if (!ok) failures++;
    }

    static List<P2> Poly(params double[] c) => Enumerable.Range(0, c.Length / 2).Select(i => new P2(c[2 * i], c[2 * i + 1])).ToList();

    static void Run(string name, List<P2> poly, P2 dir, double[] targets, bool opposite = false)
    {
        var total = ParcelDivider.Area(poly);
        var r = ParcelDivider.Divide(poly, dir, targets, opposite);
        var expected = targets.Concat(new[] { total - targets.Sum() }).ToList();
        bool ok = r.Areas.Count == expected.Count && r.Areas.Zip(expected, (a, e) => Math.Abs(a - e) < 0.01).All(x => x)
                  && Math.Abs(r.Areas.Sum() - total) < 0.01;
        Check(name, ok, $"toplam {total:0.###} → parçalar [{string.Join(", ", r.Areas.Select(a => a.ToString("0.###")))}] beklenen [{string.Join(", ", expected.Select(a => a.ToString("0.###")))}]" + (r.Warning != null ? "  ⚠ " + r.Warning : ""));
    }

    static int Main()
    {
        // 30x20 dikdörtgen (600 m²), kesimler kuzey-güney doğrultusunda → doğu-batı yönünde 3 eşit parça
        var rect = Poly(0, 0, 30, 0, 30, 20, 0, 20);
        Run("Dikdörtgen 3 eşit", rect, new P2(0, 1), new[] { 200.0, 200.0 });
        Run("Dikdörtgen 150 + 250 + kalan", rect, new P2(0, 1), new[] { 150.0, 250.0 });
        Run("Dikdörtgen öbür kenardan", rect, new P2(0, 1), new[] { 100.0 }, opposite: true);
        Run("Dikdörtgen yatay kesim", rect, new P2(1, 0), new[] { 300.0 });

        // Eğik dörtgen (gerçekçi parsel koordinatları)
        var skew = Poly(452300.12, 4512800.50, 452341.80, 4512806.10, 452338.40, 4512851.95, 452296.05, 4512843.20);
        var edge0 = skew[1] - skew[0];
        Run("Eğik parsel, 1. kenara dik, 4 eşit", skew, new P2(-edge0.N, edge0.E), Enumerable.Repeat(ParcelDivider.Area(skew) / 4, 3).ToArray());

        // Üçgen
        var tri = Poly(0, 0, 40, 0, 10, 30);
        Run("Üçgen 2 eşit", tri, new P2(0, 1), new[] { ParcelDivider.Area(tri) / 2 });

        // İçbükey L şekli: uyarı vermeli, alanlar yine tutmalı
        var ell = Poly(0, 0, 40, 0, 40, 10, 10, 10, 10, 30, 0, 30);
        Run("L şekli (içbükey) 2 eşit", ell, new P2(1, 0), new[] { ParcelDivider.Area(ell) / 2 });

        // Hatalı girdi: hedef toplamı parselden büyük
        try { ParcelDivider.Divide(rect, new P2(0, 1), new[] { 700.0 }, false); Check("Fazla alan reddi", false, "istisna bekleniyordu"); }
        catch (ArgumentException ex) { Check("Fazla alan reddi", true, ex.Message); }

        Console.WriteLine(failures == 0 ? "\nTÜM TESTLER GEÇTİ" : $"\n{failures} TEST HATALI");
        return failures == 0 ? 0 : 1;
    }
}
