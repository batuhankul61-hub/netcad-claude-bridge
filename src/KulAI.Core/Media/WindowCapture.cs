using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using KulAI.Core.Llm;

namespace KulAI.Core.Media
{
    /// <summary>Bir pencerenin (ör. CAD çizim alanı) görüntüsünü alır; pencere başka pencerelerin altında olsa da çalışır.</summary>
    public static class WindowCapture
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
        private const uint PW_RENDERFULLCONTENT = 2;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);

        private static string ClassOf(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>
        /// Bu sürecin görünür en büyük üst düzey penceresi. Delphi programlarında (NETCAD) Process.MainWindowHandle
        /// boyutsuz gizli TApplication penceresini döndürebildiği için ana form böyle bulunur.
        /// </summary>
        /// <param name="windowClass">Verilirse yalnızca bu sınıftaki pencereler (ör. açılış ekranı gibi başka pencereler yakalanmasın).</param>
        public static IntPtr FindMainWindow(string windowClass = null)
        {
            uint pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;
            EnumWindows((h, l) =>
            {
                if (GetWindowThreadProcessId(h, out var p) != 0 && p == pid && IsWindowVisible(h) && !IsIconic(h)
                    && (windowClass == null || ClassOf(h) == windowClass)
                    && GetWindowRect(h, out var r))
                {
                    long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                    if (area > bestArea) { bestArea = area; best = h; }
                }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        /// <summary>Pencereyi yakalar ve uzun kenarı <paramref name="maxSize"/> pikseli geçmeyecek şekilde küçültür.</summary>
        public static ImageAttachment Capture(IntPtr hwnd, int maxSize = 1600, string label = null)
        {
            if (!GetWindowRect(hwnd, out var r) || r.Right <= r.Left || r.Bottom <= r.Top)
                throw new InvalidOperationException("Pencere boyutu alınamadı (simge durumunda olabilir).");
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            using (var full = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(full))
                {
                    var hdc = g.GetHdc();
                    try { PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT); }
                    finally { g.ReleaseHdc(hdc); }
                }
                double scale = Math.Min(1.0, (double)maxSize / Math.Max(w, h));
                using (var small = new Bitmap(full, Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale))))
                using (var ms = new MemoryStream())
                {
                    var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    var p = new EncoderParameters(1) { Param = { [0] = new EncoderParameter(Encoder.Quality, 85L) } };
                    small.Save(ms, jpeg, p);
                    return new ImageAttachment
                    {
                        MediaType = "image/jpeg",
                        Base64 = Convert.ToBase64String(ms.ToArray()),
                        Label = label,
                    };
                }
            }
        }
    }
}
