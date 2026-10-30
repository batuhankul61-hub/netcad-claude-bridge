using System.Linq;
using KulAI.Core.Settings;

namespace KulAI.Core.Llm
{
    public static class ProviderFactory
    {
        public static ILlmProvider Create(AppSettings s)
        {
            switch (s.Provider)
            {
                case ProviderKind.Anthropic:
                    return ClaudeLoader.Create(s.AnthropicApiKey, s.AnthropicModel);
                case ProviderKind.Gemini:
                    var key = s.GeminiApiKey;
                    if (string.IsNullOrWhiteSpace(key))
                        throw new System.InvalidOperationException(
                            "Gemini API anahtarı girilmemiş. Ayarlar'dan ekleyin (aistudio.google.com adresinden ücretsiz alınır).");
                    // Ana model + kota dolunca sırayla denenecek yedek modeller.
                    var models = new[] { s.GeminiModel }
                        .Concat((s.GeminiFallbackModels ?? "").Split(',').Select(m => m.Trim()))
                        .Where(m => m.Length > 0).Distinct(System.StringComparer.OrdinalIgnoreCase);
                    return new FallbackProvider(models.Select(m => new OpenAiCompatibleProvider("Gemini", AppSettings.GeminiUrl, key, m)
                    {
                        // Gemini 3'ün varsayılan yüksek düşünme seviyesi CAD komutlarında dakikalar sürebiliyor.
                        ReasoningEffort = s.GeminiReasoningEffort,
                        // Önizleme modelleri bazen hata vermeden dakikalarca bekletiyor; o durumda yedeğe geç.
                        FirstByteTimeout = System.TimeSpan.FromSeconds(45),
                    }));
                case ProviderKind.OpenAiCompatible:
                    return new OpenAiCompatibleProvider("OpenAI uyumlu", s.OpenAiUrl, s.OpenAiApiKey, s.OpenAiModel);
                default:
                    return new OllamaProvider(s.OllamaUrl, s.OllamaModel, s.OllamaContextLength);
            }
        }
    }
}
