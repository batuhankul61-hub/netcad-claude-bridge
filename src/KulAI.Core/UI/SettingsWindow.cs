using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KulAI.Core.Settings;

namespace KulAI.Core.UI
{
    /// <summary>
    /// Model ayarları. Önerilen seçenek Gemini (yalnızca anahtar ister); yerel Ollama, Claude ve
    /// OpenAI uyumlu servisler "Gelişmiş" altında.
    /// </summary>
    public sealed class SettingsWindow : Window
    {
        private const string GeminiKeyPage = "https://aistudio.google.com/app/apikey";

        private readonly AppSettings _settings = AppSettings.Load();

        // Seçenekler ayrı kutularda durduğu için aynı grup adı şart; yoksa birden çok seçenek işaretli kalır.
        private const string ProviderGroup = "KulAIProvider";
        private readonly RadioButton _gemini = new RadioButton { GroupName = ProviderGroup, Content = "Google Gemini (önerilen) — ücretsiz anahtar yeterli" };
        private readonly RadioButton _ollama = new RadioButton { GroupName = ProviderGroup, Content = "Yerel model (Ollama) — internet gerekmez, güçlü ekran kartı ister" };
        private readonly RadioButton _anthropic = new RadioButton { GroupName = ProviderGroup, Content = "Claude (kendi API anahtarınız)" };
        private readonly RadioButton _openai = new RadioButton { GroupName = ProviderGroup, Content = "OpenAI uyumlu servis (OpenAI, Groq, OpenRouter…)" };

        private readonly PasswordBox _geminiKey = new PasswordBox();
        private readonly TextBox _geminiModel = new TextBox();
        private readonly TextBox _geminiFallbacks = new TextBox();
        private readonly ComboBox _geminiEffort = new ComboBox
        {
            Width = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = new[] { "low (hızlı, önerilen)", "medium", "high (yavaş)", "minimal" },
            ToolTip = "Modelin cevap vermeden önce ne kadar düşüneceği. Çizim komutları için low yeterli ve hızlıdır.",
        };
        private readonly TextBox _rules = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly TextBox _confirmAbove = new TextBox { Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBox _ollamaUrl = new TextBox();
        private readonly TextBox _ollamaModel = new TextBox();
        private readonly TextBox _anthropicModel = new TextBox();
        private readonly PasswordBox _anthropicKey = new PasswordBox();
        private readonly TextBox _openaiUrl = new TextBox();
        private readonly TextBox _openaiModel = new TextBox();
        private readonly PasswordBox _openaiKey = new PasswordBox();

        public SettingsWindow()
        {
            Title = AppEnvironment.ProductName + " — Ayarlar";
            Width = 540;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _gemini.IsChecked = _settings.Provider == ProviderKind.Gemini;
            _ollama.IsChecked = _settings.Provider == ProviderKind.Ollama;
            _anthropic.IsChecked = _settings.Provider == ProviderKind.Anthropic;
            _openai.IsChecked = _settings.Provider == ProviderKind.OpenAiCompatible;

            _geminiKey.Password = _settings.GeminiApiKey;
            _geminiModel.Text = _settings.GeminiModel;
            _geminiFallbacks.Text = _settings.GeminiFallbackModels;
            _geminiEffort.SelectedIndex = System.Math.Max(0, System.Array.FindIndex((string[])_geminiEffort.ItemsSource,
                i => i.Split(' ')[0] == (_settings.GeminiReasoningEffort ?? "low")));
            _rules.Text = _settings.CustomInstructions;
            _confirmAbove.Text = _settings.ConfirmAboveObjectCount.ToString();
            _ollamaUrl.Text = _settings.OllamaUrl;
            _ollamaModel.Text = _settings.OllamaModel;
            _anthropicModel.Text = _settings.AnthropicModel;
            _anthropicKey.Password = _settings.AnthropicApiKey;
            _openaiUrl.Text = _settings.OpenAiUrl;
            _openaiModel.Text = _settings.OpenAiModel;
            _openaiKey.Password = _settings.OpenAiApiKey;

            var stack = new StackPanel { Margin = new Thickness(14) };

            // Önerilen: Gemini
            var link = new Hyperlink(new Run("aistudio.google.com/app/apikey"));
            link.Click += (s, e) => Process.Start(new ProcessStartInfo(GeminiKeyPage) { UseShellExecute = true });
            var help = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 2, 0, 4) };
            help.Inlines.Add(new Run("Anahtar almak için Google hesabınızla "));
            help.Inlines.Add(link);
            help.Inlines.Add(new Run(" sayfasında \"Create API key\" deyin ve anahtarı aşağıya yapıştırın."));
            stack.Children.Add(Section(_gemini,
                help,
                Row("API anahtarı", _geminiKey),
                Row("Model", _geminiModel),
                Row("Yedek modeller", _geminiFallbacks),
                Row("Düşünme seviyesi", _geminiEffort)));

            // Firma standartları ve güvenlik
            var rulesPanel = new StackPanel { Margin = new Thickness(4) };
            rulesPanel.Children.Add(new TextBlock
            {
                Text = "Asistanın her işte uyacağı kurallar, her satıra bir tane. Ör: \"Bina katmanının adı BINA_MEVCUT, rengi kırmızı\". " +
                       "Sohbette \"bunu hatırla\" dediğiniz kurallar da buraya eklenir.",
                TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 4),
            });
            rulesPanel.Children.Add(_rules);
            rulesPanel.Children.Add(Row("Onay sınırı", _confirmAbove));
            rulesPanel.Children.Add(new TextBlock
            {
                Text = "Bundan fazla nesne oluşturacak ya da nesne silecek işlerden önce onay sorulur.",
                TextWrapping = TextWrapping.Wrap, FontSize = 11,
            });
            stack.Children.Add(new GroupBox { Header = "Firma standartları ve güvenlik", Content = rulesPanel, Margin = new Thickness(0, 0, 0, 10) });

            // Gelişmiş: diğer sağlayıcılar
            var advanced = new StackPanel();
            advanced.Children.Add(Section(_ollama,
                Row("Sunucu adresi", _ollamaUrl),
                Row("Model", _ollamaModel)));
            advanced.Children.Add(Section(_anthropic,
                Row("Model", _anthropicModel),
                Row("API anahtarı", _anthropicKey)));
            advanced.Children.Add(Section(_openai,
                Row("Taban adres", _openaiUrl),
                Row("Model", _openaiModel),
                Row("API anahtarı", _openaiKey)));
            stack.Children.Add(new Expander
            {
                Header = "Gelişmiş: diğer model seçenekleri",
                Content = advanced,
                IsExpanded = _settings.Provider != ProviderKind.Gemini,
                Margin = new Thickness(0, 0, 0, 10),
            });

            stack.Children.Add(new TextBlock
            {
                Text = "API anahtarları bu bilgisayarda Windows hesabınıza bağlı olarak şifreli saklanır.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 10),
            });

            var save = new Button { Content = "Kaydet", Width = 90, IsDefault = true };
            save.Click += (s, e) => { Save(); DialogResult = true; };
            var cancel = new Button { Content = "Vazgeç", Width = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            stack.Children.Add(buttons);

            Content = stack;
        }

        private void Save()
        {
            _settings.Provider = _ollama.IsChecked == true ? ProviderKind.Ollama
                               : _anthropic.IsChecked == true ? ProviderKind.Anthropic
                               : _openai.IsChecked == true ? ProviderKind.OpenAiCompatible
                               : ProviderKind.Gemini;
            _settings.GeminiApiKey = _geminiKey.Password.Trim();
            _settings.GeminiModel = _geminiModel.Text.Trim();
            _settings.GeminiFallbackModels = _geminiFallbacks.Text.Trim();
            _settings.GeminiReasoningEffort = ((_geminiEffort.SelectedItem as string) ?? "low").Split(' ')[0];
            _settings.CustomInstructions = _rules.Text.Trim();
            if (int.TryParse(_confirmAbove.Text.Trim(), out var confirmAbove) && confirmAbove > 0)
                _settings.ConfirmAboveObjectCount = confirmAbove;
            _settings.OllamaUrl = _ollamaUrl.Text.Trim();
            _settings.OllamaModel = _ollamaModel.Text.Trim();
            _settings.AnthropicModel = _anthropicModel.Text.Trim();
            _settings.AnthropicApiKey = _anthropicKey.Password.Trim();
            _settings.OpenAiUrl = _openaiUrl.Text.Trim();
            _settings.OpenAiModel = _openaiModel.Text.Trim();
            _settings.OpenAiApiKey = _openaiKey.Password.Trim();
            _settings.Save();
        }

        private static GroupBox Section(RadioButton header, params UIElement[] rows)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };
            foreach (var r in rows) panel.Children.Add(r);
            return new GroupBox { Header = header, Content = panel, Margin = new Thickness(0, 0, 0, 10) };
        }

        private static UIElement Row(string label, Control input)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(input, 1);
            grid.Children.Add(text);
            grid.Children.Add(input);
            return grid;
        }
    }
}
