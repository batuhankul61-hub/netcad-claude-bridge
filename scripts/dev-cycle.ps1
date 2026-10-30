# Geliştirme döngüsü: derle → NETCAD'i kapat → geliştirici kurulumu → NETCAD'i aç → komutları çalıştır.
# Kullanım:  powershell -ExecutionPolicy Bypass -File scripts\dev-cycle.ps1 [-NoBuild] [-CommandFile komutlar.txt]
#   Komut dosyasında her satır bir komuttur (JSON tırnakları komut satırında kaybolmasın diye dosyadan okunur).
#   Komut verilmezse yalnızca derleyip yeniden başlatır.
param([switch]$NoBuild, [string]$CommandFile)
$Commands = if ($CommandFile) { Get-Content $CommandFile -Encoding UTF8 | Where-Object { $_.Trim() -and -not $_.StartsWith("#") } } else { @() }
$root = Split-Path $PSScriptRoot -Parent
$log = Join-Path $env:APPDATA "KulAI.Netcad\logs\modul.log"
$inbox = Join-Path $root "dev-inbox"

if (-not $NoBuild) {
    $b = dotnet build "$root\src\KulAI.Netcad" -c Release 2>&1 | Select-String -Pattern "error|warning CS" | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique
    if ($b) { $b; "DERLEME BAŞARISIZ"; exit 1 }
    "Derleme tamam"
    # Araç tanımlarını NETCAD'i açmadan doğrula (bozuk JSON şema paneli açılmaz hale getirir).
    # Ayrı süreçte: modül DLL'ini yükler, bu süreci kirletmesin.
    $v = powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\validate-tools.ps1"
    if ($LASTEXITCODE -ne 0) { $v; "ARAÇ TANIMLARI GEÇERSİZ"; exit 1 }
    $v
}

$p = Get-Process Nc32 -ErrorAction SilentlyContinue
if ($p) {
    # Önce açık deneme projelerini (KulAI-Test) kaydetmeden kapat; yoksa NETCAD kapanırken kaydetmeyi sorar.
    $name = "close_{0:yyyyMMddHHmmss}" -f (Get-Date)
    [IO.File]::WriteAllText("$inbox\$name.cmd", "closetest", (New-Object Text.UTF8Encoding $false))
    $deadline = (Get-Date).AddSeconds(10)
    while (-not (Test-Path "$inbox\$name.out") -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (Test-Path "$inbox\$name.out") { Get-Content "$inbox\$name.out" -Encoding UTF8 } else { Remove-Item "$inbox\$name.cmd" -ErrorAction SilentlyContinue }
    [void]$p.CloseMainWindow()
}
$deadline = (Get-Date).AddSeconds(25)
while ((Get-Process Nc32 -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep 2 }
if (Get-Process Nc32 -ErrorAction SilentlyContinue) { "NETCAD kapanmadı (kaydetme sorusu olabilir)"; exit 1 }

powershell -ExecutionPolicy Bypass -File "$PSScriptRoot\deploy.ps1" -Start -Dev | ForEach-Object { "$_" }

$start = Get-Date; $deadline = $start.AddSeconds(150); $ready = $false
while ((Get-Date) -lt $deadline -and -not $ready) {
    if ((Test-Path $log) -and (Get-Item $log).LastWriteTime -gt $start) {
        # Yalnızca bu başlatmadan sonra yazılan satırlara bak; eski satırlar yanıltmasın.
        $stamp = $start.ToString('yyyy-MM-dd HH:mm:ss')
        $fresh = Get-Content $log -Encoding UTF8 -Tail 30 | Where-Object { $_ -match '^\[(.{19})\]' -and $Matches[1] -ge $stamp }
        if (($fresh -join "`n") -match 'Geliştirici kanalı açık|Panel açılamadı') { $ready = $true }
    }
    if (-not $ready) { Start-Sleep 3 }
}
"NETCAD hazır: $ready"
# Modül yüklendiğinde NETCAD hâlâ açılış ekranındadır; ana form görünene kadar bekle
# (açılış sırasında gönderilen komutlar kaybolabiliyor, ekran yakalama da açılış ekranına denk geliyor).
Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class NcWindows {
  delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EP f, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  public static bool Visible(uint pid, string cls) { bool found = false;
    EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h,out p); if (p==pid && IsWindowVisible(h)) { var c=new StringBuilder(256); GetClassName(h,c,256); if (c.ToString()==cls) found=true; } return true; }, IntPtr.Zero);
    return found; }
}
'@
if ($ready) {
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        $nc = Get-Process Nc32 -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($nc -and [NcWindows]::Visible([uint32]$nc.Id, "TNetcadForWindowsMainForm") -and -not [NcWindows]::Visible([uint32]$nc.Id, "TSplashForm60")) { break }
        Start-Sleep 2
    }
    Start-Sleep 3
    if ($nc -and [NcWindows]::Visible([uint32]$nc.Id, "#32770")) { "UYARI: NETCAD bir iletişim kutusu gösteriyor." }
}
Get-Content $log -Encoding UTF8 -Tail 3

$i = 0
foreach ($c in $Commands) {
    $i++; $name = "c{0:yyyyMMddHHmmss}_$i" -f (Get-Date)
    [IO.File]::WriteAllText("$inbox\$name.cmd", $c, (New-Object Text.UTF8Encoding $false))
    $deadline = (Get-Date).AddSeconds(120)
    while (-not (Test-Path "$inbox\$name.out") -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    "===== $c"
    if (Test-Path "$inbox\$name.out") { Get-Content "$inbox\$name.out" -Encoding UTF8 } else { "ZAMAN AŞIMI" }
}
