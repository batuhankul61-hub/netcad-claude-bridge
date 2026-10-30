using System;
using System.Collections.Generic;
using System.Linq;
using Netcad.Interop;
using Netcad.Interop.NcApi;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    // Katmanlar: listeleme, açma/kapatma, oluşturma.

    internal static class Layers
    {
        public static INetcadLayerManager Manager => NetcadContext.Nc.LayerManager(NewOne.Current);

        /// <summary>Projedeki katman adları (NETCAD'in kendi sırasıyla).</summary>
        public static List<string> Names() =>
            (Manager.ListOfLayers(false).GetString() ?? "")
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        /// <summary>'*' ve '?' joker karakterli ad eşleştirme (büyük/küçük harf duyarsız).</summary>
        public static bool Matches(string name, string pattern)
        {
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }

    public sealed class ListLayersTool : NetcadTool
    {
        public override string Name => "list_layers";
        public override string Description => "Projedeki katmanları listeler: ad, açık/kapalı, kilitli mi. Katmanlarla ilgili her işten önce çağır.";
        protected override string SchemaJson => @"{ ""type"": ""object"", ""properties"": {} }";
        public override bool ModifiesDrawing => false;

        public override string Execute(JObject args)
        {
            var lm = Layers.Manager;
            var layers = Layers.Names().Take(300).Select(n =>
            {
                var l = lm.LayerByName(n);
                return new { name = n, open = l != null && (bool)l.IsOpen, locked = l != null && (bool)l.IsLocked };
            }).ToList();
            return Ok(new { count = layers.Count, open = layers.Count(l => l.open), layers });
        }
    }

    public sealed class SetLayersVisibilityTool : NetcadTool
    {
        public override string Name => "set_layers_visibility";
        public override string Description =>
            "Katmanları açar ya da kapatır. 'layers' ile ad listesi ya da joker kalıp ('*' tümü, 'YOL*' YOL ile başlayanlar) ver; " +
            "'except' ile hariç tutulacakları belirt (ör. tümünü kapatıp birini açık bırakmak).";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""layers"":  { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Katman adları ya da kalıplar, ör. * (tümü) veya BINA, YOL*"" },
    ""visible"": { ""type"": ""boolean"", ""description"": ""true = aç, false = kapat"" },
    ""except"":  { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Dokunulmayacak katmanlar"" }
  },
  ""required"": [""layers"", ""visible""]
}";
        // Katman görünürlüğü nesne değiştirmez; geri alma bloğu gerekmez ama ekran yenilenmeli.
        public override bool ModifiesDrawing => true;

        public override string Execute(JObject args)
        {
            var patterns = (args["layers"] as JArray)?.Select(t => (string)t).ToList() ?? new List<string>();
            if (patterns.Count == 0) throw new ArgumentException("'layers' boş olamaz.");
            var except = (args["except"] as JArray)?.Select(t => (string)t).ToList() ?? new List<string>();
            bool visible = Bool(args, "visible", true);

            var lm = Layers.Manager;
            var changed = new List<string>();
            var notFound = patterns.Where(p => !p.Contains("*") && !p.Contains("?") && !Layers.Names().Any(n => n.Equals(p, StringComparison.OrdinalIgnoreCase))).ToList();

            foreach (var name in Layers.Names())
            {
                if (!patterns.Any(p => Layers.Matches(name, p)) || except.Any(p => Layers.Matches(name, p))) continue;
                var layer = lm.LayerByName(name);
                if (layer == null || (bool)layer.IsOpen == visible) continue;
                if (visible) layer.OpenLayer(); else layer.CloseLayer(-1);
                changed.Add(name);
            }

            return Ok(new
            {
                action = visible ? "açıldı" : "kapatıldı",
                changed_count = changed.Count,
                changed = changed.Take(100),
                not_found = notFound.Count > 0 ? notFound : null,
            });
        }
    }

    public sealed class CreateLayerTool : NetcadTool
    {
        public override string Name => "create_layer";
        public override string Description => "Katman oluşturur (zaten varsa dokunmaz).";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""name"":  { ""type"": ""string"" },
    ""color"": { ""type"": ""string"", ""description"": ""kırmızı, sarı, yeşil, mavi, camgöbeği, eflatun, beyaz, gri, zeytin"" }
  },
  ""required"": [""name""]
}";

        public override string Execute(JObject args)
        {
            var name = RequiredStr(args, "name");
            bool existed = Layers.Names().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            int index = EnsureLayer(name, Str(args, "color"));
            return Ok(new { layer = name, index, existed });
        }
    }
}
