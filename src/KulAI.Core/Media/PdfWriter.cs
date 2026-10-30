using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KulAI.Core.Media
{
    /// <summary>
    /// Ek kütüphanesiz, küçük bir vektör PDF yazıcı (çizgi, çoklu çizgi, yazı). Birimler milimetre, orijin sol alt.
    /// Yazı tipi PDF'in yerleşik Helvetica'sı; Türkçe harfler (ğ, ş, ı, İ…) kodlama farkları tablosuyla eklenir.
    /// </summary>
    public sealed class PdfWriter
    {
        private const double PtPerMm = 72.0 / 25.4;
        private readonly List<(double w, double h, StringBuilder content)> _pages = new List<(double, double, StringBuilder)>();
        private StringBuilder _c;
        private string _lastStroke, _lastWidth;

        public void AddPage(double widthMm, double heightMm)
        {
            _c = new StringBuilder();
            _lastStroke = _lastWidth = null;
            _pages.Add((widthMm, heightMm, _c));
        }

        private static string F(double v) => (v * PtPerMm).ToString("0.###", CultureInfo.InvariantCulture);
        private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private void Stroke(int rgb, double widthMm)
        {
            var color = $"{N(((rgb >> 16) & 255) / 255.0)} {N(((rgb >> 8) & 255) / 255.0)} {N((rgb & 255) / 255.0)}";
            if (color != _lastStroke) { _c.Append(color).Append(" RG ").Append(color).Append(" rg\n"); _lastStroke = color; }
            var width = F(widthMm);
            if (width != _lastWidth) { _c.Append(width).Append(" w\n"); _lastWidth = width; }
        }

        public void Polyline(IList<(double x, double y)> pts, bool closed, int rgb = 0, double widthMm = 0.18)
        {
            if (pts == null || pts.Count < 2) return;
            Stroke(rgb, widthMm);
            _c.Append(F(pts[0].x)).Append(' ').Append(F(pts[0].y)).Append(" m\n");
            for (int i = 1; i < pts.Count; i++) _c.Append(F(pts[i].x)).Append(' ').Append(F(pts[i].y)).Append(" l\n");
            _c.Append(closed ? "s\n" : "S\n");
        }

        /// <summary>Sonraki çizimleri dikdörtgenin içiyle sınırlar (EndClip'e kadar).</summary>
        public void BeginClip(double x, double y, double w, double h)
        {
            _c.Append("q ").Append(F(x)).Append(' ').Append(F(y)).Append(' ').Append(F(w)).Append(' ').Append(F(h)).Append(" re W n\n");
        }

        public void EndClip()
        {
            _c.Append("Q\n");
            _lastStroke = _lastWidth = null;   // grafik durumu geri yüklendi
        }

        public void Line(double x1, double y1, double x2, double y2, int rgb = 0, double widthMm = 0.18) =>
            Polyline(new[] { (x1, y1), (x2, y2) }, false, rgb, widthMm);

        public void Rect(double x, double y, double w, double h, int rgb = 0, double widthMm = 0.35) =>
            Polyline(new[] { (x, y), (x + w, y), (x + w, y + h), (x, y + h) }, true, rgb, widthMm);

        /// <param name="sizeMm">Büyük harf yüksekliğine yakın yazı boyu (mm).</param>
        /// <param name="angleRad">Saat yönünün tersine açı (radyan).</param>
        public void Text(double x, double y, string text, double sizeMm, double angleRad = 0, int rgb = 0, bool bold = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            Stroke(rgb, 0.1);
            double cos = Math.Cos(angleRad), sin = Math.Sin(angleRad);
            // Helvetica'da büyük harf yüksekliği yazı boyunun ~0.72'si: verilen yükseklik büyük harf boyu olsun.
            double fontPt = sizeMm / 0.72 * PtPerMm;
            _c.Append("BT /").Append(bold ? "F2 " : "F1 ").Append(fontPt.ToString("0.##", CultureInfo.InvariantCulture)).Append(" Tf ")
              .Append($"{N(cos)} {N(sin)} {N(-sin)} {N(cos)} {F(x)} {F(y)} Tm <").Append(Encode(text)).Append("> Tj ET\n");
        }

        // Windows-1252'de olmayan Türkçe harfler için kullanılmayan/az kullanılan kodlar.
        private static readonly Dictionary<char, byte> Turkish = new Dictionary<char, byte>
        {
            ['Ğ'] = 0x81, ['ğ'] = 0x8D, ['İ'] = 0x8F, ['ı'] = 0x90, ['Ş'] = 0x9D, ['ş'] = 0xA4,
        };
        private const string Differences = "[129 /Gbreve 141 /gbreve 143 /Idotaccent 144 /dotlessi 157 /Scedilla 164 /scedilla]";
        private static readonly Encoding Cp1252 = Encoding.GetEncoding(1252, new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?"));

        private static string Encode(string s)
        {
            var hex = new StringBuilder(s.Length * 2);
            foreach (var ch in s)
            {
                byte b = Turkish.TryGetValue(ch, out var t) ? t : Cp1252.GetBytes(ch.ToString())[0];
                hex.Append(b.ToString("X2"));
            }
            return hex.ToString();
        }

        public void Save(string path)
        {
            if (_pages.Count == 0) throw new InvalidOperationException("PDF'te sayfa yok.");
            var objects = new List<string>();
            int Add(string body) { objects.Add(body); return objects.Count; }

            int catalog = Add(null), pagesObj = Add(null);
            var enc = $"<< /Type /Encoding /BaseEncoding /WinAnsiEncoding /Differences {Differences} >>";
            int font1 = Add($"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding {enc} >>");
            int font2 = Add($"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding {enc} >>");
            var kids = new List<int>();
            foreach (var (w, h, content) in _pages)
            {
                var data = content.ToString();
                int stream = Add($"<< /Length {Encoding.ASCII.GetByteCount(data)} >>\nstream\n{data}endstream");
                kids.Add(Add($"<< /Type /Page /Parent {pagesObj} 0 R /MediaBox [0 0 {F(w)} {F(h)}] " +
                             $"/Resources << /Font << /F1 {font1} 0 R /F2 {font2} 0 R >> >> /Contents {stream} 0 R >>"));
            }
            objects[catalog - 1] = $"<< /Type /Catalog /Pages {pagesObj} 0 R >>";
            objects[pagesObj - 1] = $"<< /Type /Pages /Kids [{string.Join(" ", kids.ConvertAll(k => k + " 0 R"))}] /Count {kids.Count} >>";

            using (var ms = new MemoryStream())
            {
                var ascii = Encoding.ASCII;
                void W(string s) { var b = ascii.GetBytes(s); ms.Write(b, 0, b.Length); }
                W("%PDF-1.4\n");
                var offsets = new List<long>();
                for (int i = 0; i < objects.Count; i++) { offsets.Add(ms.Position); W($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
                long xref = ms.Position;
                W($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
                foreach (var o in offsets) W(o.ToString("D10") + " 00000 n \n");
                W($"trailer\n<< /Size {objects.Count + 1} /Root {catalog} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
                File.WriteAllBytes(path, ms.ToArray());
            }
        }
    }
}
