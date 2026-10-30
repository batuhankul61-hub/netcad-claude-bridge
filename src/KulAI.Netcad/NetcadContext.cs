using System;
using Netcad.Interop;
using Netcad.Interop.Extensions.NcApi;
using Netcad.Interop.NcApi;

namespace KulAI.Netcad
{
    /// <summary>
    /// Açık NETCAD projesine erişim. NetcadApplication.CurrentProject yalnızca proje değişince güncellenir
    /// (açılışta açık proje için boş kalır); bu yüzden NETCAD'in COM arayüzündeki canlı değer önceliklidir.
    /// </summary>
    internal static class NetcadContext
    {
        public static INetcadProject CurrentProject =>
            NetcadApplication.CurrentCOM?.CurrentProject ?? NetcadApplication.CurrentProject;

        public static INetcad Nc =>
            CurrentProject?.NC ?? throw new InvalidOperationException("Açık bir NETCAD projesi yok.");
    }
}
