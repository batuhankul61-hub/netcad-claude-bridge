using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    /// <summary>
    /// NETCAD komutlarının menüdeki yeri. Bilgi, kurulu NETCAD'in kendi şerit tanımlarından (*.NCRIB) okunur:
    /// ana program ve kurulu modüller (Netpro, Netsurf, Netkamu…) dahil, kullanıcının sürümüyle birebir aynıdır.
    /// </summary>
    internal static class CommandCatalog
    {
        public sealed class Command
        {
            public string Name, Hint, Description, Category, Shortcut, Tag, Module, File;
            public int CommandId;
            public string[] Tokens;
        }

        private static List<Command> _all;

        public static string NetcadDirectory
        {
            get
            {
                try { return Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName); }
                catch { return Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(typeof(CommandCatalog).Assembly.Location))); }
            }
        }

        public static List<Command> All()
        {
            if (_all != null) return _all;
            var dir = NetcadDirectory;
            var files = Directory.GetFiles(dir, "*_TRK.NCRIB", SearchOption.AllDirectories).ToList();
            if (files.Count == 0) files = Directory.GetFiles(dir, "*_ENU.NCRIB", SearchOption.AllDirectories).ToList();
            // Kendi modülümüzün dosyası listeye girmesin.
            files = files.Where(f => !f.Replace('/', '\\').ToUpperInvariant().Contains("\\MODUL\\KULAI\\")).ToList();

            var list = new List<Command>();
            var enc = Encoding.GetEncoding(1254);
            foreach (var file in files)
            {
                try
                {
                    var doc = XDocument.Parse(File.ReadAllText(file, enc));
                    foreach (var item in doc.Descendants("Item"))
                    {
                        string V(string n) => ((string)item.Element(n) ?? "").Trim();
                        var category = V("Category").TrimEnd('/');
                        if (category.Length == 0) continue;
                        var cmd = new Command
                        {
                            Name = V("SimpleName"), Hint = V("Hint"), Description = V("Exp"),
                            Category = category.StartsWith("Quicktoolbar", StringComparison.OrdinalIgnoreCase) ? "Hızlı erişim araç çubuğu (en üst)" : category,
                            Shortcut = V("Shortcut"), Tag = V("Tag"), Module = V("Short_Module_Name"),
                            CommandId = int.TryParse(V("CommandId"), out var id) ? id : 0,
                            File = Path.GetFileName(file),
                        };
                        if (cmd.Name.Length == 0) continue;
                        cmd.Tokens = Tokenize($"{cmd.Name} {cmd.Hint} {cmd.Description} {cmd.Tag} {cmd.Category}");
                        list.Add(cmd);
                    }
                }
                catch (Exception ex) { MainModule.Log($"Şerit dosyası okunamadı ({file}): {ex.Message}"); }
            }
            // Aynı komut birden çok yerde olabilir (şerit + hızlı erişim): ad + yer tekil olsun.
            _all = list.GroupBy(c => c.Name + "|" + c.Category).Select(g => g.First()).ToList();
            return _all;
        }

        /// <summary>Küçük harf, Türkçe harfler sadeleşmiş (ı→i, ş→s…), noktalama atılmış kelimeler.</summary>
        public static string[] Tokenize(string s)
        {
            var lower = (s ?? "").ToLower(new CultureInfo("tr-TR"));
            var sb = new StringBuilder(lower.Length);
            foreach (var ch in lower)
            {
                switch (ch)
                {
                    case 'ı': sb.Append('i'); break;
                    case 'ş': sb.Append('s'); break;
                    case 'ğ': sb.Append('g'); break;
                    case 'ü': sb.Append('u'); break;
                    case 'ö': sb.Append('o'); break;
                    case 'ç': sb.Append('c'); break;
                    case 'â': sb.Append('a'); break;
                    case 'î': sb.Append('i'); break;
                    default: sb.Append(char.IsLetterOrDigit(ch) ? ch : ' '); break;
                }
            }
            return sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Kelime eşleşmesi ekleri tolere eder: "yazdırma" ~ "yaz", "kütürü" ~ "kutur".</summary>
        public static bool WordMatches(string query, string token)
        {
            if (token == query) return true;
            if (token.Length >= 3 && query.StartsWith(token)) return true;
            if (query.Length >= 3 && token.StartsWith(query)) return true;
            return false;
        }
    }

    public sealed class RunNetcadCommandTool : NetcadTool
    {
        public override string Name => "run_netcad_command";
        public override string Description =>
            "NETCAD'in kendi bir komutunu, kullanıcı menüden tıklamış gibi başlatır (ör. 'Cephe Yaz', 'Kutur Bağla', 'Etiketleri Üret'). " +
            "Kendi araçlarınla yapamadığın ama NETCAD'de komutu olan işler için kullan; komut adını önce find_netcad_command ile bul. " +
            "Çoğu komut etkileşimlidir: başlattıktan sonra kullanıcıya NETCAD'in ekranda ne isteyeceğini (nesne seç, nokta göster, " +
            "Enter/sağ tık ile bitir) kısaca söyle. Komutun sonucunu sen göremezsin; 'yaptım' deme, 'başlattım' de.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""command"":   { ""type"": ""string"", ""description"": ""find_netcad_command'ın verdiği komut adı, ör. Cephe Yaz"" },
    ""menu_path"": { ""type"": ""string"", ""description"": ""Aynı adlı birden çok komut varsa menü yolu"" }
  },
  ""required"": [""command""]
}";
        // Komutun kendisi değişikliği kullanıcının etkileşimiyle yapar; geri alma bloğu ve onay NETCAD'in kendi işidir.
        public override bool ModifiesDrawing => false;

        private static readonly string[] Risky = { "kapat", "çık", "cik", "yeni", "aç", "kaydet", "sil", "temizle" };

        private static CommandCatalog.Command Find(JObject args)
        {
            var name = RequiredStr(args, "command").Trim();
            var path = Str(args, "menu_path");
            var all = CommandCatalog.All().Where(c => c.CommandId > 0 && !string.IsNullOrEmpty(c.Module)).ToList();
            var hits = all.Where(c => string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (hits.Count == 0)
            {
                var q = CommandCatalog.Tokenize(name);
                hits = all.Where(c => CommandCatalog.Tokenize(c.Name).SequenceEqual(q)).ToList();
            }
            if (hits.Count > 1 && !string.IsNullOrWhiteSpace(path))
            {
                var p = CommandCatalog.Tokenize(path);
                hits = hits.OrderByDescending(c => p.Count(w => CommandCatalog.Tokenize(c.Category).Contains(w))).Take(1).ToList();
            }
            if (hits.Count == 0)
                throw new ArgumentException($"'{name}' adlı NETCAD komutu bulunamadı. Önce find_netcad_command ile tam adını bul.");
            return hits[0];
        }

        public override string ConfirmationFor(JObject args, int threshold)
        {
            var c = Find(args);
            var tokens = CommandCatalog.Tokenize(c.Name);
            bool risky = c.Category.StartsWith("Uygulama Menüsü") || c.Category.StartsWith("Hızlı erişim")
                         || Risky.Any(r => tokens.Contains(CommandCatalog.Tokenize(r).FirstOrDefault() ?? r));
            return risky ? $"NETCAD komutu başlatılacak: {c.Category.Replace("/", " > ")} > {c.Name}" : null;
        }

        public override string Execute(JObject args)
        {
            var c = Find(args);
            var ribbon = global::Netcad.Interop.NetcadApplication.Current.RibbonManager
                         ?? throw new InvalidOperationException("NETCAD şerit yöneticisine erişilemedi.");
            ribbon.FindRibbonItem(c.Module, c.CommandId, out var details);
            var onCommand = details?.GetOnCommand()
                            ?? throw new InvalidOperationException($"'{c.Name}' komutu NETCAD'de bulunamadı (modül yüklü olmayabilir).");

            // Etkileşimli komut kullanıcı seçim yapana kadar sürer: asistanın cevabını bekletmemek için hemen ardından başlat.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    try { onCommand.Execute(c.Module, c.CommandId); }
                    catch (Exception ex) { MainModule.Log($"NETCAD komutu çalıştırılamadı ({c.Name}): {ex}"); }
                }));

            return Ok(new
            {
                started = c.Name,
                menu_path = c.Category.Replace("/", " > ") + " > " + c.Name,
                note = "Komut NETCAD'de başlatıldı. Kullanıcı ekranda NETCAD'in istediği seçimleri yapmalı; sonucu sen göremezsin.",
            });
        }
    }

    public sealed class FindNetcadCommandTool : NetcadTool
    {
        public override string Name => "find_netcad_command";
        public override string Description =>
            "NETCAD'in kendi komutlarının menüde nerede olduğunu bulur (sekme / grup / komut adı, kısayol). " +
            "Kullanıcı 'X komutu nerede', 'X'i nasıl yaparım' diye sorduğunda ya da senin araçlarınla yapılamayan bir iş " +
            "istendiğinde önce bununla ara ve kullanıcıya menü yolunu söyle. Bilgi kullanıcının kurulu NETCAD'inden okunur; " +
            "burada çıkmayan bir menü yolu uydurma. Arama: Türkçe anahtar kelimeler, ör. 'cephe yaz', 'kesit', 'aplikasyon'.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""query"": { ""type"": ""string"", ""description"": ""Anahtar kelimeler"" },
    ""limit"": { ""type"": ""integer"", ""description"": ""Varsayılan 10"" }
  },
  ""required"": [""query""]
}";
        public override bool ModifiesDrawing => false;

        public override string Execute(JObject args)
        {
            var words = CommandCatalog.Tokenize(RequiredStr(args, "query"))
                .Where(w => w.Length >= 2 && !StopWords.Contains(w)).Distinct().ToList();
            if (words.Count == 0) throw new ArgumentException("Aranacak kelime yok.");
            var all = CommandCatalog.All();

            var scored = all.Select(c =>
            {
                int matched = words.Count(w => c.Tokens.Any(t => CommandCatalog.WordMatches(w, t)));
                var nameTokens = CommandCatalog.Tokenize(c.Name);
                int inName = words.Count(w => nameTokens.Any(t => CommandCatalog.WordMatches(w, t)));
                return (c, score: matched * 10 + inName * 3);
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.c.Name.Length)
            .Take((int)Num(args, "limit", 10))
            .Select(x => new
            {
                command = x.c.Name,
                menu_path = x.c.Category.Replace("/", " > ") + " > " + x.c.Name,
                description = !string.IsNullOrEmpty(x.c.Description) ? x.c.Description : (x.c.Hint != x.c.Name ? x.c.Hint : null),
                shortcut = string.IsNullOrEmpty(x.c.Shortcut) ? null : x.c.Shortcut,
                module = x.c.Module,
            }).ToList();

            return Ok(new
            {
                total_commands = all.Count,
                matches = scored.Count,
                commands = scored,
                note = scored.Count == 0 ? "Şeritte bu kelimelerle komut yok; başka kelimelerle dene ya da kullanıcıya bulunamadığını söyle." : null,
            });
        }

        private static readonly HashSet<string> StopWords = new HashSet<string>
        {
            "komut", "komutu", "komutlari", "nerede", "nerde", "nasil", "yapilir", "yaparim", "icin", "ile", "ve", "bir", "bu", "su", "mi", "mu",
        };
    }
}
