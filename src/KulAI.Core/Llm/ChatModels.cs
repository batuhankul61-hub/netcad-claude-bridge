using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace KulAI.Core.Llm
{
    // Sağlayıcıdan bağımsız sohbet modeli. Her sağlayıcı bunu kendi API biçimine çevirir;
    // böylece ajan döngüsü ve araçlar hangi modelin kullanıldığını bilmez.

    public enum TurnKind { User, Assistant, ToolResults }

    public sealed class ToolCall
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public JObject Arguments { get; set; } = new JObject();
    }

    /// <summary>Modele gösterilen görüntü (çizim ekranı, PDF sayfası, kullanıcının eklediği resim).</summary>
    public sealed class ImageAttachment
    {
        public string MediaType { get; set; } = "image/png";
        public string Base64 { get; set; }
        public string Label { get; set; }

        public string DataUri => "data:" + MediaType + ";base64," + Base64;
    }

    public sealed class ToolResult
    {
        public string ToolCallId { get; set; }
        public string ToolName { get; set; }
        public string Content { get; set; }
        public bool IsError { get; set; }

        /// <summary>Aracın modele göstermek istediği görüntüler (ör. ekran görüntüsü). Görüntü desteklemeyen modelde yok sayılır.</summary>
        public List<ImageAttachment> Images { get; } = new List<ImageAttachment>();
    }

    public sealed class ChatTurn
    {
        public TurnKind Kind { get; set; }
        public string Text { get; set; } = "";
        public List<ToolCall> ToolCalls { get; } = new List<ToolCall>();
        public List<ToolResult> ToolResults { get; } = new List<ToolResult>();

        /// <summary>Kullanıcı mesajına eklenen görüntüler.</summary>
        public List<ImageAttachment> Images { get; } = new List<ImageAttachment>();

        /// <summary>
        /// Asistan cevabının sağlayıcıya özgü ham içeriği (ör. Claude'un thinking blokları).
        /// Aynı sağlayıcıya geri gönderilirken değiştirilmeden kullanılır.
        /// </summary>
        public object ProviderPayload { get; set; }

        public static ChatTurn User(string text) => new ChatTurn { Kind = TurnKind.User, Text = text };
    }

    public sealed class ToolSpec
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public JObject InputSchema { get; set; }
    }

    public sealed class LlmReply
    {
        public ChatTurn Assistant { get; set; }

        /// <summary>Model güvenlik nedeniyle isteği reddetti (Claude stop_reason "refusal").</summary>
        public bool Refused { get; set; }

        /// <summary>Cevap max_tokens sınırında kesildi.</summary>
        public bool Truncated { get; set; }
    }

    public interface ILlmProvider
    {
        string DisplayName { get; }

        /// <summary>
        /// Modele bir tur gönderir. Cevap akış halinde gelir: metin parçaları geldikçe
        /// <paramref name="onTextDelta"/> çağrılır (arka plan iş parçacığından); dönüş değeri tam cevaptır.
        /// </summary>
        Task<LlmReply> SendAsync(string systemPrompt, IReadOnlyList<ChatTurn> history,
                                 IReadOnlyList<ToolSpec> tools, Action<string> onTextDelta, CancellationToken ct);
    }
}
