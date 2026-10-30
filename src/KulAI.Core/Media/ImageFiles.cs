using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KulAI.Core.Llm;

namespace KulAI.Core.Media
{
    /// <summary>Kullanıcının eklediği resimleri (dosya ya da panodan) modele gönderilecek boyuta getirir.</summary>
    public static class ImageFiles
    {
        public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

        public static bool IsImage(string path) =>
            Extensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

        public static ImageAttachment FromFile(string path, int maxSize = 1600)
        {
            BitmapSource source;
            using (var fs = File.OpenRead(path))
                source = BitmapFrame.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return FromBitmap(source, maxSize, Path.GetFileName(path));
        }

        public static ImageAttachment FromBitmap(BitmapSource source, int maxSize = 1600, string label = null)
        {
            double scale = Math.Min(1.0, maxSize / (double)Math.Max(source.PixelWidth, source.PixelHeight));
            if (scale < 1.0) source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            // JPEG kodlayıcı için 24 bit renge çevir (saydamlık kaybolur).
            var flat = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
            var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
            encoder.Frames.Add(BitmapFrame.Create(flat));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                return new ImageAttachment { MediaType = "image/jpeg", Base64 = Convert.ToBase64String(ms.ToArray()), Label = label };
            }
        }
    }
}
