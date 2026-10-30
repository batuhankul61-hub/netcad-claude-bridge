using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Netcad.Interop;
using Netcad.Interop.NMath;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    /// <summary>
    /// Cephe yazma: çizgi/alan kenarlarının uzunluklarını kenarın ortasına, kenara paralel yazar.
    /// Kapalı alanlarda yazı dışa, açık çizgilerde sola konur; yazı hiçbir zaman baş aşağı durmaz.
    /// </summary>
    public sealed class WriteEdgeLengthsTool : NetcadTool
    {
        public override string Name => "write_edge_lengths";
        public override string Description =>
            "Cephe yazar: seçilen çizgi/alanların (parsel, bina) her kenarının uzunluğunu kenara paralel olarak yazar " +
            "(NETCAD'deki 'Cephe Yaz' işinin otomatik hali). Nesneler sıra numarasıyla (find_objects / select_objects) ya da " +
            "katmanla verilir. Yazı yüksekliği verilmezse çizime göre otomatik.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""object_indices"": { ""type"": ""array"", ""items"": { ""type"": ""integer"" } },
    ""layer"":          { ""type"": ""string"", ""description"": ""Bu katmandaki tüm çizgi/alanlar (* joker)"" },
    ""text_layer"":     { ""type"": ""string"", ""description"": ""Yazıların katmanı (varsayılan CEPHE)"" },
    ""decimals"":       { ""type"": ""integer"", ""description"": ""Ondalık hane (varsayılan 2)"" },
    ""height"":         { ""type"": ""number"", ""description"": ""Yazı yüksekliği, m (verilmezse otomatik)"" },
    ""min_length"":     { ""type"": ""number"", ""description"": ""Bundan kısa kenarlar yazılmaz, m (varsayılan 0.1)"" },
    ""inside"":         { ""type"": ""boolean"", ""description"": ""Kapalı alanlarda yazıyı içe koy (varsayılan dışa)"" }
  }
}";

        private sealed class Label { public double X, Y, Angle; public string Text; }

        private static List<(Objects.Item item, List<double[]> pts, bool closed)> Sources(JObject args)
        {
            var indices = (args["object_indices"] as JArray)?.Select(t => (int)t).ToList();
            var layerPattern = Str(args, "layer");
            if ((indices == null || indices.Count == 0) && string.IsNullOrWhiteSpace(layerPattern))
                throw new ArgumentException("object_indices ya da layer gerekli (hangi nesnelerin cephesi yazılacak?).");
            var layerNames = Objects.LayerNamesByIndex();
            var result = new List<(Objects.Item, List<double[]>, bool)>();
            foreach (var o in Objects.All())
            {
                var b = Objects.Base(o.Obj);
                if (b.Tag == Cases.Text || b.Tag == Cases.Point) continue;
                if (indices != null && indices.Count > 0) { if (!indices.Contains(o.Index)) continue; }
                else if (!(layerNames.TryGetValue(b.Layer, out var ln) && Layers.Matches(ln, layerPattern))) continue;
                var pts = Objects.Vertices(o.Obj);
                if (pts.Count < 2) continue;
                var poly = o.Obj.AsPoly();
                bool closed = poly != null && (bool)poly.IsClosed;
                // Kapalı şekillerde son nokta ilkini tekrar ediyorsa çıkar.
                if (pts.Count > 2 && Math.Abs(pts[0][0] - pts[pts.Count - 1][0]) < 1e-6 && Math.Abs(pts[0][1] - pts[pts.Count - 1][1]) < 1e-6)
                { pts.RemoveAt(pts.Count - 1); closed = true; }
                result.Add((o, pts, closed));
            }
            return result;
        }

        private static List<Label> Layout(JObject args, double h)
        {
            int decimals = (int)Num(args, "decimals", 2);
            double minLen = Num(args, "min_length", 0.1);
            bool inside = Bool(args, "inside", false);
            double gap = h * 0.5;
            var labels = new List<Label>();
            foreach (var (_, pts, closed) in Sources(args))
            {
                // Saat yönünün tersine (pozitif alan) ise dış normal kenarın sağındadır.
                double area2 = 0;
                for (int i = 0; i < pts.Count; i++) { var a = pts[i]; var b = pts[(i + 1) % pts.Count]; area2 += a[0] * b[1] - b[0] * a[1]; }
                bool ccw = area2 > 0;
                int edges = closed ? pts.Count : pts.Count - 1;
                for (int i = 0; i < edges; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                    double dx = b[0] - a[0], dy = b[1] - a[1], len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < minLen) continue;
                    double ux = dx / len, uy = dy / len;
                    // Dış normal: CCW'de sağ (uy, -ux); CW'de sol. Açık çizgide sol taraf.
                    double nx = -uy, ny = ux;
                    if (closed && ccw) { nx = uy; ny = -ux; }
                    if (closed && inside) { nx = -nx; ny = -ny; }

                    // Okunabilir açı: (-90°, 90°]
                    double ang = Math.Atan2(dy, dx);
                    if (ang > Math.PI / 2 + 1e-9) ang -= Math.PI;
                    if (ang <= -Math.PI / 2 + 1e-9) ang += Math.PI;
                    double tx = Math.Cos(ang), ty = Math.Sin(ang);     // yazı yönü
                    double upx = -ty, upy = tx;                        // yazının yukarısı

                    var text = len.ToString("F" + decimals, CultureInfo.InvariantCulture);
                    double width = text.Length * h * 0.62;              // yaklaşık yazı genişliği
                    double mx = (a[0] + b[0]) / 2, my = (a[1] + b[1]) / 2;
                    // Yazının taban çizgisi: normal yazının yukarısına bakıyorsa kenara yakın, değilse bir yazı boyu ötede.
                    bool normalUp = nx * upx + ny * upy > 0;
                    double off = normalUp ? gap : gap + h;
                    labels.Add(new Label
                    {
                        X = mx + nx * off - tx * width / 2,
                        Y = my + ny * off - ty * width / 2,
                        Angle = ang,
                        Text = text,
                    });
                }
            }
            return labels;
        }

        private static double Height(JObject args) =>
            args["height"] != null && args["height"].Type != JTokenType.Null ? Num(args, "height") : AutoTextHeight();

        public override string ConfirmationFor(JObject args, int threshold)
        {
            int n = Layout(args, 1).Count;
            return n > threshold ? $"{n} kenar uzunluğu yazılacak." : null;
        }

        public override string Execute(JObject args)
        {
            double h = Height(args);
            var labels = Layout(args, h);
            if (labels.Count == 0) throw new ArgumentException("Yazılacak kenar bulunamadı (nesneler nokta/yazı olabilir ya da kenarlar çok kısa).");
            var nc = NetcadContext.Nc;
            int layer = EnsureLayer(Str(args, "text_layer", "CEPHE"));
            foreach (var l in labels)
            {
                var c = new Coor(l.X, l.Y);
                var t = nc.MakeTextWide(ref c, l.Text, 0, 0, h, l.Angle, '\0', layer);
                nc.NC_AddObjectX(ref t, null);
            }
            return Ok(new { written = labels.Count, text_height = h, layer = Str(args, "text_layer", "CEPHE"), sample = labels.Take(6).Select(l => l.Text) });
        }
    }
}
