using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KulAI.Core;
using KulAI.Core.Agent;
using KulAI.Core.Llm;
using KulAI.Core.Settings;
using Netcad.Interop;
using Netcad.Interop.DockManager;
using Netcad.Interop.Modules;

[assembly: NetcadModule(typeof(KulAI.Netcad.MainModule))]

namespace KulAI.Netcad
{
    /// <summary>
    /// NETCAD modülünün giriş noktası. Yaşam döngüsü:
    ///   OnCreate → modül kendini tanıtır (ad, sürüm, komut sayısı)
    ///   OnRegisterMenuItems → menü düğmeleri kaydedilir (ID'ler .NCRIB'deki CommandId ile eşleşir)
    ///   OnMenuItemClick → düğmeye basılınca çağrılır
    /// </summary>
    public sealed class MainModule : NetcadModule
    {
        public const string ShortName = "KulAI";
        public const ushort CmdOpenAssistant = 1;

        private global::Netcad.Interop.DockManager.NetcadDockForm<AssistantDockForm> _dock;
        private DevChannel _dev;
        private static string _devProvider;
        private static CancellationTokenSource _devAsk;

        private static readonly string ModuleDirectory = Path.GetDirectoryName(typeof(MainModule).Assembly.Location);

        static MainModule()
        {
            // Çekirdek, Claude bileşeni ve bağımlılıkları modül klasöründe durur; CLR onları orada bulsun.
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var file = new AssemblyName(e.Name).Name + ".dll";
                var path = Path.Combine(ModuleDirectory, file);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
                // NETCAD'in kendi .NET kütüphaneleri (ör. Netcad.Epsg) TOOLS\UTILS\NETTOOLS altında.
                var netTools = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(ModuleDirectory)), "TOOLS", "UTILS", "NETTOOLS", file);
                return File.Exists(netTools) ? Assembly.LoadFrom(netTools) : null;
            };
        }

        public MainModule()
        {
            Log("MainModule oluşturuldu.");
        }

        private static void ConfigureCore()
        {
            AppEnvironment.ProductName = "Kul AI Asistan";
            AppEnvironment.DataFolderName = "KulAI.Netcad";
            AppEnvironment.PluginDirectory = ModuleDirectory;
            AppEnvironment.Diagnostic = Log;
        }

        protected override TModuleInfo OnCreate(ModuleInitEventArgs e)
        {
            Log($"OnCreate: NETCAD {e.Version}, dil={e.Language}, yol={e.DllPath}");
            var info = new TModuleInfo
            {
                Name = ShortName,
                Version = typeof(MainModule).Assembly.GetName().Version,
                Date = DateTime.Today,
                Author = "Kul",
                Description = "Kul AI Asistan: NETCAD için yapay zeka asistanı",
            };
            info.NCommands = 1;
            return info;
        }

        protected override bool OnInit(ModuleInitEventArgs e)
        {
            Log("OnInit");
            return true;
        }

        protected override void OnRegisterMenuItems(RegisterMenuItemEventArgs e)
        {
            Log($"OnRegisterMenuItems: RegisterRibbonItems={e.RegisterRibbonItems} HasMenuItems={e.HasMenuItems}");
            // Şerit menü öğeleri modül klasöründeki KulAI_<DİL>.NCRIB dosyasından okunsun.
            e.RegisterRibbonItems = true;
            int registered = e.Register(new NetcadMenuItem
            {
                ID = CmdOpenAssistant,
                Name = "Kul AI Asistan",
                Category = "Araçlar",
                Hint = "Yapay zeka asistanını açar",
                ShortModuleName = ShortName,
                RegMenu = true,
            });
            Log($"  Register sonucu={registered}, sonra: RegisterRibbonItems={e.RegisterRibbonItems} HasMenuItems={e.HasMenuItems}");
        }

        protected override bool OnAfterModulesLoaded(global::Netcad.Interop.NcApiX.NetcadEventArgs e)
        {
            Log("OnAfterModulesLoaded: tüm modüller yüklendi.");
            try
            {
                ConfigureCore();
                // Şerit menüye düğme eklenemediği sürece panel NETCAD açılınca kendiliğinden gelir.
                var form = new AssistantDockForm();
                _dock = NetcadDockForm<AssistantDockForm>.Create(form, false);
                _dock.Show();
                Log("Panel gösterildi.");

                // Geliştirme kurulumunda: komutları klasörden al, panelin (NETCAD ana) iş parçacığında çalıştır.
                _dev = DevChannel.TryStart(ModuleDirectory, form, command => RunDevCommand(form, command));
            }
            catch (Exception ex)
            {
                Log("Panel açılamadı: " + ex);
            }
            return true;
        }

        protected override int OnMenuItemClick(MenuItemClickEventArgs e)
        {
            Log($"OnMenuItemClick: {e.MenuID}");
            if (e.MenuID == CmdOpenAssistant)
            {
                MessageBox.Show("Kul AI Asistan NETCAD içinde çalışıyor.\n\n(Deneme modülü)", "Kul AI Asistan",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }

        /// <summary>
        /// Geliştirici kanalı komutları:
        ///   diag                     NETCAD API erişim zincirini kontrol eder
        ///   tool &lt;ad&gt; &lt;json&gt;  bir aracı modelsiz çalıştırır
        ///   ask &lt;istek&gt;          asistanı (seçili modelle) çalıştırır, konuşmayı döndürür
        ///   schemas                  araç tanımlarını JSON olarak döndürür (MCP köprüsü bunları Claude'a sunar)
        /// Çizim yapabilecek komutlar yalnızca deneme projesinde çalışır (DevChannel.RefuseIfRealProject).
        /// </summary>
        private static async Task<string> RunDevCommand(AssistantDockForm form, string command)
        {
            command = command.Trim();
            if (command == "diag") return DrawingProbe.Diagnose();

            if (command == "schemas")
                return Newtonsoft.Json.JsonConvert.SerializeObject(form.Tools.Specs.Select(s => new { name = s.Name, description = s.Description, input_schema = s.InputSchema }));

            if (command == "closetest")
            {
                // Yalnızca geliştirici deneme projelerini (adı KulAI-Test) kaydetmeden kapatır; diğer projelere dokunmaz.
                int closed = 0;
                for (int i = 0; i < 20; i++)
                {
                    var file = NetcadContext.CurrentProject?.FileName ?? "";
                    if (System.IO.Path.GetFileName(file).IndexOf("KulAI-Test", StringComparison.OrdinalIgnoreCase) < 0) break;
                    if (!NetcadApplication.Current.CloseProject(false)) break;
                    closed++;
                }
                return $"Kapatılan deneme projesi: {closed}";
            }

            if (command == "newproject")
            {
                // Testler için yeni, boş, kaydedilmemiş proje; kullanıcının açık projelerine dokunmaz.
                NetcadApplication.Current.NewProject("KulAI-Test");
                return "Yeni proje açıldı: " + NetcadContext.CurrentProject?.FileName;
            }

            if (command.StartsWith("tool "))
            {
                var rest = command.Substring(5).Trim();
                int space = rest.IndexOf(' ');
                var name = space < 0 ? rest : rest.Substring(0, space);
                var json = space < 0 ? "{}" : rest.Substring(space + 1);
                var listOnly = name == "list_layers" || name == "get_drawing_info";
                if (!listOnly && DevChannel.RefuseIfRealProject() is string refusal) return refusal;
                var result = form.Tools.Execute(new ToolCall { Id = "dev", Name = name, Arguments = Newtonsoft.Json.Linq.JObject.Parse(json) });
                // Araç görüntüleri (ekran, PDF sayfası) gözle kontrol için gelen kutusuna kaydedilir.
                var saved = new System.Collections.Generic.List<string>();
                foreach (var img in result.Images)
                {
                    var file = Path.Combine(DevChannel.Inbox, $"{name}-{DateTime.Now:HHmmss}-{saved.Count + 1}.jpg");
                    File.WriteAllBytes(file, Convert.FromBase64String(img.Base64));
                    saved.Add(file);
                }
                return (result.IsError ? "HATA: " : "SONUÇ: ") + result.Content +
                       (saved.Count > 0 ? "\nGÖRÜNTÜ: " + string.Join(", ", saved) : "");
            }

            if (command == "exporters")
            {
                // NETCAD'in kayıtlı dışa aktarıcıları (PDF desteği araştırması).
                var mgr = NetcadApplication.CurrentCOM.ImportExportManager;
                var list = mgr.GetExporterList(global::Netcad.Interop.Extensions.NcApi.ImportExportFlag.All);
                var sb = new System.Text.StringBuilder($"Dışa aktarıcı: {list.Count}\n");
                for (int i = 0; i < list.Count; i++)
                {
                    try
                    {
                        var ex = (global::Netcad.Interop.Extensions.NcApi.INetcadExporter)list[i];
                        sb.AppendLine($"{i}: {ex.Name()} | {ex.FileFilter(0)} | {ex.FileFilter(1)} | flags={ex.Flags()} | {ex.DLLName()}");
                    }
                    catch (Exception e) { sb.AppendLine($"{i}: ? {e.Message}"); }
                }
                return sb.ToString();
            }

            if (command == "drawbmp")
            {
                var objs = new System.Collections.Generic.List<global::Netcad.Interop.NMath.NetcadObjectEx>();
                foreach (var item in KulAI.Netcad.Tools.Objects.All()) objs.Add(item.Obj);
                using (var bmp = NetcadApplication.Current.DrawManager.DrawToBitmap(objs, out var rect, out var world))
                {
                    var file = Path.Combine(DevChannel.Inbox, $"drawbmp-{DateTime.Now:HHmmss}.png");
                    bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                    return $"{objs.Count} nesne, {bmp.Width}x{bmp.Height}, rect={rect}, world={world.Left},{world.Bottom},{world.Right},{world.Top}\nGÖRÜNTÜ: {file}";
                }
            }

            if (command == "cancel")
            {
                // Süresi dolan senaryonun asistanı sonraki senaryonun projesine çizim yapmasın.
                _devAsk?.Cancel();
                return "Durdurma istendi.";
            }

            if (command.StartsWith("provider "))
            {
                // Yalnızca geliştirici kanalındaki "ask" komutları için; kaydedilmez, kullanıcının ayarı değişmez.
                _devProvider = command.Substring(9).Trim();
                return "Geliştirici sağlayıcısı: " + _devProvider;
            }

            if (command.StartsWith("ask "))
            {
                if (DevChannel.RefuseIfRealProject() is string refusal) return refusal;
                var log = new ConversationLog("dev");
                var transcript = new System.Text.StringBuilder();
                log.LineWritten += line => transcript.AppendLine(line);
                var agent = new AssistantAgent(form.Tools, form.Chat.Dispatcher, NetcadPrompt.Text)
                {
                    // Yalnızca geliştirici kanalı (deneme projesi): onaylar otomatik verilir ve kaydedilir.
                    ConfirmAsync = (call, message) => { log.Write("ONAY (otomatik, geliştirici kanalı): " + message); return Task.FromResult(true); },
                };
                log.Attach(agent);
                var settings = AppSettings.Load();
                if (!string.IsNullOrEmpty(_devProvider)) settings.Provider = _devProvider;
                try
                {
                    var provider = ProviderFactory.Create(settings);
                    log.Write("SAĞLAYICI: " + provider.DisplayName);
                    log.Write("KULLANICI: " + command.Substring(4));
                    _devAsk = new CancellationTokenSource();
                    await agent.RunAsync(provider, command.Substring(4), settings.MaxToolIterations, _devAsk.Token);
                }
                catch (OperationCanceledException)
                {
                    log.Write("DURDURULDU (geliştirici kanalı)");
                }
                catch (Exception ex)
                {
                    log.Write("İSTİSNA: " + ex.Message);
                }
                return transcript.ToString();
            }

            return "Bilinmeyen komut: " + command;
        }

        /// <summary>Modülün kendi yaşam döngüsü kaydı (NETCAD yükleme, panel, hatalar).</summary>
        internal static void Log(string message)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KulAI.Netcad", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "modul.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Log isteğe bağlıdır.
            }
        }
    }
}
