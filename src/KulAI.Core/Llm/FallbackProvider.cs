using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KulAI.Core.Llm
{
    /// <summary>Sağlayıcının uzun süreli kotası (ör. günlük) doldu; aynı istek başka bir modelle denenebilir.</summary>
    public sealed class QuotaExceededException : InvalidOperationException
    {
        public QuotaExceededException(string message) : base(message) { }
    }

    /// <summary>
    /// Modelleri sırayla dener: ana modelin kotası dolunca (QuotaExceededException) sıradakine geçer ve
    /// oturum boyunca orada kalır. Ücretsiz Gemini katmanında kota her model için ayrı olduğundan pratik çözümdür.
    /// </summary>
    public sealed class FallbackProvider : ILlmProvider
    {
        private readonly IReadOnlyList<ILlmProvider> _providers;
        private int _current;

        // Kotası dolan ya da yanıt vermeyen modeller bir süre atlanır. Panel her mesajda sağlayıcıyı yeniden
        // kurduğu için bu bilgi örnekler arasında paylaşılır; yoksa her mesaj aynı beklemeyi yaşar.
        private static readonly Dictionary<string, DateTime> AvoidUntil = new Dictionary<string, DateTime>();
        private static readonly TimeSpan AvoidFor = TimeSpan.FromMinutes(30);

        public FallbackProvider(IEnumerable<ILlmProvider> providers)
        {
            _providers = providers.ToList();
            if (_providers.Count == 0) throw new ArgumentException("En az bir sağlayıcı gerekli.");
            lock (AvoidUntil)
            {
                while (_current < _providers.Count - 1 &&
                       AvoidUntil.TryGetValue(_providers[_current].DisplayName, out var until) && until > DateTime.UtcNow)
                    _current++;
            }
        }

        public string DisplayName => _providers[_current].DisplayName;

        /// <summary>Kota yüzünden modele geçildiğinde bildirilir (arayüz kullanıcıya not düşer).</summary>
        public event Action<string> SwitchedModel;

        public async Task<LlmReply> SendAsync(string systemPrompt, IReadOnlyList<ChatTurn> history,
                                              IReadOnlyList<ToolSpec> tools, Action<string> onTextDelta, CancellationToken ct)
        {
            while (true)
            {
                try
                {
                    return await _providers[_current].SendAsync(systemPrompt, history, tools, onTextDelta, ct).ConfigureAwait(false);
                }
                catch (QuotaExceededException ex) when (_current < _providers.Count - 1)
                {
                    lock (AvoidUntil) AvoidUntil[_providers[_current].DisplayName] = DateTime.UtcNow + AvoidFor;
                    AppEnvironment.Diagnostic?.Invoke($"{_providers[_current].DisplayName} {AvoidFor.TotalMinutes:0} dk atlanacak: {ex.Message}");
                    _current++;
                    SwitchedModel?.Invoke($"{ex.Message} → {_providers[_current].DisplayName} ile devam ediliyor.");
                }
            }
        }
    }
}
