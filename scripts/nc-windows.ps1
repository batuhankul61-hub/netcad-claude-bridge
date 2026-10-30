# NETCAD'in görünür pencerelerini ve iletişim kutusu metinlerini listeler (ekran görüntüsü almadan).
# Kullanım:  powershell -ExecutionPolicy Bypass -File scripts\nc-windows.ps1
Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class NcWin {
  delegate bool EP(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] struct R { public int L, T, Ri, B; }
  [DllImport("user32.dll")] static extern bool EnumWindows(EP f, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EP f, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
  static string Txt(IntPtr h) { var s = new StringBuilder(2048); GetWindowText(h, s, 2048); return s.ToString(); }
  public static List<string> List(uint pid) {
    var r = new List<string>();
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) {
        R rc; GetWindowRect(h, out rc);
        r.Add(string.Format("{0} | {1} | {2}x{3}{4}", Cls(h), Txt(h), rc.Ri - rc.L, rc.B - rc.T, IsIconic(h) ? " (simge)" : ""));
        if (Cls(h) == "#32770")
          EnumChildWindows(h, (c, l2) => { var t = Txt(c); if (t.Length > 0) r.Add("    " + Cls(c) + ": " + t); return true; }, IntPtr.Zero);
      }
      return true;
    }, IntPtr.Zero);
    return r;
  }
}
'@
foreach ($p in Get-Process Nc32 -ErrorAction SilentlyContinue) { "Nc32 pid=$($p.Id) yanıt veriyor=$($p.Responding)"; [NcWin]::List([uint32]$p.Id) }
