using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KulAI.Core.Llm
{
    /// <summary>
    /// Yerel Ollama — kendi /api/chat uç noktası üzerinden. OpenAI uyumlu uç nokta bağlam
    /// uzunluğunu (num_ctx) ayarlatmadığı için Ollama varsayılanı olan 4096 token'a takılır;
    /// araç tanımları + çok adımlı geçmiş buna sığmaz ve model eski adımları sessizce unutur.
    /// </summary>
    public sealed class OllamaProvider : ILlmProvider
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private static readonly Regex ThinkTags = new Regex("<think>.*?</think>", RegexOptions.Singleline);

        private readonly string _baseUrl;
        private readonly string _model;
        private readonly int _contextLength;

        public OllamaProvider(string baseUrl, string model, int contextLength)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("Ollama model adı girilmemiş. Ayarlar'dan ekleyin.");

            // Eski ayarlarda OpenAI uyumlu adres (/v1) kayıtlı olabilir.
            _baseUrl = Regex.Replace(baseUrl.Trim().TrimEnd('/'), "/v1$", "");
            _model = model;
            _contextLength = contextLength;
        }

        public string DisplayName => "Yerel (Ollama) · " + _model;

        public async Task<LlmReply> SendAsync(string systemPrompt, IReadOnlyList<ChatTurn> history,
                                              IReadOnlyList<ToolSpec> tools, Action<string> onTextDelta, CancellationToken ct)
        {
            var messages = new JArray { new JObject { ["role"] = "system", ["content"] = systemPrompt } };
            foreach (var turn in history)
                foreach (var m in ToMessages(turn))
                    messages.Add(m);

            var body = new JObject
            {
                ["model"] = _model,
                ["messages"] = messages,
                ["stream"] = true,
                ["options"] = new JObject { ["num_ctx"] = _contextLength },
                ["tools"] = new JArray(tools.Select(t => new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = t.InputSchema,
                    },
                })),
            };

            // Bu model görüntü desteklemiyorsa (daha önce reddettiyse) görüntüleri baştan çıkar.
            bool noVision;
            lock (TextOnlyModels) noVision = TextOnlyModels.Contains(_model);
            if (noVision) StripImages(messages);

            for (int attempt = 1; ; attempt++)
            using (var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/api/chat"))
            {
                request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new InvalidOperationException(
                        $"Yerel modele (Ollama) bağlanılamadı ({_baseUrl}). Ollama kurulu değilse Ayarlar'dan Google Gemini'yi seçin.", ex);
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        // Görüntü desteklemeyen model (ör. qwen3): görüntüleri çıkarıp modele durumu yazarak yeniden dene.
                        if (attempt == 1 && error.IndexOf("multimodal", StringComparison.OrdinalIgnoreCase) >= 0 && StripImages(messages))
                        {
                            lock (TextOnlyModels) TextOnlyModels.Add(_model);
                            AppEnvironment.Diagnostic?.Invoke($"{DisplayName}: görüntü desteklemiyor, görüntüsüz yeniden deneniyor.");
                            continue;
                        }
                        if (error.Contains("not found"))
                            throw new InvalidOperationException($"'{_model}' modeli yüklü değil. Komut isteminde: ollama pull {_model}");
                        throw new InvalidOperationException($"Ollama hata döndürdü ({(int)response.StatusCode}): {error}");
                    }
                    return ParseReply(await ReadStreamAsync(response, onTextDelta, ct).ConfigureAwait(false));
                }
            }
        }

        private static readonly HashSet<string> TextOnlyModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public const string NoVisionNote =
            "[Sistem notu: Bu mesajla bir görüntü (resim, ekran görüntüsü ya da PDF sayfası) gönderildi ama şu anki model " +
            "görüntü göremiyor. Görüntünün içeriğini tahmin etme. Kullanıcıya bunu söyle ve görüntü için Ayarlar'dan " +
            "Google Gemini'yi seçmesini öner; işi görüntüsüz yapabiliyorsan (ör. nesneleri find_objects ile okuyarak) devam et.]";

        /// <summary>Mesajlardaki görüntüleri çıkarır, yerlerine not ekler. Görüntü varsa true.</summary>
        private static bool StripImages(JArray messages)
        {
            bool any = false;
            foreach (var m in messages.OfType<JObject>())
            {
                if (m["images"] == null) continue;
                m.Remove("images");
                m["content"] = ((string)m["content"] ?? "") + "\n" + NoVisionNote;
                any = true;
            }
            return any;
        }

        /// <summary>
        /// Ollama akışı satır başına bir JSON'dur; metin parçaları message.content'te, araç çağrıları
        /// message.tool_calls'ta gelir, son satırda done=true olur. Sonunda akışsız cevap biçimi kurulur.
        /// </summary>
        private static async Task<JObject> ReadStreamAsync(HttpResponseMessage response, Action<string> onTextDelta, CancellationToken ct)
        {
            var content = new StringBuilder();
            var toolCalls = new JArray();
            string doneReason = null;

            using (ct.Register(response.Dispose))
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var chunk = JObject.Parse(line);
                    if (chunk["error"] != null)
                        throw new InvalidOperationException("Ollama akış sırasında hata döndürdü: " + (string)chunk["error"]);

                    var message = chunk["message"];
                    var text = (string)message?["content"];
                    if (!string.IsNullOrEmpty(text))
                    {
                        content.Append(text);
                        onTextDelta?.Invoke(text);
                    }
                    if (message?["tool_calls"] is JArray calls)
                        foreach (var call in calls) toolCalls.Add(call);

                    if ((bool?)chunk["done"] == true)
                    {
                        doneReason = (string)chunk["done_reason"];
                        break;
                    }
                }
            }

            var result = new JObject { ["content"] = content.ToString() };
            if (toolCalls.Count > 0) result["tool_calls"] = toolCalls;
            return new JObject { ["message"] = result, ["done_reason"] = doneReason };
        }

        private static LlmReply ParseReply(JObject json)
        {
            var message = json["message"] as JObject ?? new JObject();
            var turn = new ChatTurn { Kind = TurnKind.Assistant };
            turn.Text = ThinkTags.Replace((string)message["content"] ?? "", "").Trim();

            if (message["tool_calls"] is JArray calls)
            {
                foreach (var call in calls)
                {
                    var fn = call["function"];
                    var args = fn?["arguments"];
                    turn.ToolCalls.Add(new ToolCall
                    {
                        // Ollama'nın kendi API'si çağrı kimliği üretmez.
                        Id = (string)call["id"] ?? "call_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                        Name = (string)fn?["name"],
                        Arguments = args as JObject ?? ParseArgumentString((string)args),
                    });
                }
            }

            return new LlmReply
            {
                Assistant = turn,
                Truncated = (string)json["done_reason"] == "length",
            };
        }

        private static JObject ParseArgumentString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new JObject();
            try { return JObject.Parse(s); }
            catch (JsonException) { return new JObject { ["_parse_error"] = s }; }
        }

        private static IEnumerable<JObject> ToMessages(ChatTurn turn)
        {
            switch (turn.Kind)
            {
                case TurnKind.User:
                    yield return WithImages(new JObject { ["role"] = "user", ["content"] = turn.Text }, turn.Images);
                    break;

                case TurnKind.Assistant:
                    var msg = new JObject { ["role"] = "assistant", ["content"] = turn.Text ?? "" };
                    if (turn.ToolCalls.Count > 0)
                    {
                        msg["tool_calls"] = new JArray(turn.ToolCalls.Select(c => new JObject
                        {
                            ["function"] = new JObject { ["name"] = c.Name, ["arguments"] = c.Arguments },
                        }));
                    }
                    yield return msg;
                    break;

                default:
                    foreach (var r in turn.ToolResults)
                    {
                        yield return new JObject
                        {
                            ["role"] = "tool",
                            ["tool_name"] = r.ToolName,
                            ["content"] = r.IsError ? "HATA: " + r.Content : r.Content,
                        };
                    }
                    var images = turn.ToolResults.SelectMany(r => r.Images).ToList();
                    if (images.Count > 0)
                        yield return WithImages(new JObject { ["role"] = "user", ["content"] = "(Araçların döndürdüğü görüntüler)" }, images);
                    break;
            }
        }

        // Ollama görüntüleri mesajın "images" alanında düz base64 olarak alır (yalnızca görüntü destekli modellerde işe yarar).
        private static JObject WithImages(JObject message, List<ImageAttachment> images)
        {
            if (images.Count > 0) message["images"] = new JArray(images.Select(i => i.Base64));
            return message;
        }
    }
}
