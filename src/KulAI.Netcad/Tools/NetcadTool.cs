using System;
using System.Collections.Generic;
using System.Linq;
using KulAI.Core.Llm;
using Netcad.Interop;
using Netcad.Interop.NMath;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    /// <summary>
    /// Modelin çağırabileceği tek bir NETCAD işlemi. NetcadToolHost onu NETCAD'in ana iş parçacığında
    /// ve (değiştiren araçlar için) bir geri alma bloğu içinde çalıştırır.
    /// </summary>
    public abstract class NetcadTool
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        protected abstract string SchemaJson { get; }

        /// <summary>Çizimi değiştiriyor mu? Değiştirenler geri alma bloğunda çalışır ve sonunda ekran yenilenir.</summary>
        public virtual bool ModifiesDrawing => true;

        public abstract string Execute(JObject args);

        /// <summary>
        /// Çalışmadan önce kullanıcı onayı gerekiyorsa açıklama (ör. "312 nokta oluşturulacak"), gerekmiyorsa null.
        /// <paramref name="threshold"/>: kullanıcının ayarladığı "bundan fazla nesne oluşturulursa sor" sınırı.
        /// </summary>
        public virtual string ConfirmationFor(JObject args, int threshold) => null;

        /// <summary>Execute sırasında modele gösterilecek görüntüler; araç sunucusu sonuca taşır ve temizler.</summary>
        public List<KulAI.Core.Llm.ImageAttachment> Images { get; } = new List<KulAI.Core.Llm.ImageAttachment>();

        public ToolSpec ToSpec() => new ToolSpec { Name = Name, Description = Description, InputSchema = JObject.Parse(SchemaJson) };

        // ---- Parametre yardımcıları -------------------------------------------------

        protected static string Str(JObject args, string key, string fallback = null)
        {
            var v = (string)args[key];
            return string.IsNullOrWhiteSpace(v) ? fallback : v;
        }

        protected static string RequiredStr(JObject args, string key) =>
            Str(args, key) ?? throw new ArgumentException($"'{key}' parametresi gerekli.");

        protected static double Num(JObject args, string key, double? fallback = null)
        {
            var t = args[key];
            if (t == null || t.Type == JTokenType.Null)
                return fallback ?? throw new ArgumentException($"'{key}' parametresi gerekli.");
            return t.Value<double>();
        }

        protected static bool Bool(JObject args, string key, bool fallback) =>
            args[key] == null || args[key].Type == JTokenType.Null ? fallback : args[key].Value<bool>();

        /// <summary>
        /// [east, north] ya da [east, north, z] dizisini noktaya çevirir. NETCAD API'sinde Coor.X sağa (harita Y'si),
        /// Coor.Y yukarıdır (harita X'i); harita düzeniyle karışmasın diye araçlarda x/y adı kullanılmaz.
        /// </summary>
        protected static (double x, double y, double z) Pt(JToken t, string what = "nokta")
        {
            if (t is JArray a && a.Count >= 2)
                return (a[0].Value<double>(), a[1].Value<double>(), a.Count > 2 ? a[2].Value<double>() : 0);
            throw new ArgumentException($"Geçersiz {what}: [east, north] ya da [east, north, z] bekleniyor (east = harita Y, north = harita X).");
        }

        protected static List<(double x, double y, double z)> Pts(JToken t, string what = "noktalar")
        {
            if (!(t is JArray a)) throw new ArgumentException($"'{what}' bir nokta dizisi olmalı.");
            return a.Select(p => Pt(p)).ToList();
        }

        /// <summary>
        /// Çizimin büyüklüğüne uygun yazı yüksekliği (m): en uzun kenarın ~1/120'si, yuvarlak bir değere çekilmiş.
        /// 30 m'lik bir yapıda 0.25 m, 300 m'lik bir adada 2.5 m. Çizim boşsa 1 m.
        /// </summary>
        public static double AutoTextHeight()
        {
            try
            {
                NetcadContext.Nc.NC_GetExtends(out var world, out var _);
                if (!world.IsValid) return 1;
                double span = Math.Max(Math.Abs(world.Right - world.Left), Math.Abs(world.Top - world.Bottom));
                if (span <= 0) return 1;
                double raw = span / 120;
                var nice = new[] { 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.4, 0.5, 0.75, 1, 1.5, 2, 2.5, 3, 4, 5, 7.5, 10 };
                return nice.OrderBy(n => Math.Abs(n - raw)).First();
            }
            catch { return 1; }
        }

        protected static string Ok(object payload) => JObject.FromObject(payload).ToString(Newtonsoft.Json.Formatting.None);

        // ---- NETCAD yardımcıları ----------------------------------------------------

        private static readonly Dictionary<string, Func<NetcadColor>> ColorNames =
            new Dictionary<string, Func<NetcadColor>>(StringComparer.OrdinalIgnoreCase)
            {
                ["kırmızı"] = () => NetcadColor.Red, ["kirmizi"] = () => NetcadColor.Red, ["red"] = () => NetcadColor.Red,
                ["sarı"] = () => NetcadColor.Yellow, ["sari"] = () => NetcadColor.Yellow, ["yellow"] = () => NetcadColor.Yellow,
                ["yeşil"] = () => NetcadColor.Lime, ["yesil"] = () => NetcadColor.Lime, ["green"] = () => NetcadColor.Lime,
                ["mavi"] = () => NetcadColor.Blue, ["blue"] = () => NetcadColor.Blue,
                ["camgöbeği"] = () => NetcadColor.Aqua, ["turkuaz"] = () => NetcadColor.Aqua, ["cyan"] = () => NetcadColor.Aqua,
                ["eflatun"] = () => NetcadColor.Fuchsia, ["mor"] = () => NetcadColor.Fuchsia, ["magenta"] = () => NetcadColor.Fuchsia,
                ["beyaz"] = () => NetcadColor.White, ["white"] = () => NetcadColor.White,
                ["gri"] = () => NetcadColor.Silver, ["gray"] = () => NetcadColor.Silver,
                ["zeytin"] = () => NetcadColor.Olive, ["olive"] = () => NetcadColor.Olive,
            };

        protected static NetcadColor Color(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return NetcadColor.White;
            if (ColorNames.TryGetValue(name.Trim(), out var c)) return c();
            throw new ArgumentException($"Bilinmeyen renk: '{name}'. Kullanılabilir: kırmızı, sarı, yeşil, mavi, camgöbeği, eflatun, beyaz, gri, zeytin.");
        }

        /// <summary>Katmanı bulur, yoksa oluşturur; NETCAD katman numarasını döndürür.</summary>
        protected static int EnsureLayer(string name, string color = null)
        {
            var nc = NetcadContext.Nc;
            if (string.IsNullOrWhiteSpace(name)) name = "0";
            var lm = nc.LayerManager(global::Netcad.Interop.NcApi.NewOne.Current);
            int index = lm.Find(new DelphiString(name), IntPtr.Zero);
            if (index >= 0) return index;
            return nc.NC_CreateLayer(new DelphiString(name), Color(color), 0);
        }

        protected static double R(double v) => Math.Round(v, 3);
    }
}
