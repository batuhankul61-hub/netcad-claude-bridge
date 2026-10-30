# Test (beta) paketi üretir: dist\KulAI-Netcad-Beta-<sürüm>.zip
#   KulAI\                             NETCAD modülü (NETCAD\MODUL\KulAI altına kurulur)
#   Kur.bat / Kaldir.bat / kurulum.ps1 çift tıklamayla kurulum/kaldırma
#   KURULUM VE TEST REHBERI.txt        test edenler için Türkçe rehber
# Kullanım:  powershell -ExecutionPolicy Bypass -File scripts\package.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

[xml]$props = Get-Content "$root\src\Directory.Build.props"
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$name = "KulAI-Netcad-Beta-$version"

Write-Host "Derleniyor ($version)..."
dotnet build "$root\src\KulAI.Netcad" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız." }
$v = powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\validate-tools.ps1"
if ($LASTEXITCODE -ne 0) { $v; throw "Araç tanımları geçersiz." }

$dist = Join-Path $root "dist"
$stage = Join-Path $dist $name
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\KulAI" | Out-Null

# Modül dosyaları: hata ayıklama dosyaları ve geliştirici ve MCP kanalı işaretleri (dev-inbox.txt, mcp-inbox.txt) pakete girmez.
$bin = Join-Path $root "src\KulAI.Netcad\bin\Release"
Get-ChildItem $bin -Recurse -File |
    Where-Object { $_.Extension -ne ".pdb" -and $_.Name -ne "dev-inbox.txt" -and $_.Name -ne "mcp-inbox.txt" } |
    ForEach-Object {
        $rel = $_.FullName.Substring($bin.Length + 1)
        $to = Join-Path "$stage\KulAI" $rel
        New-Item -ItemType Directory -Force (Split-Path $to -Parent) | Out-Null
        Copy-Item $_.FullName $to
    }
if (Test-Path "$stage\KulAI\dev-inbox.txt") { throw "Geliştirici kanalı pakete girmemeli." }

Copy-Item "$PSScriptRoot\beta\Kur.bat", "$PSScriptRoot\beta\Kaldir.bat" $stage
# PowerShell 5.1 ve Not Defteri Türkçe karakterleri doğru okusun diye BOM'lu UTF-8
$bom = New-Object Text.UTF8Encoding $true
foreach ($f in "kurulum.ps1", "KURULUM VE TEST REHBERI.txt") {
    [IO.File]::WriteAllText("$stage\$f", [IO.File]::ReadAllText("$PSScriptRoot\beta\$f", [Text.Encoding]::UTF8), $bom)
}

$zip = Join-Path $dist "$name.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Write-Host "Hazır: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
