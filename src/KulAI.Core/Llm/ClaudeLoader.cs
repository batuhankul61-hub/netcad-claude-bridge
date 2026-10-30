using System;
using System.IO;
using System.Reflection;

namespace KulAI.Core.Llm
{
    /// <summary>
    /// Claude sağlayıcısını (KulAI.Claude.dll) yükler.
    /// .NET 8 (ör. Civil 3D 2025+): Anthropic SDK'sının istediği System.Text.Json 10, barındıran programın yüklediği
    /// 8.0 ile aynı bağlamda yüklenemez. Bu yüzden DLL ve bağımlılıkları izole bir AssemblyLoadContext'e
    /// alınır; yalnızca ortak sözleşme (KulAI.Core.dll, Newtonsoft.Json) barındıran programın bağlamından paylaşılır.
    /// .NET Framework (Civil 3D 2022–2024, NETCAD 8): normal yükleme; bağımlılıkları barındıran eklentinin AssemblyResolve'u bulur.
    /// </summary>
    internal static class ClaudeLoader
    {
        private const string AssemblyFile = "KulAI.Claude.dll";
        private const string ProviderType = "KulAI.Claude.AnthropicProvider";

        private static Assembly _assembly;

        public static ILlmProvider Create(string apiKey, string model)
        {
            var type = LoadAssembly().GetType(ProviderType, throwOnError: true);
            try
            {
                return (ILlmProvider)Activator.CreateInstance(type, apiKey, model);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // Kurucudaki "API anahtarı girilmemiş" gibi hatalar kullanıcıya olduğu gibi gitsin.
                throw ex.InnerException;
            }
        }

        private static Assembly LoadAssembly()
        {
            if (_assembly != null) return _assembly;

            var path = Path.Combine(AppEnvironment.PluginDirectory, AssemblyFile);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Claude bileşeni bulunamadı ({AssemblyFile}). Eklentiyi yeniden kurun.");

#if NET
            _assembly = new IsolatedContext(AppEnvironment.PluginDirectory).LoadFromAssemblyPath(path);
#else
            _assembly = Assembly.LoadFrom(path);
#endif
            return _assembly;
        }

#if NET
        private sealed class IsolatedContext : System.Runtime.Loader.AssemblyLoadContext
        {
            private readonly string _directory;

            public IsolatedContext(string directory) : base("KulAI.Claude", isCollectible: false)
            {
                _directory = directory;
            }

            protected override Assembly Load(AssemblyName name)
            {
                // Ortak tipler (ILlmProvider, ChatTurn, JObject) barındıran programın bağlamındaki kopyadan gelmeli,
                // yoksa tip kimliği uyuşmaz ve dönüşüm başarısız olur.
                if (name.Name == "KulAI.Core" || name.Name == "Newtonsoft.Json")
                    return null;

                // SDK ve bağımlılıkları (System.Text.Json 10 vb.) eklenti klasöründen, bu bağlama.
                var candidate = Path.Combine(_directory, name.Name + ".dll");
                return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
            }
        }
#endif
    }
}
