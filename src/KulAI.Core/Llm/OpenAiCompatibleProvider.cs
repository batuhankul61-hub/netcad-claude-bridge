using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
    /// OpenAI "chat/completions" biçimini konuşan her servis: yerel Ollama (/v1),
    /// OpenAI, Gemini (OpenAI uyumlu uç nokta), Groq, OpenRouter vb.
    /// </summary>
    public sealed class OpenAiCompatibleProvider : ILlmProvider
    {
        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly Regex ThinkTags = new Regex("<think>.*?</think>", RegexOptions.Singleline);

        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _label;

        /// <summary>"minimal" | "low" | "medium" | "high"; boşsa gönderilmez (modelin varsayılanı).</summary>
        public string ReasoningEffort { get; set; }

        /// <summary>Bu sürede ilk yanıt gelmezse yeniden dene, sonra QuotaExceededException ile yedek modele bırak. Boşsa sınırsız.</summary>
        public TimeSpan? FirstByteTimeout { get; set; }

        public OpenAiCompatibleProvider(string label, string baseUrl, string apiKey, string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("Model adı girilmemiş. Ayarlar'dan ekleyin.");

            _label = label;
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;
            _model = model;
        }

        public string DisplayName => _label + " · " + _model;

        private static HttpClient CreateHttpClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            // Yerel modeller yavaş donanımda dakikalar sürebilir.
            return new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        }

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
            };
            if (!string.IsNullOrWhiteSpace(ReasoningEffort))
                body["reasoning_effort"] = ReasoningEffort.Trim();
            if (tools.Count > 0)
            {
                body["tools"] = new JArray(tools.Select(t => new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = t.InputSchema,
                    },
                }));
            }

            var payload = body.ToString(Formatting.None);

            // Bulut servisleri ara sıra 429 (kota) / 5xx (yoğunluk) döndürür; kullanıcının işi
            // yarıda kalmasın diye artan beklemeyle yeniden dene.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int attempt = 1; ; attempt++)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions"))
                {
                    if (!string.IsNullOrEmpty(_apiKey))
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                    HttpResponseMessage response;
                    using (var firstByte = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    try
                    {
                        if (FirstByteTimeout.HasValue) firstByte.CancelAfter(FirstByteTimeout.Value);
                        // Başlıklar gelince dön: gövde akış halinde okunur.
                        response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, firstByte.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested && FirstByteTimeout.HasValue)
                    {
                        // Sunucu bazen hata vermeden dakikalarca bekletiyor: kullanıcı beklemesin, yedek modele geç.
                        AppEnvironment.Diagnostic?.Invoke($"{DisplayName}: {FirstByteTimeout.Value.TotalSeconds:0} sn içinde yanıt yok.");
                        throw new QuotaExceededException($"{_label} ({_model}) şu an yanıt vermiyor");
                    }
                    catch (HttpRequestException ex)
                    {
                        AppEnvironment.Diagnostic?.Invoke($"{DisplayName}: bağlantı hatası ({ex.Message}), deneme {attempt}.");
                        if (attempt < MaxAttempts)
                        {
                            await Task.Delay(RetryDelay(attempt, null), ct).ConfigureAwait(false);
                            continue;
                        }
                        throw new InvalidOperationException($"{_label} sunucusuna bağlanılamadı ({_baseUrl}).", ex);
                    }

                    using (response)
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            var headersAt = clock.Elapsed.TotalSeconds;
                            var reply = ParseReply(await ReadStreamAsync(response, onTextDelta, ct).ConfigureAwait(false));
                            AppEnvironment.Diagnostic?.Invoke(
                                $"{DisplayName}: cevap {clock.Elapsed.TotalSeconds:0.0} sn (ilk bayt {headersAt:0.0} sn, deneme {attempt}).");
                            return reply;
                        }

                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        int status = (int)response.StatusCode;
                        // Günlük kota gibi uzun beklemeler yeniden denemeyle çözülmez (Gemini gövdede
                        // "retryDelay": "14494s" döndürür, başlıkta Retry-After vermez).
                        var serverDelay = response.Headers.RetryAfter?.Delta ?? ParseRetryDelay(json);
                        bool longWait = serverDelay.HasValue && serverDelay.Value > TimeSpan.FromMinutes(1);
                        bool transient = status == 408 || status == 429 || status >= 500;
                        if (transient && !longWait && attempt < MaxAttempts)
                        {
                            var delay = RetryDelay(attempt, serverDelay);
                            AppEnvironment.Diagnostic?.Invoke($"{DisplayName}: HTTP {status}, {delay.TotalSeconds:0} sn sonra yeniden denenecek (deneme {attempt}).");
                            await Task.Delay(delay, ct).ConfigureAwait(false);
                            continue;
                        }

                        if (status == 429 && longWait)
                            throw new QuotaExceededException(
                                $"{_label}: kullanım kotası doldu, yaklaşık {FormatWait(serverDelay.Value)} sonra yenilenecek. " +
                                "Ücretsiz katmanın sınırı düşük olabilir; başka bir model ya da ücretli plan kullanabilirsiniz.");
                        // Yeniden denemelere rağmen süren yoğunluk: yedek model varsa ona geçilsin (QuotaExceededException).
                        if (status == 429)
                            throw new QuotaExceededException($"{_label} ({_model}): istek kotası doldu. Biraz bekleyip tekrar deneyin.");
                        if (status >= 500)
                            throw new QuotaExceededException($"{_label} ({_model}) şu an yoğun ya da erişilemiyor ({status}). Biraz sonra tekrar deneyin.");
                        throw new InvalidOperationException($"{_label} hata döndürdü ({status}): {json}");
                    }
                }
            }
        }

        /// <summary>
        /// SSE akışını ("data: {...}" satırları) okur, metni parça parça bildirir ve sonunda akışsız
        /// cevapla aynı biçimde bir JSON kurar ({choices:[{message, finish_reason}]}).
        /// Araç çağrıları index'e göre parça parça gelir: arguments birleştirilir, diğer alanlar
        /// (id, type, Gemini'nin extra_content'i) aynen korunur.
        /// </summary>
        private static async Task<JObject> ReadStreamAsync(HttpResponseMessage response, Action<string> onTextDelta, CancellationToken ct)
        {
            var content = new StringBuilder();
            var calls = new SortedDictionary<int, JObject>();
            string finishReason = null;

            using (ct.Register(response.Dispose))
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!line.StartsWith("data:")) continue;
                    var data = line.Substring(5).Trim();
                    if (data == "[DONE]") break;
                    if (data.Length == 0) continue;

                    var chunk = JObject.Parse(data);
                    if (chunk["error"] != null)
                        throw new InvalidOperationException("Servis akış sırasında hata döndürdü: " + chunk["error"].ToString(Formatting.None));

                    var choice = chunk["choices"]?[0];
                    if (choice == null) continue;
                    finishReason = (string)choice["finish_reason"] ?? finishReason;
                    var delta = choice["delta"] as JObject;
                    if (delta == null) continue;

                    var text = (string)delta["content"];
                    if (!string.IsNullOrEmpty(text))
                    {
                        content.Append(text);
                        onTextDelta?.Invoke(text);
                    }

                    if (delta["tool_calls"] is JArray parts)
                    {
                        foreach (var part in parts.OfType<JObject>())
                        {
                            int index = (int?)part["index"] ?? calls.Count;
                            if (!calls.TryGetValue(index, out var call))
                                calls[index] = call = new JObject();
                            foreach (var prop in part.Properties())
                            {
                                if (prop.Name == "index") continue;
                                if (prop.Name == "function" && prop.Value is JObject fn)
                                {
                                    var target = call["function"] as JObject ?? (JObject)(call["function"] = new JObject());
                                    if (fn["name"] != null && string.IsNullOrEmpty((string)target["name"]))
                                        target["name"] = fn["name"];
                                    if (fn["arguments"] != null)
                                        target["arguments"] = ((string)target["arguments"] ?? "") + (string)fn["arguments"];
                                }
                                else
                                {
                                    call[prop.Name] = prop.Value;
                                }
                            }
                        }
                    }
                }
            }

            var message = new JObject { ["content"] = content.ToString() };
            if (calls.Count > 0) message["tool_calls"] = new JArray(calls.Values);
            return new JObject
            {
                ["choices"] = new JArray(new JObject { ["message"] = message, ["finish_reason"] = finishReason }),
            };
        }

        private const int MaxAttempts = 4;

        private static readonly Regex RetryDelayField = new Regex("\"retryDelay\"\\s*:\\s*\"(\\d+(?:\\.\\d+)?)s\"");

        private static TimeSpan? ParseRetryDelay(string body)
        {
            var m = RetryDelayField.Match(body ?? "");
            return m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                ? TimeSpan.FromSeconds(seconds)
                : (TimeSpan?)null;
        }

        private static string FormatWait(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours} saat {t.Minutes} dakika" : $"{Math.Max(1, (int)Math.Ceiling(t.TotalMinutes))} dakika";

        /// <summary>Sunucunun istediği bekleme (Retry-After) ya da 2, 5, 10 sn; en fazla 30 sn.</summary>
        private static TimeSpan RetryDelay(int attempt, TimeSpan? retryAfter)
        {
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
                return retryAfter.Value < TimeSpan.FromSeconds(30) ? retryAfter.Value : TimeSpan.FromSeconds(30);
            return TimeSpan.FromSeconds(attempt == 1 ? 2 : attempt == 2 ? 5 : 10);
        }

        private static LlmReply ParseReply(JObject json)
        {
            var choice = json["choices"]?[0];
            var message = choice?["message"] as JObject ?? new JObject();
            var turn = new ChatTurn { Kind = TurnKind.Assistant };

            var text = (string)message["content"] ?? "";
            turn.Text = ThinkTags.Replace(text, "").Trim();

            if (message["tool_calls"] is JArray calls)
            {
                foreach (var call in calls.OfType<JObject>())
                {
                    // Bazı modeller id üretmez; ham çağrıya da yaz ki geri gönderilen kopya tutarlı olsun.
                    if (string.IsNullOrEmpty((string)call["id"]))
                        call["id"] = "call_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                    var fn = call["function"];
                    turn.ToolCalls.Add(new ToolCall
                    {
                        Id = (string)call["id"],
                        Name = (string)fn?["name"],
                        Arguments = ParseArguments(fn?["arguments"]),
                    });
                }
                // Ham çağrılar aynen geri gönderilir: Gemini gibi servisler çağrılara ek alanlar
                // (ör. extra_content.google.thought_signature) koyar ve sonraki istekte bunları ister.
                turn.ProviderPayload = calls;
            }

            return new LlmReply
            {
                Assistant = turn,
                Truncated = (string)choice?["finish_reason"] == "length",
            };
        }

        private static JObject ParseArguments(JToken token)
        {
            if (token is JObject obj) return obj;
            var s = (string)token;
            if (string.IsNullOrWhiteSpace(s)) return new JObject();
            try { return JObject.Parse(s); }
            catch (JsonException) { return new JObject { ["_parse_error"] = s }; }
        }

        private static IEnumerable<JObject> ToMessages(ChatTurn turn)
        {
            switch (turn.Kind)
            {
                case TurnKind.User:
                    yield return new JObject { ["role"] = "user", ["content"] = WithImages(turn.Text, turn.Images) };
                    break;

                case TurnKind.Assistant:
                    var msg = new JObject { ["role"] = "assistant", ["content"] = turn.Text ?? "" };
                    if (turn.ProviderPayload is JArray rawCalls)
                    {
                        msg["tool_calls"] = rawCalls;
                    }
                    else if (turn.ToolCalls.Count > 0)
                    {
                        msg["tool_calls"] = new JArray(turn.ToolCalls.Select(c => new JObject
                        {
                            ["id"] = c.Id,
                            ["type"] = "function",
                            ["function"] = new JObject
                            {
                                ["name"] = c.Name,
                                ["arguments"] = c.Arguments.ToString(Formatting.None),
                            },
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
                            ["tool_call_id"] = r.ToolCallId,
                            ["content"] = r.IsError ? "HATA: " + r.Content : r.Content,
                        };
                    }
                    // "tool" mesajı görüntü taşıyamaz: araç görüntülerini hemen ardından kullanıcı mesajı olarak ver.
                    var images = turn.ToolResults.SelectMany(r => r.Images).ToList();
                    if (images.Count > 0)
                        yield return new JObject { ["role"] = "user", ["content"] = WithImages("(Araçların döndürdüğü görüntüler)", images) };
                    break;
            }
        }

        private static JToken WithImages(string text, List<ImageAttachment> images)
        {
            if (images == null || images.Count == 0) return text ?? "";
            var parts = new JArray { new JObject { ["type"] = "text", ["text"] = text ?? "" } };
            foreach (var img in images)
            {
                if (!string.IsNullOrEmpty(img.Label)) parts.Add(new JObject { ["type"] = "text", ["text"] = img.Label });
                parts.Add(new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = img.DataUri } });
            }
            return parts;
        }
    }
}
