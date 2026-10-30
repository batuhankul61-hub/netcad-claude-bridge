using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using KulAI.Core.Llm;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace KulAI.Core.Media
{
    /// <summary>
    /// PDF sayfalarını Windows'un yerleşik PDF motoruyla (Windows.Data.Pdf) görüntüye çevirir; model sayfaları görerek okur.
    /// Taranmış belgelerde (tapu, imar durumu, kroki) metin katmanı olmadığından görüntü yolu metin çıkarmaktan daha güvenilirdir.
    /// </summary>
    public static class PdfPages
    {
        public sealed class Result
        {
            public int PageCount { get; set; }
            public int FirstPage { get; set; }
            public List<ImageAttachment> Images { get; } = new List<ImageAttachment>();
        }

        /// <param name="firstPage">1'den başlar.</param>
        /// <param name="width">Sayfa görüntüsünün piksel genişliği.</param>
        public static Result Render(string path, int firstPage, int maxPages, int width = 1400)
        {
            // WinRT çağrıları UI iş parçacığını beklemesin diye iş parçacığı havuzunda çalışır.
            return Task.Run(() => RenderAsync(path, firstPage, maxPages, width)).GetAwaiter().GetResult();
        }

        private static async Task<Result> RenderAsync(string path, int firstPage, int maxPages, int width)
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask().ConfigureAwait(false);
            PdfDocument doc;
            try
            {
                doc = await PdfDocument.LoadFromFileAsync(file).AsTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("PDF açılamadı (bozuk ya da şifreli olabilir): " + ex.Message, ex);
            }

            var result = new Result { PageCount = (int)doc.PageCount, FirstPage = Math.Max(1, firstPage) };
            int last = Math.Min(result.PageCount, result.FirstPage + Math.Max(1, maxPages) - 1);
            for (int n = result.FirstPage; n <= last; n++)
            {
                using (var page = doc.GetPage((uint)(n - 1)))
                using (var stream = new InMemoryRandomAccessStream())
                {
                    var options = new PdfPageRenderOptions
                    {
                        DestinationWidth = (uint)width,
                        BitmapEncoderId = BitmapEncoder.JpegEncoderId,
                    };
                    await page.RenderToStreamAsync(stream, options).AsTask().ConfigureAwait(false);
                    var bytes = new byte[stream.Size];
                    using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                    {
                        await reader.LoadAsync((uint)stream.Size).AsTask().ConfigureAwait(false);
                        reader.ReadBytes(bytes);
                    }
                    result.Images.Add(new ImageAttachment
                    {
                        MediaType = "image/jpeg",
                        Base64 = Convert.ToBase64String(bytes),
                        Label = $"{Path.GetFileName(path)} — sayfa {n}/{result.PageCount}",
                    });
                }
            }
            return result;
        }
    }
}
