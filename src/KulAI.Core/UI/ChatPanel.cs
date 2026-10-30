using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KulAI.Core.Agent;
using KulAI.Core.Llm;
using KulAI.Core.Settings;
using KulAI.Core.Tools;

namespace KulAI.Core.UI
{
    /// <summary>
    /// Sohbet arayüzü (kod ile kurulmuş WPF). Ürün kendi araç setini, talimatını, örnek isteğini ve
    /// ayarlar penceresini açma yolunu verir; Civil 3D paletinde ya da NETCAD panelinde barındırılır.
    /// </summary>
    public sealed class ChatPanel : UserControl
    {
        private readonly AssistantAgent _agent;
        private readonly Action _showSettings;
        private readonly StackPanel _messages = new StackPanel { Margin = new Thickness(8) };
        private readonly ScrollViewer _scroll;
        private readonly TextBox _input;
        private readonly TextBlock _attachments;
        private readonly System.Collections.Generic.List<ImageAttachment> _pendingImages = new System.Collections.Generic.List<ImageAttachment>();
        private readonly Button _send;
        private readonly TextBlock _providerLabel;
        private CancellationTokenSource _cts;
        private ConversationLog _log = new ConversationLog("chat");
        private Border _streamingBubble;
        private TextBox _streamingText;

        private static readonly Brush UserBg = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
        private static readonly Brush AssistantBg = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x47));
        private static readonly Brush PanelBg = new SolidColorBrush(Color.FromRgb(0x25, 0x29, 0x30));
        private static readonly Brush MutedFg = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB0));
        private static readonly Brush ErrorFg = new SolidColorBrush(Color.FromRgb(0xF0, 0x80, 0x80));

        public ChatPanel(IToolHost tools, string productPrompt, string exampleRequest, Action showSettings)
        {
            _showSettings = showSettings;
            _agent = new AssistantAgent(tools, Dispatcher, productPrompt) { ConfirmAsync = ShowConfirmation };
            // Akış: parçalar tek balonda birikir; tur bitince balon temizlenmiş tam metinle değişir.
            _agent.AssistantTextDelta += delta =>
            {
                if (_streamingText == null)
                    (_streamingBubble, _streamingText) = AddBubble("", AssistantBg, HorizontalAlignment.Left);
                _streamingText.AppendText(delta);
                _scroll.ScrollToEnd();
            };
            _agent.AssistantText += text =>
            {
                if (_streamingText != null) _streamingText.Text = text;
                else AddBubble(text, AssistantBg, HorizontalAlignment.Left);
                _streamingText = null;
                _streamingBubble = null;
            };
            _agent.AssistantTextRetracted += () =>
            {
                if (_streamingBubble != null) _messages.Children.Remove(_streamingBubble);
                _streamingText = null;
                _streamingBubble = null;
            };
            _agent.ToolStarted += call => AddNote("⚙ " + call.Name + " " + call.Arguments.ToString(Newtonsoft.Json.Formatting.None), MutedFg);
            _agent.ToolFinished += r => { if (r.IsError) AddNote("✖ " + r.Content, ErrorFg); };
            // Her "Yeni sohbet" yeni bir log dosyası başlatır; olaylar o anki log'a yazılır.
            _agent.AssistantText += text => _log.Write("ASİSTAN: " + text);
            _agent.ToolStarted += call => _log.Write("ARAÇ: " + call.Name + " " + call.Arguments.ToString(Newtonsoft.Json.Formatting.None));
            _agent.ToolFinished += r => _log.Write((r.IsError ? "HATA: " : "SONUÇ: ") + r.Content);

            Background = PanelBg;

            // Üst çubuk: sağlayıcı + ayarlar + yeni sohbet
            _providerLabel = new TextBlock { Foreground = MutedFg, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var settingsBtn = SmallButton("Ayarlar", (s, e) => _showSettings());
            var newChatBtn = SmallButton("Yeni sohbet", (s, e) =>
            {
                _agent.Reset();
                _messages.Children.Clear();
                _log = new ConversationLog("chat");
            });
            var header = new DockPanel { Margin = new Thickness(8, 6, 8, 6), LastChildFill = true };
            DockPanel.SetDock(settingsBtn, Dock.Right);
            DockPanel.SetDock(newChatBtn, Dock.Right);
            header.Children.Add(settingsBtn);
            header.Children.Add(newChatBtn);
            header.Children.Add(_providerLabel);

            _scroll = new ScrollViewer { Content = _messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            // Alt kısım: giriş kutusu + gönder
            _input = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 56,
                MaxHeight = 160,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(6),
                FontSize = 13,
            };
            _input.PreviewKeyDown += OnInputKeyDown;
            _send = new Button { Content = "Gönder", Width = 80, Margin = new Thickness(6, 0, 0, 0) };
            _send.Click += (s, e) => OnSendOrStop();
            _attachments = new TextBlock
            {
                Foreground = MutedFg,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed,
                ToolTip = "Eklenen resimleri kaldırmak için tıklayın",
            };
            _attachments.MouseLeftButtonUp += (s, e) => { _pendingImages.Clear(); UpdateAttachments(); };
            var footer = new DockPanel { Margin = new Thickness(8) };
            DockPanel.SetDock(_attachments, Dock.Top);
            DockPanel.SetDock(_send, Dock.Right);
            footer.Children.Add(_attachments);
            footer.Children.Add(_send);
            footer.Children.Add(_input);

            // Dosya bırakma: resimler modele gösterilir, diğer dosyaların (PDF, Excel, CSV) yolu mesaja eklenir.
            AllowDrop = true;
            _input.AllowDrop = true;
            PreviewDragOver += OnDragOver;
            PreviewDrop += OnDrop;
            _input.PreviewDragOver += OnDragOver;
            _input.PreviewDrop += OnDrop;

            var root = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(header);
            root.Children.Add(footer);
            root.Children.Add(_scroll);
            Content = root;

            RefreshProvider();
            if (!string.IsNullOrEmpty(exampleRequest))
                AddNote("Örnek: \"" + exampleRequest + "\"", MutedFg);
        }

        public void FocusInput() => _input.Focus();

        public void RefreshProvider()
        {
            try { _providerLabel.Text = ProviderFactory.Create(AppSettings.Load()).DisplayName; }
            catch (Exception ex) { _providerLabel.Text = "Ayar gerekli: " + ex.Message; }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return;
            e.Handled = true;
            foreach (var file in files)
            {
                if (Media.ImageFiles.IsImage(file))
                {
                    try { _pendingImages.Add(Media.ImageFiles.FromFile(file)); }
                    catch (Exception ex) { AddNote($"Resim okunamadı ({System.IO.Path.GetFileName(file)}): {ex.Message}", ErrorFg); }
                }
                else
                {
                    var quoted = "\"" + file + "\"";
                    _input.Text = _input.Text.Length == 0 ? quoted + " " : _input.Text.TrimEnd() + " " + quoted + " ";
                }
            }
            UpdateAttachments();
            _input.CaretIndex = _input.Text.Length;
            _input.Focus();
        }

        private bool TryPasteImage()
        {
            if (!Clipboard.ContainsImage()) return false;
            try
            {
                _pendingImages.Add(Media.ImageFiles.FromBitmap(Clipboard.GetImage(), 1600, "Panodan yapıştırılan resim"));
                UpdateAttachments();
            }
            catch (Exception ex) { AddNote("Resim yapıştırılamadı: " + ex.Message, ErrorFg); }
            return true;
        }

        private void UpdateAttachments()
        {
            _attachments.Text = $"📎 {_pendingImages.Count} resim eklendi (kaldırmak için tıklayın)";
            _attachments.Visibility = _pendingImages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+V: panoda resim varsa (ör. ekran alıntısı) mesaja eklenir.
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && TryPasteImage())
            {
                e.Handled = true;
                return;
            }
            // Enter gönderir, Shift+Enter yeni satır.
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                if (_cts == null) OnSendOrStop();
            }
        }

        private async void OnSendOrStop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                return;
            }

            var text = _input.Text.Trim();
            if (text.Length == 0 && _pendingImages.Count > 0) text = "Bu resme bak.";
            if (text.Length == 0) return;

            var settings = AppSettings.Load();
            if (AppSettings.LastLoadError != null)
            {
                AddNote(AppSettings.LastLoadError, ErrorFg);
                _log.Write("UYARI: " + AppSettings.LastLoadError);
            }
            ILlmProvider provider;
            try
            {
                provider = ProviderFactory.Create(settings);
                if (provider is FallbackProvider fallback)
                    fallback.SwitchedModel += note => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        AddNote(note, MutedFg);
                        _log.Write("MODEL DEĞİŞTİ: " + note);
                        RefreshProvider();
                    }));
            }
            catch (Exception ex) { AddNote(ex.Message, ErrorFg); return; }

            _input.Clear();
            var images = new System.Collections.Generic.List<ImageAttachment>(_pendingImages);
            _pendingImages.Clear();
            UpdateAttachments();
            AddBubble(images.Count > 0 ? $"{text}\n📎 {images.Count} resim" : text, UserBg, HorizontalAlignment.Right);
            if (images.Count > 0 && provider is OllamaProvider)
                AddNote("Not: Yerel model (Ollama) çoğunlukla resim göremez. Resimleri okutmak için Ayarlar'dan Google Gemini'yi seçin.", MutedFg);
            _cts = new CancellationTokenSource();
            _send.Content = "Durdur";
            var thinking = AddNote("Düşünüyor…", MutedFg);
            _log.Write("SAĞLAYICI: " + provider.DisplayName);
            _log.Write("KULLANICI: " + text + (images.Count > 0 ? $" [{images.Count} resim]" : ""));

            try
            {
                await _agent.RunAsync(provider, text, settings.MaxToolIterations, _cts.Token, images);
            }
            catch (OperationCanceledException)
            {
                AddNote("Durduruldu.", MutedFg);
                _log.Write("DURDURULDU");
            }
            catch (Exception ex)
            {
                AddNote("Hata: " + ex.Message, ErrorFg);
                _log.Write("İSTİSNA: " + ex);
            }
            finally
            {
                // Durdurulduysa yarım kalan akış balonu olduğu gibi kalır; sonraki tur yeni balon açar.
                _streamingText = null;
                _streamingBubble = null;
                _messages.Children.Remove(thinking);
                _cts.Dispose();
                _cts = null;
                _send.Content = "Gönder";
                _input.Focus();
            }
        }

        private (Border bubble, TextBox body) AddBubble(string text, Brush background, HorizontalAlignment align)
        {
            var body = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                FontSize = 13,
            };
            var bubble = new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(align == HorizontalAlignment.Right ? 40 : 0, 4, align == HorizontalAlignment.Left ? 40 : 0, 4),
                HorizontalAlignment = align,
                Child = body,
            };
            _messages.Children.Add(bubble);
            _scroll.ScrollToEnd();
            return (bubble, body);
        }

        /// <summary>
        /// Onay kartı: ne yapılacağını gösterir, kullanıcı "Onayla" ya da "İptal" diyene kadar bekler.
        /// Ajan bu sırada çizime dokunmaz.
        /// </summary>
        private Task<bool> ShowConfirmation(ToolCall call, string description)
        {
            var tcs = new TaskCompletionSource<bool>();
            var approve = new Button { Content = "Onayla", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 6, 6, 0) };
            var cancel = new Button { Content = "İptal", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 6, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(approve);
            buttons.Children.Add(cancel);
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = "Onay gerekiyor", FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            body.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 0) });
            body.Children.Add(buttons);
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x6B, 0x4E, 0x16)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 40, 4),
                Child = body,
            };
            void Finish(bool ok)
            {
                approve.IsEnabled = cancel.IsEnabled = false;
                body.Children.Add(new TextBlock { Text = ok ? "✔ Onaylandı" : "✖ İptal edildi", Foreground = MutedFg, Margin = new Thickness(0, 4, 0, 0) });
                _log.Write((ok ? "ONAYLANDI: " : "İPTAL: ") + description);
                tcs.TrySetResult(ok);
            }
            approve.Click += (s, e) => Finish(true);
            cancel.Click += (s, e) => Finish(false);
            _cts?.Token.Register(() => Dispatcher.BeginInvoke(new Action(() => Finish(false))));
            _messages.Children.Add(card);
            _scroll.ScrollToEnd();
            _log.Write("ONAY SORULDU: " + description);
            return tcs.Task;
        }

        private UIElement AddNote(string text, Brush foreground)
        {
            var note = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 2, 2, 2),
            };
            _messages.Children.Add(note);
            _scroll.ScrollToEnd();
            return note;
        }

        private static Button SmallButton(string text, RoutedEventHandler onClick)
        {
            var b = new Button { Content = text, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            b.Click += onClick;
            return b;
        }
    }
}
