using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using KulAI.Core.Llm;
using Newtonsoft.Json.Linq;

namespace KulAI.Claude
{
    /// <summary>Claude (Anthropic API) — kullanıcının kendi API anahtarıyla.</summary>
    public sealed class AnthropicProvider : ILlmProvider
    {
        private const string FallbackBeta = "server-side-fallback-2026-07-01";

        private readonly AnthropicClient _client;
        private readonly string _model;

        public AnthropicProvider(string apiKey, string model)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Claude API anahtarı girilmemiş. Ayarlar'dan ekleyin.");

            _client = new AnthropicClient { ApiKey = apiKey };
            _model = model;
        }

        public string DisplayName => "Claude · " + _model;

        // Haiku'da sunucu tarafı yedek model yok; diğer güncel modellerde reddedilen istek
        // Anthropic'in önerdiği modele otomatik yönlendirilir.
        private bool UseServerFallback => _model.IndexOf("haiku", StringComparison.OrdinalIgnoreCase) < 0;

        public async Task<LlmReply> SendAsync(string systemPrompt, IReadOnlyList<ChatTurn> history,
                                              IReadOnlyList<ToolSpec> tools, Action<string> onTextDelta, CancellationToken ct)
        {
            var parameters = new MessageCreateParams
            {
                Model = _model,
                // Akışlı istekte zaman aşımı sorunu olmadığı için geniş tutulur.
                MaxTokens = 64000,
                System = systemPrompt,
                Tools = tools.Select(ToBetaTool).ToList(),
                Messages = history.Select(ToMessageParam).ToList(),
            };
            if (UseServerFallback)
            {
                parameters = parameters with
                {
                    Betas = [FallbackBeta],
                    Fallbacks = new Default(), // "default": Anthropic reddedilen isteği önerdiği modele yönlendirir
                };
            }

            // Metin parçalarını arayüze aktarırken akışı SDK'nın birleştiricisiyle tam mesaja çevir
            // (thinking imzaları, araç girdileri ve fallback blokları dahil). SDK geçici hatalarda
            // (429, 5xx, bağlantı) kendisi yeniden dener; buraya gelenler kalıcı hatalardır.
            BetaMessage message;
            try
            {
                var stream = _client.Beta.Messages.CreateStreaming(parameters, ct);
                message = await Tap(stream, onTextDelta).Aggregate().ConfigureAwait(false);
            }
            catch (Anthropic.Exceptions.AnthropicUnauthorizedException)
            {
                throw new InvalidOperationException("Claude API anahtarı geçersiz. Ayarlar'dan anahtarı kontrol edin.");
            }
            catch (Anthropic.Exceptions.AnthropicForbiddenException)
            {
                throw new InvalidOperationException("Claude API anahtarının bu modele erişim izni yok. Ayarlar'dan farklı bir model deneyin.");
            }
            catch (Anthropic.Exceptions.AnthropicNotFoundException)
            {
                throw new InvalidOperationException($"'{_model}' modeli bulunamadı. Ayarlar'dan model adını kontrol edin.");
            }
            catch (Anthropic.Exceptions.AnthropicRateLimitException)
            {
                throw new InvalidOperationException("Claude kullanım sınırına ulaşıldı. Biraz bekleyip tekrar deneyin ya da hesabınızın kredisini kontrol edin.");
            }
            catch (Anthropic.Exceptions.Anthropic5xxException)
            {
                throw new InvalidOperationException("Claude şu an yoğun ya da erişilemiyor. Biraz sonra tekrar deneyin.");
            }
            catch (Anthropic.Exceptions.AnthropicIOException)
            {
                throw new InvalidOperationException("Claude'a bağlanılamadı. İnternet bağlantınızı kontrol edin.");
            }

            var stopReason = message.StopReason?.ToString() ?? "";
            if (stopReason.Equals("refusal", StringComparison.OrdinalIgnoreCase))
                return new LlmReply { Refused = true, Assistant = new ChatTurn { Kind = TurnKind.Assistant } };

            return new LlmReply
            {
                Assistant = ToAssistantTurn(message),
                Truncated = stopReason.Equals("max_tokens", StringComparison.OrdinalIgnoreCase),
            };
        }

        private static async IAsyncEnumerable<BetaRawMessageStreamEvent> Tap(
            IAsyncEnumerable<BetaRawMessageStreamEvent> source, Action<string> onTextDelta)
        {
            await foreach (var e in source.ConfigureAwait(false))
            {
                if (onTextDelta != null && e.TryPickContentBlockDelta(out var block) && block.Delta.TryPickText(out var text))
                    onTextDelta(text.Text);
                yield return e;
            }
        }

        private static ChatTurn ToAssistantTurn(BetaMessage message)
        {
            var turn = new ChatTurn { Kind = TurnKind.Assistant };
            var echo = new List<BetaContentBlockParam>();
            var texts = new List<string>();

            // Model ortada yedek modele geçtiyse (fallback bloğu), sınırdan önceki
            // thinking / tool_use blokları geri gönderilmez; metin blokları kalır.
            var blocks = message.Content.ToList();
            int lastFallback = blocks.FindLastIndex(b => b.TryPickFallback(out _));

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                bool beforeBoundary = i < lastFallback;

                if (block.TryPickText(out BetaTextBlock text))
                {
                    texts.Add(text.Text);
                    echo.Add(new BetaTextBlockParam { Text = text.Text });
                }
                else if (block.TryPickThinking(out BetaThinkingBlock thinking))
                {
                    if (!beforeBoundary)
                        echo.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                }
                else if (block.TryPickRedactedThinking(out BetaRedactedThinkingBlock redacted))
                {
                    if (!beforeBoundary)
                        echo.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
                }
                else if (block.TryPickToolUse(out BetaToolUseBlock toolUse))
                {
                    if (beforeBoundary) continue;
                    echo.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                    turn.ToolCalls.Add(new ToolCall
                    {
                        Id = toolUse.ID,
                        Name = toolUse.Name,
                        Arguments = JObject.Parse(JsonSerializer.Serialize(toolUse.Input)),
                    });
                }
                // fallback bloğu yalnızca işaretçidir; geri gönderilmesi gerekmez.
            }

            turn.Text = string.Join("\n", texts).Trim();
            turn.ProviderPayload = echo;
            return turn;
        }

        private static BetaMessageParam ToMessageParam(ChatTurn turn)
        {
            switch (turn.Kind)
            {
                case TurnKind.User:
                    if (turn.Images.Count == 0)
                        return new BetaMessageParam { Role = Role.User, Content = turn.Text };
                    var parts = turn.Images.Select(ImageBlock).ToList();
                    parts.Add(new BetaTextBlockParam { Text = turn.Text ?? "" });
                    return new BetaMessageParam { Role = Role.User, Content = parts };

                case TurnKind.Assistant:
                    var content = turn.ProviderPayload as List<BetaContentBlockParam> ?? RebuildAssistant(turn);
                    return new BetaMessageParam { Role = Role.Assistant, Content = content };

                default:
                    // Aynı turdaki tüm araç sonuçları tek kullanıcı mesajında gider; görüntüler araç sonuçlarının ardından.
                    var results = turn.ToolResults
                        .Select(r => (BetaContentBlockParam)new BetaToolResultBlockParam
                        {
                            ToolUseID = r.ToolCallId,
                            Content = r.Content,
                            IsError = r.IsError,
                        })
                        .ToList();
                    results.AddRange(turn.ToolResults.SelectMany(r => r.Images).Select(ImageBlock));
                    return new BetaMessageParam { Role = Role.User, Content = results };
            }
        }

        private static BetaContentBlockParam ImageBlock(ImageAttachment image) =>
            new BetaImageBlockParam
            {
                Source = new BetaBase64ImageSource
                {
                    Data = image.Base64,
                    MediaType = image.MediaType == "image/jpeg" ? MediaType.ImageJpeg : MediaType.ImagePng,
                },
            };

        // Başka bir sağlayıcıdan gelen asistan turunu Claude biçimine çevirir.
        private static List<BetaContentBlockParam> RebuildAssistant(ChatTurn turn)
        {
            var blocks = new List<BetaContentBlockParam>();
            if (!string.IsNullOrEmpty(turn.Text))
                blocks.Add(new BetaTextBlockParam { Text = turn.Text });
            foreach (var call in turn.ToolCalls)
            {
                blocks.Add(new BetaToolUseBlockParam
                {
                    ID = call.Id,
                    Name = call.Name,
                    Input = ToJsonElements(call.Arguments),
                });
            }
            return blocks;
        }

        private static BetaToolUnion ToBetaTool(ToolSpec spec)
        {
            var schema = spec.InputSchema;
            var properties = schema["properties"] as JObject ?? new JObject();
            var required = (schema["required"] as JArray)?.Select(t => (string)t).ToList() ?? new List<string>();

            return new BetaTool
            {
                Name = spec.Name,
                Description = spec.Description,
                // Büyük araç girdileri (ör. yüzlerce nokta) parça parça akar. Kesilmiş girdiye karşı
                // ajan, max_tokens ile biten turlarda araçları çalıştırmaz (AssistantAgent).
                EagerInputStreaming = true,
                InputSchema = new()
                {
                    Properties = ToJsonElements(properties),
                    Required = required,
                },
            };
        }

        private static Dictionary<string, JsonElement> ToJsonElements(JObject obj)
        {
            var dict = new Dictionary<string, JsonElement>();
            foreach (var prop in obj.Properties())
            {
                using (var doc = JsonDocument.Parse(prop.Value.ToString(Newtonsoft.Json.Formatting.None)))
                    dict[prop.Name] = doc.RootElement.Clone();
            }
            return dict;
        }
    }
}
