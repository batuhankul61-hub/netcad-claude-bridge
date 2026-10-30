using System;
using System.Collections.Generic;
using System.Linq;
using Netcad.Interop;
using Netcad.Interop.NMath;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    /// <summary>Var olan yazıları toplu düzenler: yükseklik (mutlak, çarpan ya da otomatik), açı.</summary>
    public sealed class EditTextsTool : NetcadTool
    {
        public override string Name => "edit_texts";
        public override string Description =>
            "Var olan yazıların yüksekliğini ve/veya açısını değiştirir ('yazıları küçült', 'etiketler çok büyük', " +
            "'yazı boyunu 0.5 yap'). Yazılar katmana, sıra numarasına ya da metne göre seçilir; hiçbiri verilmezse tüm yazılar. " +
            "height = yeni yükseklik (m), factor = çarpan (0.2 = beşte biri), auto = çizim boyutuna uygun yükseklik. " +
            "Önce find_objects ile yazıların şimdiki 'height' değerine bak.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""layer"":          { ""type"": ""string"", ""description"": ""Katman adı ya da kalıp (* joker)"" },
    ""object_indices"": { ""type"": ""array"", ""items"": { ""type"": ""integer"" } },
    ""text_pattern"":   { ""type"": ""string"", ""description"": ""Yazı metni kalıbı (* joker), ör. DIS_DUVAR*"" },
    ""height"":         { ""type"": ""number"", ""description"": ""Yeni yükseklik, m"" },
    ""factor"":         { ""type"": ""number"", ""description"": ""Yükseklik çarpanı"" },
    ""auto"":           { ""type"": ""boolean"", ""description"": ""Çizim boyutuna uygun yüksekliğe getir"" },
    ""angle"":          { ""type"": ""number"", ""description"": ""Yeni açı, derece"" }
  }
}";

        private static List<Objects.Item> Targets(JObject args)
        {
            var layerPattern = Str(args, "layer");
            var textPattern = Str(args, "text_pattern");
            var indices = (args["object_indices"] as JArray)?.Select(t => (int)t).ToList();
            var layerNames = Objects.LayerNamesByIndex();
            return Objects.All().Where(o =>
            {
                var b = Objects.Base(o.Obj);
                if (b.Tag != Cases.Text) return false;
                if (indices != null && indices.Count > 0 && !indices.Contains(o.Index)) return false;
                if (!string.IsNullOrWhiteSpace(layerPattern) &&
                    !(layerNames.TryGetValue(b.Layer, out var ln) && Layers.Matches(ln, layerPattern))) return false;
                if (!string.IsNullOrWhiteSpace(textPattern) &&
                    !Layers.Matches(((INetcadTextObject)((INetcadObjectEx)o.Obj).O).S ?? "", textPattern)) return false;
                return true;
            }).ToList();
        }

        public override string ConfirmationFor(JObject args, int threshold)
        {
            var n = Targets(args).Count;
            return n > threshold ? $"{n} yazı değiştirilecek." : null;
        }

        public override string Execute(JObject args)
        {
            bool hasHeight = args["height"] != null && args["height"].Type != JTokenType.Null;
            bool hasFactor = args["factor"] != null && args["factor"].Type != JTokenType.Null;
            bool auto = Bool(args, "auto", false);
            bool hasAngle = args["angle"] != null && args["angle"].Type != JTokenType.Null;
            if (!hasHeight && !hasFactor && !auto && !hasAngle)
                throw new ArgumentException("height, factor, auto ya da angle'dan en az biri gerekli.");

            var targets = Targets(args);
            if (targets.Count == 0) throw new ArgumentException("Koşula uyan yazı bulunamadı.");
            double autoHeight = AutoTextHeight();

            var before = new List<double>();
            var after = new List<double>();
            foreach (var item in targets)
            {
                var obj = item.Obj;
                double old = obj.Height;
                double h = hasHeight ? Num(args, "height") : hasFactor ? old * Num(args, "factor") : auto ? autoHeight : old;
                if (h <= 0) throw new ArgumentException("Yükseklik sıfırdan büyük olmalı.");
                obj.Height = h;
                if (hasAngle) obj.Angle = Num(args, "angle") * Math.PI / 180.0;
                Objects.Put(item.Index, obj);
                before.Add(old);
                after.Add(obj.Height);
            }

            return Ok(new
            {
                changed = targets.Count,
                height_before = new { min = R(before.Min()), max = R(before.Max()) },
                height_after = new { min = R(after.Min()), max = R(after.Max()) },
            });
        }
    }
}
