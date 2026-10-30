using System;
using System.IO;

namespace KulAI.Core
{
    /// <summary>
    /// Çekirdeği barındıran ürünün (Civil 3D, NETCAD…) başlangıçta verdiği bilgiler.
    /// Çekirdek hiçbir CAD programını tanımaz; ürüne özgü her şey buradan ve IToolHost'tan gelir.
    /// </summary>
    public static class AppEnvironment
    {
        /// <summary>Kullanıcıya görünen ad, ör. "Kul AI Asistan".</summary>
        public static string ProductName { get; set; } = "Kul AI Asistan";

        /// <summary>%APPDATA% altındaki ayar ve log klasörü, ör. "KulAI.Netcad".</summary>
        public static string DataFolderName { get; set; } = "KulAI";

        /// <summary>Eklenti DLL'lerinin bulunduğu klasör (Claude bileşeni buradan yüklenir).</summary>
        public static string PluginDirectory { get; set; } =
            Path.GetDirectoryName(typeof(AppEnvironment).Assembly.Location);

        /// <summary>Tanı kaydı (yeniden denemeler, model geçişleri); ürün kendi log dosyasına bağlar.</summary>
        public static Action<string> Diagnostic { get; set; }

        public static string DataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DataFolderName);
    }
}
