using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using KulAI.Core.Media;
using Netcad.Interop;
using Netcad.Interop.NcApi;
using Netcad.Interop.NMath;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    /// <summary>
    /// Çizimi ölçekli vektör PDF'e çıkarır (kendi PDF yazıcımızla; NETCAD'in yazdırma ayarlarına dokunmaz).
    /// Çizgiler, alanlar, daireler, noktalar ve yazılar katman renkleriyle çizilir; semboller ve taramalar henüz yok.
    /// </summary>
    public sealed class ExportPdfTool : NetcadTool
    {
        public override string Name => "export_pdf";
        public override string Description =>
            "Projedeki çizimi ölçekli PDF'e çevirir (çıktı/pafta/kroki). Varsayılan: açık katmanlardaki tüm nesneler, " +
            "kâğıda sığan en büyük standart ölçek, A4. Belirli katmanlar (layers), nesneler (object_indices, find_objects'ten) " +
            "ya da yalnızca ekrandaki görünüm (area='view') çıkarılabilir. Ölçek 1/500 için scale=500. " +
            "PDF Masaüstüne kaydedilir ve açılır. Semboller, bloklar ve taramalar PDF'e girmez; bunu kullanıcıya söyle.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""path"":           { ""type"": ""string"", ""description"": ""Kayıt yolu ya da dosya adı (varsayılan Masaüstü\\<proje>_cikti.pdf)"" },
    ""layers"":         { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Yalnızca bu katmanlar (* joker). Verilmezse açık katmanlar."" },
    ""object_indices"": { ""type"": ""array"", ""items"": { ""type"": ""integer"" }, ""description"": ""Yalnızca bu nesneler"" },
    ""area"":           { ""type"": ""string"", ""enum"": [""all"", ""view""], ""description"": ""all = seçilen nesnelerin tamamı, view = ekranda görünen alan"" },
    ""scale"":          { ""type"": ""number"", ""description"": ""Ölçek paydası, ör. 500 = 1/500. Verilmezse otomatik."" },
    ""paper"":          { ""type"": ""string"", ""enum"": [""A4"", ""A3"", ""A2"", ""A1"", ""A0"", ""auto""], ""description"": ""Varsayılan: ölçek verilmişse sığan en küçük kâğıt, yoksa A4"" },
    ""orientation"":    { ""type"": ""string"", ""enum"": [""portrait"", ""landscape"", ""auto""] },
    ""title"":          { ""type"": ""string"", ""description"": ""Başlık bloğundaki başlık (varsayılan proje adı)"" },
    ""subtitle"":       { ""type"": ""string"", ""description"": ""Başlığın altındaki açıklama (ada/parsel, mahalle, hazırlayan…)"" },
    ""point_names"":    { ""type"": ""boolean"", ""description"": ""Nokta adlarını yaz (varsayılan true)"" },
    ""grid"":           { ""type"": ""boolean"", ""description"": ""Koordinat ızgarası (varsayılan true)"" },
    ""open"":           { ""type"": ""boolean"", ""description"": ""Kaydettikten sonra aç (varsayılan true)"" }
  }
}";
        public override bool ModifiesDrawing => false;

        /// <summary>Geliştirici kanalında (otomatik testler) PDF ekranda açılmasın.</summary>
        public static bool SuppressOpen { get; set; }

        public override string Execute(JObject args)
        {
            var project = Path.GetFileNameWithoutExtension(NetcadContext.CurrentProject?.FileName ?? "") ;
            if (string.IsNullOrWhiteSpace(project)) project = "NETCAD";

            // Katmanlar: ad, açık mı, renk
            var lm = Layers.Manager;
            var layerNames = Objects.LayerNamesByIndex();
            var layerPatterns = (args["layers"] as JArray)?.Select(t => (string)t).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            var layerInfo = new Dictionary<int, (bool include, int rgb)>();
            foreach (var kv in layerNames)
            {
                var layer = lm.LayerByName(kv.Value);
                bool open = layer == null || (bool)layer.IsOpen;
                bool include = layerPatterns != null && layerPatterns.Count > 0
                    ? layerPatterns.Any(p => Layers.Matches(kv.Value, p))
                    : open;
                int rgb = 0;
                try { if (layer != null) { var c = layer.Color.ToRgbColor(); rgb = (c.R << 16) | (c.G << 8) | c.B; } } catch { }
                layerInfo[kv.Key] = (include, rgb);
            }

            var indices = (args["object_indices"] as JArray)?.Select(t => (int)t).ToHashSet();
            var items = Objects.All().Where(o =>
            {
                if (indices != null && indices.Count > 0) return indices.Contains(o.Index);
                var b = Objects.Base(o.Obj);
                return !layerInfo.TryGetValue(b.Layer, out var li) || li.include;
            }).ToList();

            var sheet = new PlotSheet
            {
                Title = Str(args, "title", project),
                Subtitle = Str(args, "subtitle"),
                ShowPointNames = Bool(args, "point_names", true),
                Grid = Bool(args, "grid", true),
            };

            if (Str(args, "area", "all") == "view")
            {
                NetcadContext.Nc.NC_GetExtends(out var world, out var window);
                if (!window.IsValid) throw new InvalidOperationException("Ekran görünümü alınamadı.");
                sheet.Bounds = (Math.Min(window.Left, window.Right), Math.Min(window.Bottom, window.Top),
                                Math.Max(window.Left, window.Right), Math.Max(window.Bottom, window.Top));
            }

            int skipped = 0;
            foreach (var item in items)
            {
                var shape = ToShape(item, layerInfo);
                if (shape == null) { skipped++; continue; }
                if (sheet.Bounds.HasValue && !Intersects(shape, sheet.Bounds.Value)) continue;
                sheet.Shapes.Add(shape);
            }
            if (sheet.Shapes.Count == 0)
                throw new ArgumentException("Çıktıya girecek nesne bulunamadı (katmanlar kapalı ya da filtre boş olabilir).");

            var path = OutputPath(Str(args, "path"), project);
            double? scale = args["scale"] != null && args["scale"].Type != JTokenType.Null ? (double?)Num(args, "scale") : null;
            var result = sheet.Save(path, Str(args, "paper"), scale, Str(args, "orientation"));

            bool opened = false;
            if (Bool(args, "open", true) && !SuppressOpen)
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); opened = true; }
                catch (Exception ex) { MainModule.Log("PDF açılamadı: " + ex.Message); }
            }

            return Ok(new
            {
                file = path,
                paper = result.Paper,
                orientation = result.Landscape ? "yatay" : "dikey",
                scale = $"1/{result.Scale:0}",
                objects = result.ShapeCount,
                skipped_unsupported = skipped > 0 ? (int?)skipped : null,
                opened,
                note = skipped > 0 ? "Sembol/blok/tarama gibi bazı nesneler PDF'e çizilmedi." : null,
            });
        }

        private static PlotSheet.Shape ToShape(Objects.Item item, Dictionary<int, (bool include, int rgb)> layers)
        {
            var obj = item.Obj;
            var b = Objects.Base(obj);
            int rgb = layers.TryGetValue(b.Layer, out var li) ? li.rgb : 0;
            switch (b.Tag)
            {
                case Cases.Point:
                    var v = Objects.Vertices(obj).FirstOrDefault();
                    if (v == null) return null;
                    var p = ((INetcadPointObject)((INetcadObjectEx)obj).O).Structure;
                    return new PlotSheet.PointShape { E = v[0], N = v[1], Name = p.PName, Rgb = rgb };
                case Cases.Text:
                    var t = (INetcadTextObject)((INetcadObjectEx)obj).O;
                    var pos = ((INetcadObjectEx)obj).O.P1;
                    double angle = t.AngleT;
                    // NETCAD açıyı radyan tutar; yine de derece gelirse çevir.
                    if (Math.Abs(angle) > 2 * Math.PI + 0.01) angle *= Math.PI / 180.0;
                    return new PlotSheet.TextShape { E = pos.X, N = pos.Y, Text = t.S, HeightM = Math.Abs(t.Sct) > 0 ? t.Sct : 1, AngleRad = angle, Rgb = rgb };
                default:
                    var vs = Objects.Vertices(obj);
                    if (vs.Count < 2) return null;
                    var poly = obj.AsPoly();
                    var shape = new PlotSheet.PolyShape { Closed = poly != null && (bool)poly.IsClosed, Rgb = rgb };
                    shape.Points.AddRange(vs.Select(x => (x[0], x[1])));
                    return shape;
            }
        }

        private static bool Intersects(PlotSheet.Shape s, (double MinE, double MinN, double MaxE, double MaxN) b)
        {
            IEnumerable<(double E, double N)> pts;
            switch (s)
            {
                case PlotSheet.PolyShape p: pts = p.Points; break;
                case PlotSheet.PointShape p: pts = new[] { (p.E, p.N) }; break;
                case PlotSheet.TextShape t: pts = new[] { (t.E, t.N) }; break;
                default: return true;
            }
            var list = pts.ToList();
            double minE = list.Min(p => p.E), maxE = list.Max(p => p.E), minN = list.Min(p => p.N), maxN = list.Max(p => p.N);
            return maxE >= b.MinE && minE <= b.MaxE && maxN >= b.MinN && minN <= b.MaxN;
        }

        private static string OutputPath(string requested, string project)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string path = string.IsNullOrWhiteSpace(requested)
                ? Path.Combine(desktop, project + "_cikti.pdf")
                : Path.IsPathRooted(requested.Trim('"')) ? requested.Trim('"') : Path.Combine(desktop, requested.Trim('"'));
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) path += ".pdf";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Var olan dosyanın üzerine yazma (açık olabilir ya da kullanıcının önceki çıktısıdır).
            var dir = Path.GetDirectoryName(path); var name = Path.GetFileNameWithoutExtension(path);
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{name} ({i}).pdf");
            return path;
        }
    }
}
