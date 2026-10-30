using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KulAI.Netcad
{
    /// <summary>
    /// Geliştirici kanalı: arayüze tıklamadan test edebilmek için bir klasörü izler. Oraya bırakılan
    /// &lt;ad&gt;.cmd dosyasındaki komut NETCAD'in ana iş parçacığında çalışır, sonuç &lt;ad&gt;.out'a yazılır.
    /// Yalnızca modülün yanında dev-inbox.txt (izlenecek klasörün yolu) varsa açılır; bu dosyayı sadece
    /// geliştirme kurulumu koyar, müşteri paketinde yoktur.
    /// MCP kanalı: modülün yanında mcp-inbox.txt varsa aynı kanal Claude Code'un MCP köprüsüne (mcp\netcad_mcp.py) açılır.
    /// Bu kipte araçlar kullanıcının açık projesinde çalışır; her araç çağrısını Claude Code kullanıcıya onaylatır.
    /// </summary>
    internal sealed class DevChannel : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Control _uiThread;
        private readonly Func<string, Task<string>> _execute;

        /// <summary>İzlenen klasör (geliştirici kanalı açık değilse null).</summary>
        public static string Inbox { get; private set; }

        /// <summary>Kanal MCP köprüsü için açıldı: araçlar deneme projesiyle sınırlı değil.</summary>
        public static bool McpMode { get; private set; }

        private DevChannel(string inbox, Control uiThread, Func<string, Task<string>> execute)
        {
            _uiThread = uiThread;
            _execute = execute;
            Inbox = inbox;
            Tools.ExportPdfTool.SuppressOpen = true;
            Directory.CreateDirectory(inbox);
            _watcher = new FileSystemWatcher(inbox, "*.cmd") { EnableRaisingEvents = true };
            _watcher.Created += (s, e) => _uiThread.BeginInvoke(new Action(() => Handle(e.FullPath)));
            MainModule.Log("Geliştirici kanalı açık: " + inbox);
        }

        public static DevChannel TryStart(string moduleDirectory, Control uiThread, Func<string, Task<string>> execute)
        {
            var flag = Path.Combine(moduleDirectory, "dev-inbox.txt");
            if (!File.Exists(flag))
            {
                flag = Path.Combine(moduleDirectory, "mcp-inbox.txt");
                if (!File.Exists(flag)) return null;
                McpMode = true;
            }
            var inbox = File.ReadAllText(flag).Trim();
            return string.IsNullOrEmpty(inbox) ? null : new DevChannel(inbox, uiThread, execute);
        }

        /// <summary>
        /// Geliştirici kanalı yalnızca deneme projelerinde çizim yapabilir: diske kaydedilmemiş yeni bir proje ya da
        /// adında "KulAI-Test" geçen bir proje. Kullanıcının gerçek projesine yanlışlıkla çizim yapılmasın.
        /// </summary>
        public static string RefuseIfRealProject()
        {
            if (McpMode) return NetcadContext.CurrentProject == null ? "Açık proje yok." : null;
            var file = NetcadContext.CurrentProject?.FileName;
            if (string.IsNullOrEmpty(file)) return "Açık proje yok.";
            if (Path.GetFileName(file).IndexOf("KulAI-Test", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            if (!File.Exists(file)) return null;
            return $"REDDEDİLDİ: açık proje gerçek bir dosya ({Path.GetFileName(file)}). Yeni boş bir proje açın ya da adında KulAI-Test geçen bir proje kullanın.";
        }

        private async void Handle(string path)
        {
            string result;
            try
            {
                // Dosya yazımı bitmemiş olabilir; kısa bekleyip oku.
                await Task.Delay(200);
                var command = File.ReadAllText(path, Encoding.UTF8);
                result = await _execute(command);
            }
            catch (Exception ex)
            {
                result = "İSTİSNA: " + ex;
            }
            try
            {
                File.WriteAllText(Path.ChangeExtension(path, ".out"), result, Encoding.UTF8);
                File.Delete(path);
            }
            catch (Exception ex)
            {
                MainModule.Log("Geliştirici kanalı yazamadı: " + ex.Message);
            }
        }

        public void Dispose() => _watcher.Dispose();
    }
}
