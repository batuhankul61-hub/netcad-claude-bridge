using System;
using System.IO;
using System.Text;

namespace KulAI.Core.Agent
{
    /// <summary>
    /// Bir sohbetin adımlarını %APPDATA%\<ürün>\logs altına yazar (destek ve hata ayıklama için).
    /// Log yazılamazsa sohbet etkilenmez.
    /// </summary>
    public sealed class ConversationLog
    {
        public static string Directory =>
            Path.Combine(AppEnvironment.DataDirectory, "logs");

        private readonly string _path;

        public event Action<string> LineWritten;

        public ConversationLog(string prefix)
        {
            _path = Path.Combine(Directory, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        }

        /// <summary>Ajanın metin, araç çağrısı ve sonuç olaylarını log'a bağlar.</summary>
        public void Attach(AssistantAgent agent)
        {
            agent.AssistantText += text => Write("ASİSTAN: " + text);
            agent.ToolStarted += call => Write("ARAÇ: " + call.Name + " " + call.Arguments.ToString(Newtonsoft.Json.Formatting.None));
            agent.ToolFinished += r => Write((r.IsError ? "HATA: " : "SONUÇ: ") + r.Content);
        }

        public void Write(string line)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}", Encoding.UTF8);
            }
            catch
            {
                // Disk dolu, izin yok vb.: log isteğe bağlıdır.
            }
            LineWritten?.Invoke(line);
        }
    }
}
