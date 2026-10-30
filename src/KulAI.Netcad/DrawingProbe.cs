using System;
using Netcad.Interop;
using Netcad.Interop.NMath;

namespace KulAI.Netcad
{
    /// <summary>
    /// Geçici deneme: NETCAD nesne API'siyle katman, çizgi ve nokta oluşturur.
    /// Yönü görmek için çizgi (0,0)'dan (X=100, Y=50)'ye gider.
    /// </summary>
    internal static class DrawingProbe
    {
        /// <summary>NETCAD API erişim zincirini halka halka kontrol eder.</summary>
        public static string Diagnose()
        {
            var sb = new System.Text.StringBuilder();
            void Try(string label, Func<object> f)
            {
                try { var v = f(); sb.AppendLine($"{label}: {(v == null ? "NULL" : v.GetType().FullName + " = " + v)}"); }
                catch (Exception ex) { sb.AppendLine($"{label}: İSTİSNA {ex.GetType().Name}: {ex.Message}"); }
            }
            Try("NetcadApplication.Current", () => NetcadApplication.Current);
            Try("NetcadApplication.CurrentCOM", () => NetcadApplication.CurrentCOM);
            Try("NetcadApplication.CurrentProject", () => NetcadApplication.CurrentProject);
            Try("CurrentCOM.CurrentProject", () => NetcadApplication.CurrentCOM?.CurrentProject);
            Try("NetcadContext.Nc", () => NetcadContext.Nc);
            Try("Proje dosyası", () => NetcadContext.CurrentProject?.FileName);
            Try("CurrentProjectName", () => NetcadApplication.CurrentProjectName);
            Try("CurrentProjectHandle", () => NetcadApplication.CurrentProjectHandle);
            return sb.ToString();
        }
    }
}
