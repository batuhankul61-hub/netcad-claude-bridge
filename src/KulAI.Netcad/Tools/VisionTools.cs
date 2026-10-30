using System;
using System.Diagnostics;
using System.IO;
using KulAI.Core.Media;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    // Görme: modelin çizimi ve PDF belgeleri görüntü olarak görmesi. Görüntü destekli model gerekir (Gemini, Claude).

    public sealed class CaptureViewTool : NetcadTool
    {
        public override string Name => "capture_view";
        public override string Description =>
            "NETCAD penceresinin ekran görüntüsünü alıp sana gösterir; ekrandaki görünüm koordinatlarını da verir. " +
            "Kullanıcı 'ekrandakine bak', 'şu çizimde ne var', 'çizim düzgün mü' dediğinde ya da yaptığın çizimi gözle " +
            "kontrol etmek istediğinde kullan. Koordinat okumak için görüntüye değil find_objects'e güven.";
        protected override string SchemaJson => @"{ ""type"": ""object"", ""properties"": {} }";
        public override bool ModifiesDrawing => false;

        public override string Execute(JObject args)
        {
            // Yalnızca NETCAD ana formu: açılış ekranı gibi pencereleri yakalamak NETCAD'in resim bileşeninde hata çıkarıyor.
            var hwnd = WindowCapture.FindMainWindow("TNetcadForWindowsMainForm");
            if (hwnd == IntPtr.Zero) throw new InvalidOperationException("NETCAD penceresi görünür değil (simge durumunda olabilir).");
            NetcadContext.Nc.NC_GetExtends(out var world, out var window);
            Images.Add(WindowCapture.Capture(hwnd, 1600, "NETCAD ekranı"));
            return Ok(new
            {
                captured = true,
                view = window.IsValid ? new
                {
                    min_east = R(Math.Min(window.Left, window.Right)), max_east = R(Math.Max(window.Left, window.Right)),
                    min_north = R(Math.Min(window.Bottom, window.Top)), max_north = R(Math.Max(window.Bottom, window.Top)),
                } : null,
                note = "Görüntü bu mesajın ardından geliyor. Görüntüde panel ve menüler de var; çizim alanına odaklan.",
            });
        }
    }

    public sealed class ReadPdfTool : NetcadTool
    {
        public override string Name => "read_pdf";
        public override string Description =>
            "PDF belgesinin sayfalarını görüntü olarak açıp sana gösterir (tapu, imar durumu, aplikasyon krokisi, koordinat " +
            "listesi, şartname; taranmış belgeler dahil). Dosya yolu tam yol ya da Masaüstü/Belgeler/İndirilenler'deki dosya adı " +
            "olabilir. Uzun belgede önce ilk sayfalara bak, gerekirse first_page ile devam et. Okuduğun sayıları kullanıcıya " +
            "teyit ettirmeden çizime aktarma.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""path"":       { ""type"": ""string"" },
    ""first_page"": { ""type"": ""integer"", ""description"": ""1'den başlar (varsayılan 1)"" },
    ""max_pages"":  { ""type"": ""integer"", ""description"": ""Varsayılan 3, en fazla 6"" }
  },
  ""required"": [""path""]
}";
        public override bool ModifiesDrawing => false;

        public override string Execute(JObject args)
        {
            var path = TableFile.Resolve(RequiredStr(args, "path"));
            if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Yalnızca .pdf dosyaları okunur.");
            int first = (int)Num(args, "first_page", 1);
            int max = Math.Min(6, Math.Max(1, (int)Num(args, "max_pages", 3)));
            var pdf = PdfPages.Render(path, first, max);
            Images.AddRange(pdf.Images);
            int last = pdf.FirstPage + pdf.Images.Count - 1;
            return Ok(new
            {
                file = Path.GetFileName(path),
                page_count = pdf.PageCount,
                shown_pages = pdf.Images.Count == 0 ? "yok" : $"{pdf.FirstPage}-{last}",
                more_pages = last < pdf.PageCount ? $"Devamı için first_page={last + 1}" : null,
                note = "Sayfa görüntüleri bu mesajın ardından geliyor.",
            });
        }
    }
}
