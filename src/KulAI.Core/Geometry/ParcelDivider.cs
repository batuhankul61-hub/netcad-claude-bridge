using System;
using System.Collections.Generic;
using System.Linq;

namespace KulAI.Core.Geometry
{
    /// <summary>Düzlemde nokta (east = sağa / harita Y, north = yukarı / harita X).</summary>
    public struct P2
    {
        public double E, N;
        public P2(double e, double n) { E = e; N = n; }
        public static P2 operator -(P2 a, P2 b) => new P2(a.E - b.E, a.N - b.N);
        public static P2 operator +(P2 a, P2 b) => new P2(a.E + b.E, a.N + b.N);
        public static P2 operator *(P2 a, double k) => new P2(a.E * k, a.N * k);
        public double Dot(P2 o) => E * o.E + N * o.N;
        public double Length => Math.Sqrt(E * E + N * N);
        public P2 Unit => this * (1.0 / Length);
    }

    /// <summary>
    /// Parsel ifrazı: bir çokgeni, verilen yöndeki paralel kesim çizgileriyle istenen alanlara böler.
    /// Her kesim çizgisinin yeri, kesilen parçanın alanı hedefe eşit olana kadar ikili aramayla bulunur.
    /// Kapalı çokgen ve tek bir yarı düzlemle kırpma (Sutherland–Hodgman) kullanılır; içbükey parsellerde
    /// bir parça birden fazla bölgeye ayrılabilir, bu durumda uyarı verilir.
    /// </summary>
    public static class ParcelDivider
    {
        public static double Area(IList<P2> poly)
        {
            double s = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                s += a.E * b.N - b.E * a.N;
            }
            return Math.Abs(s) / 2;
        }

        public static bool IsConvex(IList<P2> poly)
        {
            int sign = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                var c = poly[(i + 2) % poly.Count];
                var cross = (b.E - a.E) * (c.N - b.N) - (b.N - a.N) * (c.E - b.E);
                if (Math.Abs(cross) < 1e-9) continue;
                int s = Math.Sign(cross);
                if (sign == 0) sign = s;
                else if (s != sign) return false;
            }
            return true;
        }

        /// <summary>Çokgenin { p : p·normal &lt;= t } (keepBelow) ya da &gt;= t tarafını kırpar.</summary>
        public static List<P2> ClipHalfPlane(IList<P2> poly, P2 normal, double t, bool keepBelow)
        {
            var result = new List<P2>();
            if (poly.Count == 0) return result;
            Func<P2, double> f = p => keepBelow ? t - p.Dot(normal) : p.Dot(normal) - t;
            for (int i = 0; i < poly.Count; i++)
            {
                var cur = poly[i];
                var next = poly[(i + 1) % poly.Count];
                double fc = f(cur), fn = f(next);
                if (fc >= 0) result.Add(cur);
                if ((fc >= 0) != (fn >= 0))
                {
                    double k = fc / (fc - fn);
                    result.Add(cur + (next - cur) * k);
                }
            }
            return RemoveDuplicates(result);
        }

        private static List<P2> RemoveDuplicates(List<P2> poly)
        {
            var clean = new List<P2>();
            foreach (var p in poly)
                if (clean.Count == 0 || (p - clean[clean.Count - 1]).Length > 1e-7) clean.Add(p);
            if (clean.Count > 1 && (clean[0] - clean[clean.Count - 1]).Length < 1e-7) clean.RemoveAt(clean.Count - 1);
            return clean;
        }

        public sealed class Result
        {
            public List<List<P2>> Parts { get; } = new List<List<P2>>();
            public List<double> Areas { get; } = new List<double>();
            /// <summary>Kesim çizgilerinin parsel içindeki parçaları (her biri iki uç nokta).</summary>
            public List<(P2 a, P2 b)> CutLines { get; } = new List<(P2, P2)>();
            public string Warning { get; set; }
        }

        /// <param name="poly">Parsel köşeleri (kapanış noktası tekrarlanmadan).</param>
        /// <param name="cutDirection">Kesim çizgilerinin yönü (birim olması gerekmez).</param>
        /// <param name="targetAreas">İlk parçaların alanları (m²); kalan alan son parça olur.</param>
        /// <param name="fromOppositeSide">true ise bölmeye öbür kenardan başlanır.</param>
        public static Result Divide(IList<P2> poly, P2 cutDirection, IList<double> targetAreas, bool fromOppositeSide)
        {
            if (poly.Count < 3) throw new ArgumentException("Parsel en az 3 köşeli olmalı.");
            double total = Area(poly);
            if (targetAreas.Count == 0) throw new ArgumentException("En az bir hedef alan gerekli.");
            if (targetAreas.Any(a => a <= 0)) throw new ArgumentException("Hedef alanlar pozitif olmalı.");
            if (targetAreas.Sum() >= total - 1e-6)
                throw new ArgumentException($"Hedef alanların toplamı ({targetAreas.Sum():0.##} m²) parsel alanından ({total:0.##} m²) küçük olmalı.");

            var dir = cutDirection.Unit;
            var normal = new P2(-dir.N, dir.E);
            if (fromOppositeSide) normal = normal * -1;

            double tMin = poly.Min(p => p.Dot(normal)), tMax = poly.Max(p => p.Dot(normal));
            var result = new Result();
            if (!IsConvex(poly))
                result.Warning = "Parsel içbükey: bazı parçalar birden fazla bölgeye ayrılmış olabilir; sonucu kontrol edin.";

            double previousT = tMin, cumulative = 0;
            var cuts = new List<double>();
            foreach (var target in targetAreas)
            {
                cumulative += target;
                double lo = previousT, hi = tMax;
                for (int i = 0; i < 80; i++)
                {
                    double mid = (lo + hi) / 2;
                    if (Area(ClipHalfPlane(poly, normal, mid, keepBelow: true)) < cumulative) lo = mid; else hi = mid;
                }
                cuts.Add((lo + hi) / 2);
                previousT = (lo + hi) / 2;
            }

            double from = double.NegativeInfinity;
            foreach (var t in cuts.Concat(new[] { double.PositiveInfinity }))
            {
                var part = (List<P2>)poly.ToList();
                if (!double.IsPositiveInfinity(t)) part = ClipHalfPlane(part, normal, t, keepBelow: true);
                if (!double.IsNegativeInfinity(from)) part = ClipHalfPlane(part, normal, from, keepBelow: false);
                result.Parts.Add(part);
                result.Areas.Add(Area(part));
                from = t;
            }

            // Kesim çizgisinin parsel içindeki uçları: kırpılmış parçada t çizgisi üzerindeki noktalar.
            foreach (var t in cuts)
            {
                var below = ClipHalfPlane(poly, normal, t, keepBelow: true);
                var onLine = below.Where(p => Math.Abs(p.Dot(normal) - t) < 1e-6).ToList();
                if (onLine.Count >= 2)
                {
                    var a = onLine.OrderBy(p => p.Dot(dir)).First();
                    var b = onLine.OrderBy(p => p.Dot(dir)).Last();
                    result.CutLines.Add((a, b));
                }
            }
            return result;
        }
    }
}
