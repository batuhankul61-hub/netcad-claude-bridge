using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using KulAI.Core.Llm;
using KulAI.Core.Tools;

namespace KulAI.Core.Agent
{
    /// <summary>
    /// Sohbet + araç çağırma döngüsü. Sağlayıcıdan ve CAD programından bağımsızdır: model araç istedikçe
    /// aracı ürünün araç setiyle (IToolHost) çalıştırır, sonucu modele geri verir, model bitirene kadar döner.
    /// </summary>
    public sealed class AssistantAgent
    {
        /// <summary>Her üründe geçerli çalışma kuralları; ürünün kendi talimatının sonuna eklenir.</summary>
        public const string CommonRules = @"Çalışma kuralları:
- Kullanıcıya bir şeyin nasıl yapılacağını anlatma; araçlarla kendin yap. Yalnızca araçların gerçekten yapamadığı bir şey istenirse açıkla.
- Serbest ya da yaratıcı isteklerde gerçekçi ölçüleri (metre) kendin seç ve hemen uygula; ölçü sormak için durma.
- Yalnızca sana verilen araçları kullan; olmayan bir araçtan bahsetme.
- Yalnızca kullanıcının dilinde yaz (genellikle Türkçe); başka dilde kelime ya da karakter kullanma.
- Çizimdeki mevcut nesnelere ya da katmanlara atıf yapılıyorsa önce okuma araçlarıyla durumu öğren; ad, kimlik ya da handle uydurma. Bunları yalnızca araç sonuçlarından al.
- Kullanıcı koordinat vermediyse makul değerler seç ve cevabında belirt.
- Bir adım önceki adımın sonucuna ihtiyaç duyuyorsa önceki aracın sonucunu bekle; o değeri aynı anda tahmin ederek çağırma.
- Birbirine bağlı olmayan işleri tek turda, aynı cevapta birden çok araç çağrısıyla topluca yap; her birini ayrı tura bölme. Aynı nesneyi iki kez oluşturma.
- Bir araç hata döndürürse nedenini anla, düzeltip bir kez daha dene; yine olmazsa kullanıcıya açıkça söyle.
- Özetinde yalnızca araç sonucu başarılı dönen işlemleri 'yapıldı' olarak yaz; hata veren adımları başarılı gibi gösterme.
- Bir araç sonucunda 'warning' alanı varsa sonuç isteği karşılamıyor demektir: mümkünse düzelt, değilse uyarıyı kullanıcıya aktar.
- İş bitince ne yaptığını kısa ve net özetle. Kullanıcı Ctrl+Z ile geri alabilir.";

        private readonly IToolHost _tools;
        private readonly Dispatcher _cadThread;
        private readonly string _systemPrompt;
        private readonly List<ChatTurn> _history = new List<ChatTurn>();

        /// <param name="productPrompt">Ürüne özgü talimat: rolü, ortamı, iş akışları, koordinat düzeni.</param>
        public AssistantAgent(IToolHost tools, Dispatcher cadThread, string productPrompt)
        {
            _tools = tools;
            _cadThread = cadThread;
            _systemPrompt = productPrompt.Trim() + "\n\n" + CommonRules;
        }
        /// <summary>Asistanın bir turdaki son, tam metni (akış bittiğinde).</summary>
        public event Action<string> AssistantText;
        /// <summary>Akış sırasında gelen metin parçası; arayüz bunları anlık gösterir.</summary>
        public event Action<string> AssistantTextDelta;
        /// <summary>Akışla gösterilen metin geçersiz sayıldı (ör. doğrulama turu); arayüz onu kaldırmalı.</summary>
        public event Action AssistantTextRetracted;
        public event Action<ToolCall> ToolStarted;
        public event Action<ToolResult> ToolFinished;

        /// <summary>
        /// Onay gerektiren bir araç çağrısında kullanıcıya sorar (arayüz "Onayla / İptal" gösterir).
        /// Atanmamışsa onay gerektiren işler yapılmaz.
        /// </summary>
        public Func<ToolCall, string, Task<bool>> ConfirmAsync { get; set; }

        public void Reset() => _history.Clear();

        private const string RememberStandardName = "remember_standard";

        private static readonly ToolSpec RememberStandardSpec = new ToolSpec
        {
            Name = RememberStandardName,
            Description = "Kullanıcının firma standardını kalıcı olarak kaydeder (ör. katman adı, renk, yazı boyu kuralı). " +
                          "Kullanıcı 'bunu hatırla', 'bizde her zaman ... yapılır' gibi bir şey söylediğinde çağır.",
            InputSchema = Newtonsoft.Json.Linq.JObject.Parse(@"{ ""type"": ""object"", ""properties"": { ""rule"": { ""type"": ""string"", ""description"": ""Tek cümlelik kural"" } }, ""required"": [""rule""] }"),
        };

        /// <summary>Ürün talimatı + ortak kurallar + kullanıcının kayıtlı firma standartları.</summary>
        private string BuildSystemPrompt(Settings.AppSettings settings)
        {
            var rules = (settings.CustomInstructions ?? "").Trim();
            return rules.Length == 0
                ? _systemPrompt
                : _systemPrompt + "\n\nKullanıcının firma standartları (her işte bunlara uy; araç parametrelerinde aksi belirtilmedikçe bunları kullan):\n" + rules;
        }

        private static ToolResult RememberStandard(ToolCall call)
        {
            var rule = ((string)call.Arguments["rule"] ?? "").Trim();
            var result = new ToolResult { ToolCallId = call.Id, ToolName = call.Name };
            if (rule.Length == 0) { result.IsError = true; result.Content = "Kural boş olamaz."; return result; }
            var settings = Settings.AppSettings.Load();
            settings.CustomInstructions = ((settings.CustomInstructions ?? "").TrimEnd() + "\n- " + rule).Trim();
            settings.Save();
            result.Content = "{\"saved\":true,\"rule\":" + Newtonsoft.Json.JsonConvert.ToString(rule) + "}";
            return result;
        }

        public async Task RunAsync(ILlmProvider provider, string userText, int maxIterations, CancellationToken ct,
                                   IReadOnlyList<ImageAttachment> images = null)
        {
            // Görüntüler her turda yeniden gönderilir ve pahalıdır: önceki isteklerin görüntülerini geçmişten çıkar.
            foreach (var old in _history)
            {
                old.Images.Clear();
                foreach (var r in old.ToolResults) r.Images.Clear();
            }
            var userTurn = ChatTurn.User(userText);
            if (images != null) userTurn.Images.AddRange(images);
            _history.Add(userTurn);
            var settings = Settings.AppSettings.Load();
            var systemPrompt = BuildSystemPrompt(settings);
            var specs = _tools.Specs.Concat(new[] { RememberStandardSpec }).ToList();
            // Bu istekte her aracın en son sonucu: sonu hata olan araç = tamamlanmamış adım.
            var lastResultByTool = new Dictionary<string, ToolResult>();
            bool verificationNudged = false;

            for (int i = 0; i < maxIterations; i++)
            {
                ct.ThrowIfCancellationRequested();
                LlmReply reply;
                bool streamed = false;
                // Parçalar arayüze sırayla ama akışı bekletmeden (BeginInvoke) gönderilir.
                Action<string> onDelta = delta =>
                {
                    streamed = true;
                    _cadThread.BeginInvoke(new Action(() => AssistantTextDelta?.Invoke(delta)));
                };
                try
                {
                    reply = await provider.SendAsync(systemPrompt, _history, specs, onDelta, ct).ConfigureAwait(false);
                }
                catch
                {
                    // Cevapsız kalan kullanıcı mesajını geri al; geçmiş tutarlı kalsın.
                    if (_history.Count > 0 && _history[_history.Count - 1].Kind == TurnKind.User)
                        _history.RemoveAt(_history.Count - 1);
                    throw;
                }

                if (reply.Refused)
                {
                    if (streamed) RaiseOnUi(() => AssistantTextRetracted?.Invoke());
                    RaiseOnUi(() => AssistantText?.Invoke("Model bu isteği güvenlik nedeniyle reddetti. İsteği farklı ifade etmeyi deneyin."));
                    return;
                }

                var assistant = reply.Assistant;
                _history.Add(assistant);

                // Akışta görünen metin temizlendikten sonra boş kaldıysa (ör. yalnızca <think> içeriği) kaldır.
                if (streamed && string.IsNullOrWhiteSpace(assistant.Text))
                    RaiseOnUi(() => AssistantTextRetracted?.Invoke());

                if (assistant.ToolCalls.Count == 0)
                {
                    // Model bitirdiğini söylüyor ama hata verip sonra başarıyla tekrarlanmamış adımlar var:
                    // bu cevabı göstermeden bir kez daha zorla. Küçük modeller aksi halde hata alan adımları
                    // "başarıyla oluşturuldu" diye özetleyebiliyor.
                    var unresolved = lastResultByTool.Values.Where(r => r.IsError).ToList();
                    if (unresolved.Count > 0 && !verificationNudged)
                    {
                        verificationNudged = true;
                        if (streamed) RaiseOnUi(() => AssistantTextRetracted?.Invoke());
                        _history.Add(ChatTurn.User(BuildVerificationNudge(unresolved)));
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(assistant.Text))
                        RaiseOnUi(() => AssistantText?.Invoke(assistant.Text));
                    if (reply.Truncated)
                        RaiseOnUi(() => AssistantText?.Invoke("(Cevap uzunluk sınırında kesildi.)"));
                    return;
                }

                if (!string.IsNullOrWhiteSpace(assistant.Text))
                    RaiseOnUi(() => AssistantText?.Invoke(assistant.Text));

                // Araçlar AutoCAD ana iş parçacığında sırayla çalışır.
                // Her araç çağrısına mutlaka bir sonuç döner; iptalde kalanlar "iptal" olarak işaretlenir,
                // yoksa bir sonraki istekte API cevapsız çağrı yüzünden hata verir.
                var results = new ChatTurn { Kind = TurnKind.ToolResults };
                foreach (var call in assistant.ToolCalls)
                {
                    if (ct.IsCancellationRequested || reply.Truncated)
                    {
                        // Uzunluk sınırında kesilen turda araç girdileri yarım olabilir (akışta özellikle):
                        // çalıştırma, modele bildir.
                        results.ToolResults.Add(new ToolResult
                        {
                            ToolCallId = call.Id, ToolName = call.Name, IsError = true,
                            Content = reply.Truncated
                                ? "Cevap uzunluk sınırında kesildi; araç girdisi eksik olabileceği için çalıştırılmadı. Daha küçük parçalarla tekrar dene."
                                : "Kullanıcı işlemi durdurdu.",
                        });
                        continue;
                    }
                    RaiseOnUi(() => ToolStarted?.Invoke(call));
                    ToolResult result;
                    if (call.Name == RememberStandardName)
                    {
                        result = RememberStandard(call);
                    }
                    else
                    {
                        // Büyük ya da geri dönüşü zor işlerde önce kullanıcıya sor (önizleme + onay).
                        var confirmText = _cadThread.Invoke(() => _tools.GetConfirmation(call, settings.ConfirmAboveObjectCount));
                        bool approved = confirmText == null;
                        if (!approved && ConfirmAsync != null)
                            approved = await _cadThread.Invoke(() => ConfirmAsync(call, confirmText)).ConfigureAwait(false);
                        result = approved
                            ? _cadThread.Invoke(() => _tools.Execute(call))
                            : new ToolResult
                            {
                                ToolCallId = call.Id, ToolName = call.Name, IsError = true,
                                Content = "Kullanıcı bu işlemi onaylamadı, yapılmadı (" + confirmText + "). Kullanıcıya ne yapmak istediğini sor.",
                            };
                    }
                    RaiseOnUi(() => ToolFinished?.Invoke(result));
                    results.ToolResults.Add(result);
                    lastResultByTool[call.Name ?? ""] = result;
                }
                _history.Add(results);
                ct.ThrowIfCancellationRequested();
            }

            RaiseOnUi(() => AssistantText?.Invoke($"Adım sınırına ({maxIterations}) ulaşıldı; işlem durduruldu."));
        }

        private static string BuildVerificationNudge(List<ToolResult> unresolved)
        {
            var lines = unresolved.Select(r => $"- {r.ToolName}: {r.Content}");
            return "[Otomatik kontrol] Şu adımlar hata verdi ve sonrasında başarıyla tekrarlanmadı, yani YAPILMADI:\n" +
                   string.Join("\n", lines) +
                   "\nHatanın nedeni artık giderildiyse (ör. eksik nesne sonradan oluşturulduysa) bu adımları şimdi araçlarla tekrar yap. " +
                   "Yapamıyorsan özetinde bu adımların yapılmadığını açıkça belirt; başarılı gibi gösterme.";
        }

        private void RaiseOnUi(Action action) => _cadThread.Invoke(action);
    }
}
