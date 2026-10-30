# Kul AI Asistan (NETCAD) - kurulum / kaldırma. Kur.bat ve Kaldir.bat tarafından çağrılır.
#   -Sessiz : sonunda Enter beklemez (otomatik deneme için)
#   -NetcadKlasoru C:\NetCAD853 : yalnızca bu NETCAD'e kur (verilmezse bulunan tüm NETCAD 8.5 kurulumlarına)
param([switch]$Kaldir, [switch]$Sessiz, [string]$NetcadKlasoru)
$ErrorActionPreference = "Stop"

function Bekle { if (-not $Sessiz) { Write-Host ""; Read-Host " Kapatmak için Enter'a basın" | Out-Null } }

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
        Write-Host " Kuruldu: $target" -ForegroundColor Green
    }
    catch [UnauthorizedAccessException] {
        Write-Host " Yazma izni yok: $target" -ForegroundColor Red
        Write-Host " Kur.bat'a sağ tıklayıp 'Yönetici olarak çalıştır' deyin." -ForegroundColor Yellow
        Bekle; exit 1
    }
}

Write-Host ""
if ($Kaldir) {
    Write-Host " Ayarlarınız ve konuşma kayıtlarınız şu klasörde duruyor: $env:APPDATA\KulAI.Netcad"
    Write-Host " Onları da silmek isterseniz bu klasörü elle silebilirsiniz."
} else {
    Write-Host " Şimdi NETCAD'i açın. Sağ kenarda 'Kul AI' paneli belirecek."
    Write-Host " Ayrıntılar: 'KURULUM VE TEST REHBERI.txt'"
}
Bekle
