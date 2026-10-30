using System;
using System.Collections.Generic;
using System.Linq;
using KulAI.Core.Geometry;
using Netcad.Interop;
using Netcad.Interop.NMath;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    // Parsel ifrazı (hesap KulAI.Core.Geometry.ParcelDivider'da, NETCAD'den bağımsız test edilir).

    public sealed class DivideParcelTool : NetcadTool
    {
        public override string Name => "divide_parcel";
        public override string Description =>
            "Parsel ifrazı: kapalı bir alanı paralel kesim çizgileriyle eşit parçalara (parts) ya da belirli alanlara (areas, m²; " +
            "kalan son parça olur) böler, parçaları kapalı alan olarak çizer ve alanlarını raporlar. Parsel, find_objects / " +
            "select_objects sonucundaki 'index' ile ya da köşe koordinatlarıyla verilir. Kesim çizgileri referans kenara " +
            "dik (varsayılan; her parça bu kenardan cephe alır) ya da paralel olur. Kenar k, k. köşeden k+1. köşeye gider.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""object_index"":   { ""type"": ""integer"", ""description"": ""Bölünecek alanın sıra numarası"" },
    ""points"":         { ""type"": ""array"", ""items"": { ""type"": ""array"", ""items"": { ""type"": ""number"" } }, ""description"": ""object_index yoksa köşeler [[east, north], ...]"" },
    ""parts"":          { ""type"": ""integer"", ""description"": ""Eşit parça sayısı"" },
    ""areas"":          { ""type"": ""array"", ""items"": { ""type"": ""number"" }, ""description"": ""İlk parçaların alanları (m²)"" },
    ""reference_edge"": { ""type"": ""integer"", ""description"": ""Referans kenar (0'dan başlar, varsayılan 0)"" },
    ""cut"":            { ""type"": ""string"", ""enum"": [""perpendicular"", ""parallel""], ""description"": ""Kesimler referans kenara dik mi paralel mi"" },
    ""from_opposite_side"": { ""type"": ""boolean"", ""description"": ""Bölmeye öbür uçtan başla"" },
    ""name_prefix"":    { ""type"": ""string"", ""description"": ""Parça adları için önek, ör. 101/ → 101/1, 101/2"" },
    ""layer"":          { ""type"": ""string"", ""description"": ""Varsayılan IFRAZ"" }
  }
}";

        public override string Execute(JObject args)
        {
            List<P2> poly;
            if (args["object_index"] != null)
            {
                var obj = Objects.ByIndex((int)Num(args, "object_index"));
                var poly0 = obj.AsPoly();
                if (poly0 == null || !(bool)poly0.IsClosed) throw new ArgumentException("Seçilen nesne kapalı bir alan değil.");
                poly = Objects.Vertices(obj).Select(v => new P2(v[0], v[1])).ToList();
            }
            else
            {
                poly = Pts(args["points"], "points").Select(p => new P2(p.x, p.y)).ToList();
            }
            // Kapanış noktası tekrarlanmışsa çıkar.
            if (poly.Count > 1 && (poly[0] - poly[poly.Count - 1]).Length < 1e-6) poly.RemoveAt(poly.Count - 1);
            if (poly.Count < 3) throw new ArgumentException("Parsel en az 3 köşeli olmalı.");

            double total = ParcelDivider.Area(poly);
            List<double> targets;
            if (args["areas"] is JArray areas && areas.Count > 0)
                targets = areas.Select(a => (double)a).ToList();
            else
            {
                int parts = (int)Num(args, "parts", 2);
                if (parts < 2) throw new ArgumentException("En az 2 parça olmalı.");
                targets = Enumerable.Repeat(total / parts, parts - 1).ToList();
            }

            int k = (int)Num(args, "reference_edge", 0);
            if (k < 0 || k >= poly.Count) throw new ArgumentException($"reference_edge 0 ile {poly.Count - 1} arasında olmalı.");
            var edge = poly[(k + 1) % poly.Count] - poly[k];
            bool perpendicular = Str(args, "cut", "perpendicular") != "parallel";
            var cutDir = perpendicular ? new P2(-edge.N, edge.E) : edge;

            var result = ParcelDivider.Divide(poly, cutDir, targets, Bool(args, "from_opposite_side", false));

            var nc = NetcadContext.Nc;
            var app = NetcadApplication.Current;
            int layer = EnsureLayer(Str(args, "layer", "IFRAZ"));
            var prefix = Str(args, "name_prefix", "");
            var report = new List<object>();
            for (int i = 0; i < result.Parts.Count; i++)
            {
                var part = result.Parts[i];
                var name = prefix + (i + 1);
                var pl = app.NewPoly();
                try
                {
                    foreach (var p in part) pl.Add(p.E, p.N);
                    pl.PolyChangeToClosed();
                    var o = nc.NC_MakePline(new DelphiString(name), 0, result.Areas[i], layer, 0, 0, pl);
                    nc.NC_AddObjectX(ref o, pl);
                }
                finally { app.DisposePoly(pl); }

                // Parça adını ve alanını ağırlık merkezine yaz.
                var c = new Coor(part.Average(p => p.E), part.Average(p => p.N));
                double h = Math.Max(0.5, Math.Sqrt(result.Areas[i]) / 15);
                var label = nc.MakeTextWide(ref c, $"{name}  {result.Areas[i]:0.00} m²", 0, 0, h, 0, '\0', layer);
                nc.NC_AddObjectX(ref label, null);

                report.Add(new { name, area = R(result.Areas[i]), vertices = part.Select(p => new[] { R(p.E), R(p.N) }) });
            }

            return Ok(new
            {
                total_area = R(total),
                parts = report,
                cut_lines = result.CutLines.Select(l => new { from = new[] { R(l.a.E), R(l.a.N) }, to = new[] { R(l.b.E), R(l.b.N) }, length = R((l.b - l.a).Length) }),
                warning = result.Warning,
            });
        }
    }
}
