using System.Collections.Generic;
using KulAI.Core.Llm;

namespace KulAI.Core.Tools
{
    /// <summary>
    /// Ürüne özgü araç seti (Civil 3D, NETCAD…). Ajan yalnızca bu arayüzü bilir.
    /// Execute her zaman CAD programının ana iş parçacığında çağrılır.
    /// </summary>
    public interface IToolHost
    {
        IReadOnlyList<ToolSpec> Specs { get; }

        /// <summary>Aracı çalıştırır; hata olursa istisna fırlatmaz, IsError=true sonuç döner.</summary>
        ToolResult Execute(ToolCall call);

        /// <summary>
        /// Bu çağrı çalışmadan önce kullanıcı onayı gerektiriyorsa kullanıcıya gösterilecek açıklama
        /// (ör. "312 nokta oluşturulacak", "KUL_BINA katmanındaki 45 nesne silinecek"); gerekmiyorsa null.
        /// </summary>
        string GetConfirmation(ToolCall call, int confirmAboveObjectCount);
    }
}
