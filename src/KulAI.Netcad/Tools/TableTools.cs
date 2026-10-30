using System;
using System.Collections.Generic;
using System.Linq;
using Netcad.Interop;
using Netcad.Interop.NMath;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    public sealed class CreateTableTool : NetcadTool
    {
        public override string Name => "create_table";
        public override string Description =>
            "Çizime çizgili bir tablo çizer (koordinat tablosu, alan tablosu, aplikasyon tablosu vb.). İçeriği sen hazırla: " +
            "ör. find_objects sonucundaki noktalardan Nokta/Y/X/Z satırları. Konumu mevcut çizimin yanında boş bir yere koy.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""top_left"":      { ""type"": ""array"", ""items"": { ""type"": ""number"" }, ""description"": ""Tablonun sol üst köşesi [east, north]"" },
    ""title"":         { ""type"": ""string"", ""description"": ""İsteğe bağlı başlık"" },
    ""columns"":       { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Sütun başlıkları"" },
    ""rows"":          { ""type"": ""array"", ""items"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }, ""description"": ""Satırlar (metin olarak)"" },
    ""text_height"":   { ""type"": ""number"", ""description"": ""Yazı yüksekliği, m (verilmezse çizim boyutuna göre otomatik)"" },
    ""layer"":         { ""type"": ""string"", ""description"": ""Varsayılan TABLO"" }
  },
  ""required"": [""top_left"", ""columns"", ""rows""]
}";

        public override string ConfirmationFor(JObject args, int threshold)
        {
            int rows = (args["rows"] as JArray)?.Count ?? 0;
            return rows > threshold ? $"{rows} satırlık tablo çizilecek." : null;
        }

        public override string Execute(JObject args)
        {
            var (left, top, _) = Pt(args["top_left"], "top_left");
            var columns = (args["columns"] as JArray)?.Select(t => (string)t).ToList() ?? throw new ArgumentException("columns gerekli.");
            var rows = (args["rows"] as JArray)?.Select(r => (r as JArray)?.Select(c => (string)c ?? "").ToList() ?? new List<string>()).ToList()
                       ?? new List<List<string>>();
            if (columns.Count == 0) throw new ArgumentException("En az bir sütun gerekli.");
            var title = Str(args, "title");
            double h = args["text_height"] != null && args["text_height"].Type != JTokenType.Null ? Num(args, "text_height") : AutoTextHeight();
            double rowH = h * 2.0, pad = h * 0.5;

            // Sütun genişliği: en uzun içerik (karakter başına ~0.8h) + kenar boşluğu.
            var widths = Enumerable.Range(0, columns.Count).Select(c =>
                Math.Max(columns[c].Length, rows.Select(r => c < r.Count ? r[c].Length : 0).DefaultIfEmpty(0).Max()) * h * 0.8 + 2 * pad).ToList();
            double width = widths.Sum();

            var nc = NetcadContext.Nc;
            int layer = EnsureLayer(Str(args, "layer", "TABLO"));
            void Line(double e1, double n1, double e2, double n2)
            {
                var a = new Coor(e1, n1);
                var b = new Coor(e2, n2);
                var o = nc.NC_MakeLine(ref a, ref b, layer, 0, 0);
                nc.NC_AddObjectX(ref o, null);
            }
            void Text(double e, double n, string s)
            {
                if (string.IsNullOrEmpty(s)) return;
                var c = new Coor(e, n);
                var o = nc.MakeTextWide(ref c, s, 0, 0, h, 0, '\0', layer);
                nc.NC_AddObjectX(ref o, null);
            }

            double y = top;
            if (title != null)
            {
                Text(left + pad, y - rowH + pad, title);
                y -= rowH;
            }
            int lineRows = rows.Count + 1; // başlık satırı + veri satırları
            double tableTop = y, tableBottom = y - lineRows * rowH;

            // Yatay çizgiler, dikey çizgiler, sonra yazılar.
            for (int r = 0; r <= lineRows; r++) Line(left, tableTop - r * rowH, left + width, tableTop - r * rowH);
            double x = left;
            Line(x, tableTop, x, tableBottom);
            foreach (var w in widths) { x += w; Line(x, tableTop, x, tableBottom); }

            for (int r = 0; r < lineRows; r++)
            {
                var cells = r == 0 ? columns : rows[r - 1];
                double cx = left;
                for (int c = 0; c < columns.Count; c++)
                {
                    Text(cx + pad, tableTop - (r + 1) * rowH + pad, c < cells.Count ? cells[c] : "");
                    cx += widths[c];
                }
            }

            return Ok(new
            {
                rows = rows.Count,
                columns = columns.Count,
                bounds = new { left = R(left), top = R(top), right = R(left + width), bottom = R(tableBottom) },
            });
        }
    }
}
