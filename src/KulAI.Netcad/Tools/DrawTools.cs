using System;
using System.Linq;
using Netcad.Interop;
using Netcad.Interop.NMath;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    // Temel çizim: çizgi, poligon/çoklu doğru, nokta, yazı, daire; ayrıca çizim bilgisi ve yakınlaştırma.

    public sealed class GetDrawingInfoTool : NetcadTool
    {
        public override string Name => "get_drawing_info";
        public override string Description =>
            "Açık projenin özetini verir: dosya adı, çizim sınırları (min/max koordinat), katman sayısı. " +
            "Mevcut çizime göre konum seçmeden önce çağır.";
        protected override string SchemaJson => @"{ ""type"": ""object"", ""properties"": {} }";
        public override bool ModifiesDrawing => false;

        public override string Execute(JObject args)
        {
            NetcadContext.Nc.NC_GetExtends(out var world, out var window);
            var names = Layers.Names();
            return Ok(new
            {
                file = NetcadContext.CurrentProject?.FileName,
                // NETCAD'de Top/Bottom yönü ekran düzenine göre değişebilir; küçük/büyük olarak ver.
                extents = world.IsValid ? new
                {
                    min_east = R(Math.Min(world.Left, world.Right)), max_east = R(Math.Max(world.Left, world.Right)),
                    min_north = R(Math.Min(world.Bottom, world.Top)), max_north = R(Math.Max(world.Bottom, world.Top)),
                } : null,
                layer_count = names.Count,
                layers_sample = names.Take(40),
            });
        }
    }

    public sealed class CreateLineTool : NetcadTool
    {
        public override string Name => "create_line";
        public override string Description => "İki nokta arasında çizgi çizer.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""start"": { ""type"": ""array"", ""items"": { ""type"": ""number"" }, ""description"": ""[east, north] = [harita Y (sağa), harita X (yukarı)]"" },
    ""end"":   { ""type"": ""array"", ""items"": { ""type"": ""number"" }, ""description"": ""[east, north] = [harita Y (sağa), harita X (yukarı)]"" },
    ""layer"": { ""type"": ""string"", ""description"": ""Katman adı (yoksa oluşturulur)"" }
  },
  ""required"": [""start"", ""end""]
}";

        public override string Execute(JObject args)
        {
            var nc = NetcadContext.Nc;
            var (x1, y1, _) = Pt(args["start"], "start");
            var (x2, y2, _) = Pt(args["end"], "end");
            int layer = EnsureLayer(Str(args, "layer"));
            var a = new Coor(x1, y1);
            var b = new Coor(x2, y2);
            var line = nc.NC_MakeLine(ref a, ref b, layer, 0, 0);
            nc.NC_AddObjectX(ref line, null);
            return Ok(new { length = R(a.Distance(b)) });
        }
    }

    public sealed class CreatePolylineTool : NetcadTool
    {
        public override string Name => "create_polyline";
        public override string Description =>
            "Noktalardan çoklu doğru (açık) ya da alan/poligon (kapalı) çizer; uzunluk ve kapalıysa alanı raporlar. " +
            "Parsel, bina, yol kenarı vb. için kullan.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""points"": { ""type"": ""array"", ""items"": { ""type"": ""array"", ""items"": { ""type"": ""number"" } }, ""description"": ""[[east, north], ...] = [[harita Y, harita X], ...], en az 2 nokta"" },
    ""closed"": { ""type"": ""boolean"", ""description"": ""Kapalı alan mı (varsayılan false)"" },
    ""name"":   { ""type"": ""string"", ""description"": ""İsteğe bağlı ad (ör. parsel no)"" },
    ""layer"":  { ""type"": ""string"" }
  },
  ""required"": [""points""]
}";

        public override string Execute(JObject args)
        {
            var pts = Pts(args["points"], "points");
            bool closed = Bool(args, "closed", false);
            if (pts.Count < (closed ? 3 : 2)) throw new ArgumentException(closed ? "Kapalı alan için en az 3 nokta gerekli." : "En az 2 nokta gerekli.");

            var nc = NetcadContext.Nc;
            var app = NetcadApplication.Current;
            int layer = EnsureLayer(Str(args, "layer"));
            var poly = app.NewPoly();
            try
            {
                foreach (var p in pts) poly.Add(p.x, p.y, (float)p.z);
                if (closed) poly.PolyChangeToClosed();
                var name = Str(args, "name", "");
                double area = closed ? Math.Abs(poly.GetArea()) : 0;
                var obj = nc.NC_MakePline(new DelphiString(name), 0, area, layer, 0, 0, poly);
                nc.NC_AddObjectX(ref obj, poly);
                return Ok(new
                {
                    vertices = pts.Count,
                    length = R(poly.GetLength()),
                    area = closed ? R(area) : (double?)null,
                    name = string.IsNullOrEmpty(name) ? null : name,
                });
            }
            finally
            {
                app.DisposePoly(poly);
            }
        }
    }

    public sealed class CreatePointTool : NetcadTool
    {
        public override string Name => "create_points";
        public override string Description =>
            "NETCAD noktaları oluşturur (aplikasyon/ölçüm noktası). Her nokta kot (z), nokta adı ve kod taşıyabilir.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""points"": {
      ""type"": ""array"",
      ""items"": {
        ""type"": ""object"",
        ""properties"": {
          ""east"":  { ""type"": ""number"", ""description"": ""Sağa koordinat = harita Y"" },
          ""north"": { ""type"": ""number"", ""description"": ""Yukarı koordinat = harita X"" },
          ""z"":     { ""type"": ""number"", ""description"": ""Kot"" },
          ""name"": { ""type"": ""string"", ""description"": ""Nokta adı/numarası"" },
          ""code"": { ""type"": ""string"", ""description"": ""Nokta kodu (ör. AGAC, BORDUR)"" }
        },
        ""required"": [""east"", ""north""]
      }
    },
    ""layer"": { ""type"": ""string"" }
  },
  ""required"": [""points""]
}";

        public override string Execute(JObject args)
        {
            if (!(args["points"] is JArray items) || items.Count == 0) throw new ArgumentException("En az bir nokta gerekli.");
            var nc = NetcadContext.Nc;
            int layer = EnsureLayer(Str(args, "layer"));
            foreach (var item in items.OfType<JObject>())
            {
                // NETCAD API: Coor.X = sağa (harita Y), Coor.Y = yukarı (harita X)
                var c = new Coor(Num(item, "east"), Num(item, "north"));
                var point = nc.NC_MakePoint(ref c, Num(item, "z", 0), new DelphiString(Str(item, "name", "")),
                                            new DelphiString(Str(item, "code", "")), layer);
                nc.NC_AddObjectX(ref point, null);
            }
            return Ok(new { created = items.Count });
        }
    }

    public sealed class CreateTextTool : NetcadTool
    {
        public override string Name => "create_text";
        public override string Description =>
            "Belirtilen noktaya yazı yazar (ada no, etiket, açıklama). Yüksekliği genelde VERME: çizimin büyüklüğüne göre " +
            "otomatik seçilir. Kullanıcı ölçü verirse ya da çizimdeki diğer yazılara uydurman gerekirse ver.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""position"": { ""type"": ""array"", ""items"": { ""type"": ""number"" }, ""description"": ""[east, north] = [harita Y (sağa), harita X (yukarı)]"" },
    ""text"":     { ""type"": ""string"" },
    ""height"":   { ""type"": ""number"", ""description"": ""Yazı yüksekliği, metre (verilmezse çizim boyutuna göre otomatik)"" },
    ""angle"":    { ""type"": ""number"", ""description"": ""Açı, derece (varsayılan 0)"" },
    ""layer"":    { ""type"": ""string"" }
  },
  ""required"": [""position"", ""text""]
}";

        public override string Execute(JObject args)
        {
            var nc = NetcadContext.Nc;
            var (x, y, _) = Pt(args["position"], "position");
            int layer = EnsureLayer(Str(args, "layer"));
            var c = new Coor(x, y);
            double height = args["height"] != null && args["height"].Type != JTokenType.Null ? Num(args, "height") : AutoTextHeight();
            var text = nc.MakeTextWide(ref c, RequiredStr(args, "text"), 0, 0, height,
                                       Num(args, "angle", 0) * Math.PI / 180.0, '\0', layer);
            nc.NC_AddObjectX(ref text, null);
            return Ok(new { done = true, height });
        }
    }

    public sealed class CreateCircleTool : NetcadTool
    {
        public override string Name => "create_circle";
        public override string Description => "Merkez ve yarıçap ile daire çizer.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""center"": { ""type"": ""array"", ""items"": { ""type"": ""number"" }, ""description"": ""[east, north] = [harita Y (sağa), harita X (yukarı)]"" },
    ""radius"": { ""type"": ""number"" },
    ""layer"":  { ""type"": ""string"" }
  },
  ""required"": [""center"", ""radius""]
}";

        public override string Execute(JObject args)
        {
            var r = Num(args, "radius");
            if (r <= 0) throw new ArgumentException("Yarıçap pozitif olmalı.");
            var nc = NetcadContext.Nc;
            var (x, y, _) = Pt(args["center"], "center");
            int layer = EnsureLayer(Str(args, "layer"));
            var c = new Coor(x, y);
            var circle = nc.NC_MakeCircle(ref c, r, layer, 0, 0);
            nc.NC_AddObjectX(ref circle, null);
            return Ok(new { area = R(Math.PI * r * r) });
        }
    }

    public sealed class ZoomExtentsTool : NetcadTool
    {
        public override string Name => "zoom_extents";
        public override string Description => "Görünümü çizimdeki tüm nesneleri gösterecek şekilde yakınlaştırır (Limit Bul).";
        protected override string SchemaJson => @"{ ""type"": ""object"", ""properties"": {} }";
        public override bool ModifiesDrawing => false;

        public override string Execute(JObject args)
        {
            NetcadContext.Nc.NC_GetExtends(out var world, out var window);
            if (!world.IsValid) return Ok(new { done = false, note = "Çizim boş." });
            NetcadContext.Nc.NC_SetCurrentWindow(world, true);
            return Ok(new { done = true });
        }
    }
}
