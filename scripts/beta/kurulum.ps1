# Kul AI Asistan (NETCAD) - kurulum / kaldırma. Kur.bat ve Kaldir.bat tarafından çağrılır.
#   -Sessiz : sonunda Enter beklemez (otomatik deneme için)
#   -NetcadKlasoru C:\NetCAD853 : yalnızca bu NETCAD'e kur (verilmezse bulunan tüm NETCAD 8.5 kurulumlarına)
#   -Claude : MCP kanalını da açar, köprüyü kurar ve Claude Code'a tanıtır (Claude-Bagla.bat)
param([switch]$Kaldir, [switch]$Sessiz, [string]$NetcadKlasoru, [switch]$Claude)
$ErrorActionPreference = "Stop"

$dataDir = Join-Path $env:APPDATA "KulAI.Netcad"
$mcpInbox = Join-Path $dataDir "mcp-inbox"
$bridgeDir = Join-Path $dataDir "mcp"

function Bekle { if (-not $Sessiz) { Write-Host ""; Read-Host " Kapatmak için Enter'a basın" | Out-Null } }

# .ps1 sarmalayıcısı değil gerçek program: PowerShell 5.1 betiklere giden "--" bağımsız değişkenini yutar.
function Find-ClaudeCli { Get-Command claude -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 }

function Find-Uv {
    $uv = Get-Command uv -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($uv) { return $uv.Source }
    # winget ile yeni kurulduysa bu pencerenin PATH'i henüz güncel değildir.
    foreach ($p in "$env:LOCALAPPDATA\Microsoft\WinGet\Links\uv.exe", "$env:USERPROFILE\.local\bin\uv.exe") {
        if (Test-Path $p) { return $p }
    }
}

function Install-Bridge {
    # PowerShell 5.1'de "Stop" iken programların stderr çıktısı (2>$null olsa bile) hataya dönüşür.
    $ErrorActionPreference = "Continue"
    Write-Host ""
    Write-Host " Claude Code bağlantısı kuruluyor..."
    New-Item -ItemType Directory -Force $mcpInbox, $bridgeDir | Out-Null
    Copy-Item (Join-Path $PSScriptRoot "mcp\*") $bridgeDir -Force
    Get-ChildItem $bridgeDir -File | Unblock-File
    $script = Join-Path $bridgeDir "netcad_mcp.py"

    $uv = Find-Uv
    if (-not $uv) {
        Write-Host " Köprü için 'uv' (Python çalıştırıcısı) gerekiyor, bu bilgisayarda yok." -ForegroundColor Yellow
        $winget = Get-Command winget -ErrorAction SilentlyContinue
        if ($winget -and -not $Sessiz -and (Read-Host " winget ile şimdi kurulsun mu? (E/H)") -match '^[eEyY]') {
            & winget install --id astral-sh.uv -e --accept-source-agreements | Out-Host
            $uv = Find-Uv
        }
        if (-not $uv) {
            Write-Host " uv'yi kurun (https://docs.astral.sh/uv/) ve Claude-Bagla.bat'ı tekrar çalıştırın." -ForegroundColor Yellow
            return $false
        }
    }

    $claude = Find-ClaudeCli
    if (-not $claude) {
        Write-Host " Claude Code komut satırı (claude) bulunamadı. Claude Code'da şu komutla ekleyin:" -ForegroundColor Yellow
        Write-Host "   claude mcp add netcad -s user -- `"$uv`" run --quiet --script `"$script`""
        return $false
    }
    # Önceki kayıt varsa yenisiyle değiştir (yol ya da uv değişmiş olabilir).
    & $claude.Source mcp remove netcad -s user 2>$null | Out-Null
    & $claude.Source mcp add netcad -s user -- $uv run --quiet --script $script | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host " Claude Code'a eklenemedi (claude mcp add hata verdi)." -ForegroundColor Red; return $false }
    Write-Host " Claude Code'a eklendi: netcad" -ForegroundColor Green
    return $true
}

function Remove-Bridge {
    $ErrorActionPreference = "Continue"
    $claude = Find-ClaudeCli
    if ($claude) { & $claude.Source mcp remove netcad -s user 2>$null | Out-Null }
    if (Test-Path $bridgeDir) { Remove-Item $bridgeDir -Recurse -Force }
}

function Find-Netcad {
    # 1) Kurulum kaydı (NETCAD'in kendi kurulum programı simge yolunu yazar)
    $keys = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
            "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
    $found = @(foreach ($k in $keys) {
        Get-ItemProperty $k -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match "netcad\s*8\.5" } |
            ForEach-Object { if ($_.DisplayIcon) { Split-Path $_.DisplayIcon.Trim('"') -Parent } elseif ($_.InstallLocation) { $_.InstallLocation } }
    })
    # 2) Bilinen klasörler (dizi olarak birleştir; tek yol bulunduğunda metin birleştirmesi olmasın)
    $found += @(Get-PSDrive -PSProvider FileSystem | ForEach-Object { Get-ChildItem "$($_.Root)NetCAD*" -Directory -ErrorAction SilentlyContinue } | ForEach-Object FullName)
    $found | Where-Object { $_ -and (Test-Path (Join-Path $_ "Nc32.exe")) -and (Test-Path (Join-Path $_ "TOOLS\UTILS\NETTOOLS\Netcad.Interop.dll")) } |
        ForEach-Object { $_.TrimEnd('\') } | Sort-Object -Unique   # Sort -Unique büyük/küçük harfe duyarsız
}

Write-Host ""
Write-Host " Kul AI Asistan - NETCAD $(if ($Kaldir) { 'Kaldırma' } else { 'Kurulum' })"
Write-Host " ================================="
Write-Host ""

if (Get-Process Nc32 -ErrorAction SilentlyContinue) {
    Write-Host " NETCAD şu an açık. Lütfen NETCAD'i kapatıp tekrar çalıştırın." -ForegroundColor Yellow
    Bekle; exit 1
}

$netcad = if ($NetcadKlasoru) { @($NetcadKlasoru.TrimEnd('\')) } else { @(Find-Netcad) }
if ($netcad.Count -eq 0) {
    Write-Host " NETCAD 8.5 bulunamadı." -ForegroundColor Yellow
    if ($Sessiz) { exit 1 }
    $netcad = @((Read-Host " NETCAD klasörünü yazın (Nc32.exe'nin olduğu klasör, ör. C:\NetCAD853)").Trim('"', ' '))
    if (-not (Test-Path (Join-Path $netcad[0] "Nc32.exe"))) { Write-Host " Bu klasörde Nc32.exe yok." -ForegroundColor Red; Bekle; exit 1 }
}

foreach ($dir in $netcad) {
    $target = Join-Path $dir "MODUL\KulAI"
    try {
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        if ($Kaldir) { Write-Host " Kaldırıldı: $target"; continue }

        $source = Join-Path $PSScriptRoot "KulAI"
        if (-not (Test-Path "$source\KulAI.NETMODUL.dll")) {
            Write-Host " HATA: KulAI klasörü bulunamadı. Zip'i önce bir klasöre çıkartın, Kur.bat'ı oradan çalıştırın." -ForegroundColor Red
            Bekle; exit 1
        }
        Copy-Item $source $target -Recurse -Force
        # İnternetten indirilen dosyalar işaretlenir; .NET işaretli DLL'leri yüklemeyebilir.
        Get-ChildItem $target -Recurse -File | Unblock-File
        # MCP kanalı işareti: modül bu dosyada yazan klasörü izler (pakette yoktur, yalnızca -Claude ile yazılır).
        if ($Claude) { [IO.File]::WriteAllText((Join-Path $target "mcp-inbox.txt"), $mcpInbox) }
        Write-Host " Kuruldu: $target" -ForegroundColor Green
    }
    catch [UnauthorizedAccessException] {
        Write-Host " Yazma izni yok: $target" -ForegroundColor Red
        Write-Host " Kur.bat'a sağ tıklayıp 'Yönetici olarak çalıştır' deyin." -ForegroundColor Yellow
        Bekle; exit 1
    }
}

$bridgeOk = $false
if ($Kaldir) { Remove-Bridge } elseif ($Claude) { $bridgeOk = Install-Bridge }

Write-Host ""
if ($Kaldir) {
    Write-Host " Ayarlarınız ve konuşma kayıtlarınız şu klasörde duruyor: $env:APPDATA\KulAI.Netcad"
    Write-Host " Onları da silmek isterseniz bu klasörü elle silebilirsiniz."
} elseif ($Claude) {
    Write-Host " Şimdi NETCAD'i açıp bir proje açın, ardından Claude Code'u yeniden başlatın."
    if ($bridgeOk) { Write-Host " Claude'a örneğin 'NETCAD'de hangi katmanlar var?' diye sorun." }
    Write-Host " (Kul AI paneli de görünür; Claude için kullanmanız ya da API anahtarı girmeniz gerekmez.)"
} else {
    Write-Host " Şimdi NETCAD'i açın. Sağ kenarda 'Kul AI' paneli belirecek."
    Write-Host " Ayrıntılar: 'KURULUM VE TEST REHBERI.txt'"
}
Bekle
