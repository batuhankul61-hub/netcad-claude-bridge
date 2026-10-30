using System;
using System.Collections.Generic;
using System.Linq;
using Netcad.Interop;
using Netcad.Interop.NcApi;
using Netcad.Interop.NMath;
using Netcad.Interop.Projection;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    // Koordinat sistemleri ve dönüşüm: NETCAD'in kendi projeksiyon motoru ve EPSG tanımları kullanılır.
    // Datum dönüşüm parametreleri tahmin edilmez; sonuçlar kontrol noktasıyla doğrulanmalıdır.

    internal static class Projections
    {
        public static INetcadProjectionSystem System =>
            NetcadApplication.Current.ProjectionSystem ?? throw new InvalidOperationException("NETCAD projeksiyon sistemine erişilemedi.");

        private static List<(int code, string name, string type)> _crs;

        /// <summary>
        /// NETCAD'in EPSG veritabanındaki (Netcad.Epsg) projeksiyonlu ve coğrafi koordinat sistemleri.
        /// Not: INetcadEPSGSupport.Supported_EPSG_Codes NETCAD 8.5'te geri dönmüyor (kilitleniyor); kullanılmaz.
        /// </summary>
        public static List<(int code, string name, string type)> Supported()
        {
            if (_crs != null) return _crs;
            var table = global::Netcad.EPSG.Data.EpsgCoordinateReferenceSystemTable.Current;
            _crs = table.Rows
                .Where(r => !r.Deprecated && (r.TypeName == "projected" || r.TypeName == "geographic 2D"))
                .Select(r => (r.Code, r.Name, r.TypeName))
                .OrderBy(r => r.Code)
                .ToList();
            return _crs;
        }

        public static TProjectionRecord FromEpsg(int epsg)
        {
            if (!(bool)System.EPSGSupport.EPSG_2_Netcad(epsg, out var record, null))
                throw new ArgumentException($"EPSG:{epsg} NETCAD tarafından tanınmıyor. find_coordinate_system ile doğru kodu bul.");
            return record;
        }
    }

    public sealed class FindCoordinateSystemTool : NetcadTool
    {
        public override string Name => "find_coordinate_system";
        public override string Description =>
            "NETCAD'in desteklediği koordinat sistemlerinde arama yapar ve EPSG kodlarını verir. transform_coordinates'ten önce " +
            "kodu buradan bul; EPSG kodunu ezberden yazma. Ör. arama: 'TM30', 'ED50', 'TUREF', 'UTM 35', 'WGS 84'.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""query"": { ""type"": ""string"", ""description"": ""Ad parçaları (boşlukla ayrılmış, hepsi geçmeli) ya da EPSG kodu"" },
    ""limit"": { ""type"": ""integer"", ""description"": ""Varsayılan 25"" }
  },
  ""required"": [""query""]
}";
        public override bool ModifiesDrawing => false;

        public override string Execute(JObject args)
        {
            var words = RequiredStr(args, "query").Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var all = Projections.Supported();
            var hits = all.Where(s => words.All(w =>
                    s.name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || s.code.ToString() == w))
                .Take((int)Num(args, "limit", 25))
                .Select(s => new { epsg = s.code, name = s.name, type = s.type }).ToList();
            return Ok(new { supported_total = all.Count, matches = hits.Count, systems = hits });
        }
    }

    public sealed class TransformCoordinatesTool : NetcadTool
    {
        public override string Name => "transform_coordinates";
        public override string Description =>
            "Noktaları bir koordinat sisteminden diğerine dönüştürür (ör. ED50 TM30 → ITRF96/TUREF TM30), NETCAD'in kendi " +
            "projeksiyon motoru ve parametreleriyle. EPSG kodlarını find_coordinate_system ile bul. Noktalar [east, north] ya da " +
            "[east, north, z] (harita Y, harita X, kot); coğrafi sistemde (ör. 4326) [boylam, enlem] derece. Sonucu kullanıcıya verirken bilinen bir kontrol noktasıyla doğrulamasını öner.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""from_epsg"": { ""type"": ""integer"" },
    ""to_epsg"":   { ""type"": ""integer"" },
    ""points"":    { ""type"": ""array"", ""items"": { ""type"": ""array"", ""items"": { ""type"": ""number"" } } }
  },
  ""required"": [""from_epsg"", ""to_epsg"", ""points""]
}";
        public override bool ModifiesDrawing => false;

        public override string Execute(JObject args)
        {
            int from = (int)Num(args, "from_epsg"), to = (int)Num(args, "to_epsg");
            var pts = Pts(args["points"], "points");
            if (pts.Count == 0) throw new ArgumentException("En az bir nokta gerekli.");

            var converter = Projections.System.ProjectionConverter(0);
            converter.InProjection = Projections.FromEpsg(from);
            converter.OutProjection = Projections.FromEpsg(to);
            if (!converter.BeforeTransform())
                throw new InvalidOperationException("Dönüşüm hazırlanamadı: " + converter.LastErrorMessage);
            try
            {
                // Derece cinsinden çıktıda 3 hane ~100 m eder; coğrafi sistemlerde 9 hane (~0.1 mm) ver.
                bool geographic = Projections.Supported().Any(s => s.code == to && s.type.StartsWith("geographic"));
                double Round(double v) => geographic ? Math.Round(v, 9) : R(v);
                var output = new List<double[]>();
                foreach (var p in pts)
                {
                    var cin = new Coor3D(p.x, p.y, p.z);
                    var cout = default(Coor3D);
                    if (!converter._Forward(ref cin, ref cout))
                        throw new InvalidOperationException($"Nokta dönüştürülemedi ({p.x}, {p.y}): {converter.LastErrorMessage}");
                    var c3 = (global::Netcad.Interop.Topology.ICoordinate3D)cout;
                    output.Add(new[] { Round(c3.X), Round(c3.Y), R((double)c3.Z) });
                }
                return Ok(new
                {
                    from_epsg = from,
                    to_epsg = to,
                    points = output,
                    axis_order = geographic ? "[boylam, enlem, z] (derece)" : "[east, north, z] = [harita Y, harita X, kot]",
                    note = "NETCAD'in projeksiyon parametreleriyle dönüştürüldü. Datum değişikliği varsa (ör. ED50 → ITRF96) " +
                           "sonucu resmi parametrelerle ya da bilinen bir kontrol noktasıyla doğrulayın.",
                });
            }
            finally
            {
                converter.AfterTransform();
            }
        }
    }

    public sealed class ProjectProjectionTool : NetcadTool
    {
        public override string Name => "project_projection";
        public override string Description =>
            "Açık projenin projeksiyon ayarını okur ya da değiştirir (NETCAD Proje Özellikleri > Projeksiyon). Proje projeksiyonu " +
            "koordinatların hangi sistemde olduğunu belirler; ikinci projeksiyon yalnızca koordinatların başka sistemde gösterilmesi içindir. " +
            "Parametresiz çağrı yalnızca okur. EPSG kodlarını find_coordinate_system ile bul. Değiştirmek nesnelerin koordinatlarını dönüştürmez.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""set_epsg"":     { ""type"": ""integer"", ""description"": ""Proje projeksiyonu, ör. 5253 = TUREF / TM27"" },
    ""display_epsg"": { ""type"": ""integer"", ""description"": ""İkinci (gösterim) projeksiyonu, ör. 4326 = WGS 84 coğrafi"" }
  }
}";

        public override string Execute(JObject args)
        {
            var project = NetcadContext.CurrentProject ?? throw new InvalidOperationException("Açık bir NETCAD projesi yok.");
            if (args["set_epsg"] != null)
            {
                var rec = Projections.FromEpsg((int)Num(args, "set_epsg"));
                project.SetProjection(ref rec);
            }
            if (args["display_epsg"] != null)
            {
                var rec = Projections.FromEpsg((int)Num(args, "display_epsg"));
                project.SetProjection2(ref rec);
            }
            if (args["set_epsg"] != null || args["display_epsg"] != null) project.Modified = true;
            return Ok(new { project = Describe(project.GetProjection()), display = Describe(project.GetProjection2()) });
        }

        private static object Describe(TProjectionRecord rec)
        {
            if (rec.IsEmpty) return null;
            var utils = Projections.System.ProjectionUtils;
            string Try(Func<string> f) { try { return f(); } catch (Exception ex) { return "? " + ex.Message; } }
            return new
            {
                type = rec.ProjTypeVal.ToString(),
                datum = rec.ProjDatum,
                zone = rec.ProjUtmZone,
                epsg = rec.EpsgCode,
                label = Try(() => utils.ProjectionTypeLabels("", ref rec)),
                proj4 = Try(() => utils.ConvertProjRec2Proj4(ref rec)),
            };
        }
    }
}
