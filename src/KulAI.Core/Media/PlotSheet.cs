using System;
using System.Collections.Generic;
using System.Linq;

namespace KulAI.Core.Media
{
    /// <summary>
    /// Harita çizimini ölçekli olarak kâğıda yerleştirip PDF'e yazar (CAD programından bağımsız).
    /// Dünya koordinatları metre: E = sağa (harita Y), N = yukarı (harita X).
    /// </summary>
    public sealed class PlotSheet
    {
        public abstract class Shape { public int Rgb; }
        public sealed class PolyShape : Shape { public List<(double E, double N)> Points = new List<(double, double)>(); public bool Closed; }
        public sealed class PointShape : Shape { public double E, N; public string Name; }
        public sealed class TextShape : Shape { public double E, N, HeightM, AngleRad; public string Text; }

        public List<Shape> Shapes { get; } = new List<Shape>();
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public bool ShowPointNames { get; set; } = true;
        /// <summary>Kenarlarda koordinat değerli ızgara artıları.</summary>
        public bool Grid { get; set; } = true;
        /// <summary>Verilirse çıktı bu alanı kapsar (ör. ekrandaki görünüm); taşan çizimler kırpılır.</summary>
        public (double MinE, double MinN, double MaxE, double MaxN)? Bounds { get; set; }

        public sealed class Result
        {
            public string Paper; public bool Landscape; public double Scale; public int ShapeCount;
            public double WidthMm, HeightMm;
        }

        private static readonly (string name, double w, double h)[] Papers =
        {
            ("A4", 210, 297), ("A3", 297, 420), ("A2", 420, 594), ("A1", 594, 841), ("A0", 841, 1189),
        };

        /// <summary>Haritacılıkta kullanılan ölçek paydaları; otomatik ölçek bunlardan seçilir.</summary>
        private static readonly double[] NiceScales =
        {
            50, 100, 200, 250, 500, 1000, 2000, 2500, 5000, 10000, 20000, 25000, 50000, 100000, 200000, 250000, 500000, 1000000,
        };

        private const double Margin = 10, TitleBlockH = 16;

        /// <param name="paper">A4..A0; null ise ölçeğe sığan en küçük kâğıt (ölçek de yoksa A4).</param>
        /// <param name="scale">Ölçek paydası (500 = 1/500); null ise kâğıda sığan en büyük standart ölçek.</param>
        /// <param name="orientation">"portrait", "landscape" ya da null (çizime göre).</param>
        public Result Save(string path, string paper, double? scale, string orientation)
        {
            if (Shapes.Count == 0) throw new InvalidOperationException("Çıktıya girecek nesne yok.");
            if (string.IsNullOrWhiteSpace(paper) || paper.Equals("auto", StringComparison.OrdinalIgnoreCase)) paper = null;
            if (string.IsNullOrWhiteSpace(orientation) || orientation.Equals("auto", StringComparison.OrdinalIgnoreCase)) orientation = null;
            if (scale.HasValue && scale.Value <= 0) scale = null;
            var all = Shapes.SelectMany(Coords).ToList();
            double minE = all.Min(p => p.E), maxE = all.Max(p => p.E), minN = all.Min(p => p.N), maxN = all.Max(p => p.N);
            if (Bounds.HasValue) (minE, minN, maxE, maxN) = Bounds.Value;
            double spanE = Math.Max(maxE - minE, 0.01), spanN = Math.Max(maxN - minN, 0.01);
            // Nokta adları ve yazılar sınırın biraz dışına taşabilir: %4 pay bırak.
            if (!Bounds.HasValue) { spanE *= 1.08; spanN *= 1.08; }

            bool? landscape = orientation == null ? (bool?)null : orientation.StartsWith("l", StringComparison.OrdinalIgnoreCase);
            bool wantLandscape = landscape ?? spanE > spanN;

            var candidates = paper == null ? Papers : Papers.Where(p => p.name.Equals(paper, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0) throw new ArgumentException($"Kâğıt boyutu tanınmadı: {paper} (A4, A3, A2, A1, A0).");

            (string name, double w, double h) chosen = default;
            double chosenScale = 0;
            foreach (var p in candidates)
            {
                double w = wantLandscape ? p.h : p.w, h = wantLandscape ? p.w : p.h;
                double drawW = w - 2 * Margin, drawH = h - 2 * Margin - TitleBlockH;
                // Bu kâğıda sığan en küçük ölçek paydası (en büyük çizim)
                double needed = Math.Max(spanE * 1000 / drawW, spanN * 1000 / drawH);
                if (scale.HasValue)
                {
                    if (scale.Value >= needed) { chosen = (p.name, w, h); chosenScale = scale.Value; break; }
                }
                else
                {
                    chosen = (p.name, w, h);
                    chosenScale = NiceScales.FirstOrDefault(s => s >= needed);
                    if (chosenScale == 0) chosenScale = Math.Ceiling(needed / 1000) * 1000;
                    break;
                }
            }
            if (chosen.name == null)
            {
                var largest = candidates.Last();
                double w = wantLandscape ? largest.h : largest.w, h = wantLandscape ? largest.w : largest.h;
                double needed = Math.Max(spanE * 1000 / (w - 2 * Margin), spanN * 1000 / (h - 2 * Margin - TitleBlockH));
                throw new ArgumentException($"Çizim 1/{scale} ölçekte {largest.name} kâğıda sığmıyor. " +
                    $"Bu kâğıtta en az 1/{Math.Ceiling(needed)} gerekir; ölçeği küçültün ya da daha büyük kâğıt seçin.");
            }

            var pdf = new PdfWriter();
            pdf.AddPage(chosen.w, chosen.h);
            double areaW = chosen.w - 2 * Margin, areaH = chosen.h - 2 * Margin - TitleBlockH;
            double ox = Margin, oy = Margin + TitleBlockH;
            double k = 1000 / chosenScale;                 // metre → mm
            double cE = (minE + maxE) / 2, cN = (minN + maxN) / 2;
            (double x, double y) Map(double e, double n) => (ox + areaW / 2 + (e - cE) * k, oy + areaH / 2 + (n - cN) * k);

            // Çerçeve ve başlık bloğu
            pdf.Rect(Margin, Margin, areaW, chosen.h - 2 * Margin, 0, 0.5);
            pdf.Line(Margin, oy, Margin + areaW, oy, 0, 0.35);
            pdf.Text(Margin + 3, Margin + TitleBlockH - 6.5, Title ?? "Çıktı", 4, 0, 0, bold: true);
            if (!string.IsNullOrEmpty(Subtitle)) pdf.Text(Margin + 3, Margin + 3.5, Subtitle, 2.2);
            var scaleText = $"Ölçek 1/{chosenScale:0}";
            pdf.Text(Margin + areaW - 4 - scaleText.Length * 2.5, Margin + TitleBlockH - 6.5, scaleText, 3.2, 0, 0, bold: true);
            var dateText = $"{chosen.name} · {DateTime.Now:dd.MM.yyyy}";
            pdf.Text(Margin + areaW - 3 - dateText.Length * 1.45, Margin + 3.5, dateText, 2.2);

            if (Grid) DrawGrid(pdf, Map, k, cE, cN, areaW, areaH, ox, oy);

            pdf.BeginClip(ox, oy, areaW, areaH);
            foreach (var s in Shapes)
            {
                int rgb = PaperColor(s.Rgb);
                switch (s)
                {
                    case PolyShape poly:
                        pdf.Polyline(poly.Points.Select(p => Map(p.E, p.N)).ToList(), poly.Closed, rgb);
                        break;
                    case PointShape pt:
                        var (x, y) = Map(pt.E, pt.N);
                        pdf.Line(x - 0.8, y, x + 0.8, y, rgb, 0.15);
                        pdf.Line(x, y - 0.8, x, y + 0.8, rgb, 0.15);
                        if (ShowPointNames && !string.IsNullOrWhiteSpace(pt.Name)) pdf.Text(x + 0.9, y + 0.6, pt.Name.Trim(), 1.4, 0, rgb);
                        break;
                    case TextShape t:
                        var (tx, ty) = Map(t.E, t.N);
                        pdf.Text(tx, ty, t.Text, Math.Max(1.2, t.HeightM * k), t.AngleRad, rgb);
                        break;
                }
            }
            pdf.EndClip();

            pdf.Save(path);
            return new Result { Paper = chosen.name, Landscape = chosen.w > chosen.h, Scale = chosenScale, ShapeCount = Shapes.Count, WidthMm = chosen.w, HeightMm = chosen.h };
        }

        private static IEnumerable<(double E, double N)> Coords(Shape s)
        {
            switch (s)
            {
                case PolyShape p: return p.Points;
                case PointShape p: return new[] { (p.E, p.N) };
                case TextShape t: return new[] { (t.E, t.N) };
                default: return Enumerable.Empty<(double, double)>();
            }
        }

        /// <summary>Beyaz/çok açık renkler beyaz kâğıtta görünmez: siyaha çevir.</summary>
        private static int PaperColor(int rgb)
        {
            int r = (rgb >> 16) & 255, g = (rgb >> 8) & 255, b = rgb & 255;
            return 0.299 * r + 0.587 * g + 0.114 * b > 200 ? 0 : rgb;
        }

        /// <summary>Yuvarlak koordinatlarda küçük artılar ve çerçeve kenarında Y/X değerleri.</summary>
        private static void DrawGrid(PdfWriter pdf, Func<double, double, (double x, double y)> map, double k,
                                     double cE, double cN, double areaW, double areaH, double ox, double oy)
        {
            // Artılar arası kâğıtta ~40-100 mm olacak yuvarlak aralık
            double stepM = new[] { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 5000, 10000 }.First(s => s * k >= 40 || s == 10000);
            double eMin = cE - areaW / 2 / k, eMax = cE + areaW / 2 / k, nMin = cN - areaH / 2 / k, nMax = cN + areaH / 2 / k;
            const int gray = 0x808080;
            for (double e = Math.Ceiling(eMin / stepM) * stepM; e <= eMax; e += stepM)
            {
                var (x, _) = map(e, cN);
                if (x - ox < 14 || ox + areaW - x < 4) continue;   // sol alt köşede X etiketiyle çakışmasın
                pdf.Text(x + 0.6, oy + 1, $"Y={e:0}", 1.6, Math.PI / 2, gray);
                for (double n = Math.Ceiling(nMin / stepM) * stepM; n <= nMax; n += stepM)
                {
                    var (gx, gy) = map(e, n);
                    // Kenardaki koordinat yazılarının üstüne binmesin: sol ve alt kenardan uzak dur.
                    if (gy - oy < 14 || oy + areaH - gy < 4 || gx - ox < 18) continue;
                    pdf.Line(gx - 1.5, gy, gx + 1.5, gy, gray, 0.12);
                    pdf.Line(gx, gy - 1.5, gx, gy + 1.5, gray, 0.12);
                }
            }
            for (double n = Math.Ceiling(nMin / stepM) * stepM; n <= nMax; n += stepM)
            {
                var (_, y) = map(cE, n);
                if (y - oy < 14 || oy + areaH - y < 4) continue;   // alttaki dikey Y etiketleriyle çakışmasın
                pdf.Text(ox + 1, y + 0.6, $"X={n:0}", 1.6, 0, gray);
            }
        }
    }
}
