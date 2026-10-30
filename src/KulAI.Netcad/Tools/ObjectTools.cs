using System;
using System.Collections.Generic;
using System.Linq;
using Netcad.Interop;
using Netcad.Interop.NcApi;
using Netcad.Interop.NMath;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    /// <summary>NETCAD nesnelerini okur ve modele uygun, kısa bir biçime çevirir.</summary>
    internal static class Objects
    {
        /// <summary>Modelin kullandığı tür adları ↔ NETCAD nesne türleri.</summary>
        public static readonly Dictionary<string, Cases> TypeNames = new Dictionary<string, Cases>(StringComparer.OrdinalIgnoreCase)
        {
            ["point"] = Cases.Point, ["line"] = Cases.Line, ["polyline"] = Cases.PLine, ["area"] = Cases.Area,
            ["text"] = Cases.Text, ["circle"] = Cases.Circle, ["arc"] = Cases.Arc, ["block"] = Cases.Block,
            ["symbol"] = Cases.Shape, ["rectangle"] = Cases.Rectangle,
        };

        public static string TypeName(Cases tag) =>
            TypeNames.FirstOrDefault(kv => kv.Value == tag).Key ?? tag.ToString().ToLowerInvariant();

        /// <summary>Katman numarası → adı.</summary>
        public static Dictionary<int, string> LayerNamesByIndex()
        {
            var lm = Layers.Manager;
            var map = new Dictionary<int, string>();
            foreach (var name in Layers.Names())
            {
                int index = lm.Find(new DelphiString(name), IntPtr.Zero);
                if (index >= 0 && !map.ContainsKey(index)) map[index] = name;
            }
            return map;
        }

        /// <summary>Projedeki bir nesne ve proje listesindeki sırası (NETCAD nesnenin kendi Index'ini -1 bırakabiliyor).</summary>
        public sealed class Item
        {
            public int Index;
            public NetcadObjectEx Obj;
        }

        private static global::Netcad.EnterpriseServices.Interop.INetcadObjectBuffer Buffer =>
            NetcadApplication.Projects.ActiveProject ?? throw new InvalidOperationException("Açık bir NETCAD projesi yok.");

        /// <summary>Açık projedeki tüm (silinmemiş) nesneler, sıra numaralarıyla.</summary>
        public static IEnumerable<Item> All()
        {
            var buffer = Buffer;
            int count = buffer.GetNumObject(0);
            for (int i = 0; i < count; i++)
            {
                var obj = default(NetcadObjectEx);
                buffer.GetObject(i, ref obj);
                if (obj.IsEmpty) continue;
                if (Base(obj).Tag == Cases.Deleted) continue;
                yield return new Item { Index = i, Obj = obj };
            }
        }

        /// <summary>Değiştirilmiş nesneyi proje listesindeki yerine yazar.</summary>
        public static void Put(int index, NetcadObjectEx obj) => Buffer.PutObject(index, ref obj.O);

        /// <summary>Proje listesinden nesne siler (büyükten küçüğe, sıra numaraları kaymasın).</summary>
        public static int Delete(IEnumerable<int> indices)
        {
            var buffer = Buffer;
            int deleted = 0;
            foreach (var i in indices.Distinct().OrderByDescending(i => i)) { buffer.DelObject(i); deleted++; }
            return deleted;
        }

        /// <summary>Ekranda seçilen nesneleri proje listesindeki karşılıklarıyla eşleştirir (tür + katman + ilk köşe).</summary>
        public static List<Item> MatchToProject(IEnumerable<NetcadObjectEx> selected)
        {
            var all = All().ToList();
            var result = new List<Item>();
            foreach (var s in selected)
            {
                var sb = Base(s);
                var sv = Vertices(s).FirstOrDefault();
                var match = all.FirstOrDefault(a =>
                {
                    var ab = Base(a.Obj);
                    if (ab.Tag != sb.Tag || ab.Layer != sb.Layer) return false;
                    var av = Vertices(a.Obj).FirstOrDefault();
                    return av != null && sv != null && Math.Abs(av[0] - sv[0]) < 1e-4 && Math.Abs(av[1] - sv[1]) < 1e-4;
                });
                if (match != null && !result.Any(r => r.Index == match.Index)) result.Add(match);
            }
            return result;
        }

        public static INetcadObjectBase Base(NetcadObjectEx obj) => (INetcadObjectBase)((INetcadObjectEx)obj).O;

        /// <summary>Köşe koordinatları [east, north, z].</summary>
        public static List<double[]> Vertices(NetcadObjectEx obj)
        {
            var poly = obj.AsPoly();
            if (poly == null) return new List<double[]>();
            return poly.GetCoordinates(true)
                       .Select(g => (global::Netcad.Interop.Topology.ICoordinate3D)g)
                       .Select(g => new[] { Math.Round(g.X, 3), Math.Round(g.Y, 3), Math.Round((double)g.Z, 3) })
                       .ToList();
        }

        /// <summary>Modele gidecek özet: tür, katman, sıra no, ölçüler ve gerekirse koordinatlar.</summary>
        public static JObject Describe(Item item, Dictionary<int, string> layerNames, bool includeVertices, int maxVertices = 200)
        {
            var obj = item.Obj;
            var b = Base(obj);
            var json = new JObject
            {
                ["index"] = item.Index,
                ["type"] = TypeName(b.Tag),
                ["layer"] = layerNames.TryGetValue(b.Layer, out var ln) ? ln : b.Layer.ToString(),
            };
            var name = b.GetName();
            if (!string.IsNullOrWhiteSpace(name)) json["name"] = name.Trim();

            switch (b.Tag)
            {
                case Cases.Point:
                    var p = ((INetcadPointObject)((INetcadObjectEx)obj).O).Structure;
                    var pv = Vertices(obj).FirstOrDefault();
                    if (pv != null) json["coord"] = new JArray(pv[0], pv[1]);
                    // Kot köşe koordinatında tutuluyor; Z1 boşsa oradan oku.
                    double z = Math.Abs(p.Z1) > 1e-9 ? p.Z1 : (pv != null ? pv[2] : 0);
                    json["z"] = Math.Round(z, 3);
                    if (!string.IsNullOrWhiteSpace(p.PName)) json["name"] = p.PName.Trim();
                    if (!string.IsNullOrWhiteSpace(p.PCode)) json["code"] = p.PCode.Trim();
                    break;
                case Cases.Text:
                    var to = (INetcadTextObject)((INetcadObjectEx)obj).O;
                    json["text"] = to.S;
                    json["height"] = Math.Round(to.Sct, 3);
                    // Yazının köşe listesi yok; yerleşim noktası temel nesnenin P1'inde.
                    var tp = ((INetcadObjectEx)obj).O.P1;
                    json["coord"] = new JArray(Math.Round(tp.X, 3), Math.Round(tp.Y, 3));
                    break;
                case Cases.Circle:
                case Cases.Arc:
                    var cp = ((INetcadObjectEx)obj).O.P1;
                    json["center"] = new JArray(Math.Round(cp.X, 3), Math.Round(cp.Y, 3));
                    goto default;
                default:
                    var poly = obj.AsPoly();
                    if (poly != null)
                    {
                        bool closed = (bool)poly.IsClosed;
                        json["closed"] = closed;
                        json["length"] = Math.Round(poly.GetLength(), 3);
                        if (closed) json["area"] = Math.Round(Math.Abs(poly.GetArea()), 3);
                        if (includeVertices)
                        {
                            var vs = Vertices(obj);
                            json["vertex_count"] = vs.Count;
                            json["vertices"] = new JArray(vs.Take(maxVertices).Select(v => new JArray(v[0], v[1])));
                        }
                    }
                    break;
            }
            return json;
        }

        /// <summary>Son seçim/arama sonucu: sonraki araçlar nesneye "index" ile başvurabilsin.</summary>
        public static NetcadObjectEx ByIndex(int index)
        {
            var found = All().FirstOrDefault(o => o.Index == index)
                        ?? throw new ArgumentException($"{index} sıra numaralı nesne bulunamadı (silinmiş olabilir; find_objects ile yeniden bul).");
            return found.Obj;
        }

        public static JObject Summarize(List<Item> objects, bool includeVertices, int limit)
        {
            var layerNames = LayerNamesByIndex();
            var described = objects.Take(limit).Select(o => Describe(o, layerNames, includeVertices)).ToList();
            return new JObject
            {
                ["count"] = objects.Count,
                ["by_type"] = JObject.FromObject(objects.GroupBy(o => TypeName(Base(o.Obj).Tag)).ToDictionary(g => g.Key, g => g.Count())),
                ["total_area"] = Math.Round(described.Where(d => d["area"] != null).Sum(d => (double)d["area"]), 3),
                ["total_length"] = Math.Round(described.Where(d => d["length"] != null).Sum(d => (double)d["length"]), 3),
                ["objects"] = new JArray(described),
                ["truncated"] = objects.Count > limit ? $"İlk {limit} nesne gösterildi; filtreyi daralt." : null,
            };
        }
    }

    public sealed class FindObjectsTool : NetcadTool
    {
        public override string Name => "find_objects";
        public override string Description =>
            "Projedeki nesneleri katmana ve/veya türe göre bulur; her birinin sıra numarasını (index), alanını, uzunluğunu, " +
            "nokta adı/kodu/kotunu, istenirse köşe koordinatlarını [east, north] verir; toplam alan ve uzunluğu hesaplar. " +
            "'Şu katmandaki alanların toplamı', 'P ile başlayan noktalar' gibi sorular için kullan.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""layer"":            { ""type"": ""string"", ""description"": ""Katman adı ya da kalıp (* joker), ör. BINA*"" },
    ""types"":            { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""point, line, polyline, area, text, circle, arc, block"" },
    ""name_pattern"":     { ""type"": ""string"", ""description"": ""Nokta/nesne adı kalıbı, ör. P*"" },
    ""include_vertices"": { ""type"": ""boolean"", ""description"": ""Çizgi/alanların köşe koordinatlarını da ver (varsayılan false)"" },
    ""limit"":            { ""type"": ""integer"", ""description"": ""En fazla kaç nesne listelensin (varsayılan 100)"" }
  }
}";
        public override bool ModifiesDrawing => false;

        public override string Execute(JObject args)
        {
            var layerPattern = Str(args, "layer");
            var types = (args["types"] as JArray)?.Select(t => (string)t).ToList();
            var namePattern = Str(args, "name_pattern");
            var layerNames = Objects.LayerNamesByIndex();

            var wanted = types?.Select(t => Objects.TypeNames.TryGetValue(t, out var c) ? (Cases?)c
                                            : throw new ArgumentException($"Bilinmeyen tür: {t}")).ToList();
            var matches = Objects.All().Where(item =>
            {
                var o = item.Obj;
                var b = Objects.Base(o);
                if (wanted != null && !wanted.Contains(b.Tag)) return false;
                if (layerPattern != null && !(layerNames.TryGetValue(b.Layer, out var ln) && Layers.Matches(ln, layerPattern))) return false;
                if (namePattern != null)
                {
                    var name = b.Tag == Cases.Point ? ((INetcadPointObject)((INetcadObjectEx)o).O).Structure.PName : b.GetName();
                    if (name == null || !Layers.Matches(name.Trim(), namePattern)) return false;
                }
                return true;
            }).ToList();

            return Objects.Summarize(matches, Bool(args, "include_vertices", false), (int)Num(args, "limit", 100))
                          .ToString(Newtonsoft.Json.Formatting.None);
        }
    }

    public sealed class SelectObjectsTool : NetcadTool
    {
        public override string Name => "select_objects";
        public override string Description =>
            "Kullanıcıdan NETCAD ekranında nesne seçmesini ister (kullanıcı tıklayıp Enter'a basar) ve seçilenlerin bilgisini " +
            "find_objects ile aynı biçimde döndürür. Kullanıcı 'seçtiğim', 'şu alan', 'bunu' gibi ifadelerle bir nesneyi kastettiğinde kullan.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""prompt"":           { ""type"": ""string"", ""description"": ""NETCAD durum çubuğunda gösterilecek mesaj, ör. 'Bölünecek parseli seçin'"" },
    ""include_vertices"": { ""type"": ""boolean"" }
  }
}";
        public override bool ModifiesDrawing => false;

        public override string Execute(JObject args)
        {
            var selection = NetcadSelectionSet.Create();
            try
            {
                selection.Select(Str(args, "prompt", "Nesneleri seçin, bitince Enter'a basın"), CaseSet.Default);
                var objects = Objects.MatchToProject(selection.ToArray().Where(o => !o.IsEmpty));
                if (objects.Count == 0)
                    return Ok(new { count = 0, note = "Kullanıcı nesne seçmedi ya da seçimi iptal etti." });
                return Objects.Summarize(objects, Bool(args, "include_vertices", true), 50).ToString(Newtonsoft.Json.Formatting.None);
            }
            finally
            {
                selection.Dispose();
            }
        }
    }

    public sealed class DeleteObjectsTool : NetcadTool
    {
        public override string Name => "delete_objects";
        public override string Description =>
            "Nesneleri siler: sıra numaralarıyla (find_objects / select_objects sonucundaki index) ya da bir katmandaki tüm nesneler. " +
            "Her zaman kullanıcı onayı istenir. Geri almak için Ctrl+Z.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""indices"": { ""type"": ""array"", ""items"": { ""type"": ""integer"" } },
    ""layer"":   { ""type"": ""string"", ""description"": ""Bu katmandaki (kalıp olabilir) tüm nesneleri sil"" }
  }
}";

        private static List<int> Targets(JObject args)
        {
            if (args["indices"] is JArray idx && idx.Count > 0)
            {
                var existing = new HashSet<int>(Objects.All().Select(o => o.Index));
                var wanted = idx.Select(t => (int)t).ToList();
                var missing = wanted.Where(i => !existing.Contains(i)).ToList();
                if (missing.Count > 0) throw new ArgumentException($"Bulunamayan sıra numaraları: {string.Join(", ", missing)}");
                return wanted;
            }
            var layer = Str(args, "layer") ?? throw new ArgumentException("'indices' ya da 'layer' gerekli.");
            var names = Objects.LayerNamesByIndex();
            return Objects.All().Where(o => names.TryGetValue(Objects.Base(o.Obj).Layer, out var n) && Layers.Matches(n, layer))
                                .Select(o => o.Index).ToList();
        }

        public override string ConfirmationFor(JObject args, int threshold)
        {
            var count = Targets(args).Count;
            return count == 0 ? null : $"{count} nesne silinecek" + (args["layer"] != null ? $" ({args["layer"]} katmanı)" : "") + ".";
        }

        public override string Execute(JObject args)
        {
            var targets = Targets(args);
            int deleted = Objects.Delete(targets);
            return Ok(new { deleted, requested = targets.Count });
        }
    }
}
