using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace KulAI.Core.Settings
{
    public static class ProviderKind
    {
        public const string Ollama = "ollama";
        public const string Anthropic = "anthropic";
        public const string OpenAiCompatible = "openai";
        public const string Gemini = "gemini";
    }

    /// <summary>
    /// Kullanıcı ayarları: %APPDATA%\<ürün>\settings.json.
    /// API anahtarları Windows DPAPI ile kullanıcı hesabına bağlı şifrelenir.
    /// </summary>
    public sealed class AppSettings
    {
        // Yeni kurulumda Gemini: ücretsiz anahtarla hemen çalışır, güçlü ekran kartı gerektirmez.
        public string Provider { get; set; } = ProviderKind.Gemini;

        // B) Kendi anahtarını getir — Google Gemini (aistudio.google.com, ücretsiz katman var)
        public const string GeminiUrl = "https://generativelanguage.googleapis.com/v1beta/openai";
        public string GeminiModel { get; set; } = "gemini-3.6-flash";
        public string GeminiApiKeyProtected { get; set; } = "";

        /// <summary>
        /// Ana modelin günlük kotası dolunca sırayla denenecek modeller (virgülle). Ücretsiz katmanda kota
        /// her model için ayrı tutulduğundan, biri dolunca diğeri çoğu zaman hâlâ kullanılabilir.
        /// </summary>
        public string GeminiFallbackModels { get; set; } = "gemini-3.5-flash, gemini-3.5-flash-lite, gemini-3.1-flash-lite";

        /// <summary>Gemini düşünme seviyesi (minimal, low, medium, high). Boşsa modelin varsayılanı; "low" hızlı ve yeterli.</summary>
        public string GeminiReasoningEffort { get; set; } = "low";

        // A) Yerel model — ücretsiz, internet gerekmez
        public string OllamaUrl { get; set; } = "http://localhost:11434";
        public string OllamaModel { get; set; } = "qwen3:14b";

        /// <summary>Bağlam penceresi (token). Ollama varsayılanı 4096 çok adımlı işlere yetmez.</summary>
        public int OllamaContextLength { get; set; } = 16384;

        // B) Kendi anahtarını getir — Claude
        public string AnthropicModel { get; set; } = "claude-opus-5-5";
        public string AnthropicApiKeyProtected { get; set; } = "";

        // B) Kendi anahtarını getir — OpenAI uyumlu (OpenAI, Gemini, Groq, OpenRouter...)
        public string OpenAiUrl { get; set; } = "https://api.openai.com/v1";
        public string OpenAiModel { get; set; } = "";
        public string OpenAiApiKeyProtected { get; set; } = "";

        public int MaxToolIterations { get; set; } = 25;

        /// <summary>
        /// Firma standartları: asistanın her işte uyacağı kurallar (katman adları, renkler, yazı boyları…).
        /// Kullanıcı ayarlardan yazar ya da asistana "bunu hatırla" der (remember_standard aracı).
        /// </summary>
        public string CustomInstructions { get; set; } = "";

        /// <summary>Bu sayıdan fazla nesne oluşturacak ya da herhangi bir nesneyi silecek işler önce onay ister.</summary>
        public int ConfirmAboveObjectCount { get; set; } = 100;

        [JsonIgnore]
        public string AnthropicApiKey
        {
            get => Unprotect(AnthropicApiKeyProtected);
            set => AnthropicApiKeyProtected = Protect(value);
        }

        [JsonIgnore]
        public string GeminiApiKey
        {
            get => Unprotect(GeminiApiKeyProtected);
            set => GeminiApiKeyProtected = Protect(value);
        }

        [JsonIgnore]
        public string OpenAiApiKey
        {
            get => Unprotect(OpenAiApiKeyProtected);
            set => OpenAiApiKeyProtected = Protect(value);
        }

        /// <summary>Ayar dosyası henüz yok mu (ilk kullanım)?</summary>
        public static bool IsFirstRun => !File.Exists(SettingsPath);

        private static string SettingsPath =>
            Path.Combine(AppEnvironment.DataDirectory, "settings.json");

        /// <summary>Son Load() çağrısında ayar dosyası okunamadıysa nedeni; okunduysa null.</summary>
        public static string LastLoadError { get; private set; }

        public static AppSettings Load()
        {
            LastLoadError = null;
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsPath))
                           ?? new AppSettings();
            }
            catch (Exception ex)
            {
                // Bozuk ayar dosyası: varsayılanlarla devam et, ama nedenini sakla (panel ve log gösterir).
                LastLoadError = $"Ayar dosyası okunamadı, varsayılanlar kullanılıyor ({SettingsPath}): {ex.GetType().Name}: {ex.Message}";
            }
            return new AppSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        private static string Unprotect(string protectedText)
        {
            if (string.IsNullOrEmpty(protectedText)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return "";
            }
        }
    }
}
