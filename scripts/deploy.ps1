# Derlenmiş modülü NETCAD'in MODUL\KulAI klasörüne kurar ve (istenirse) NETCAD'i açar.
# Kullanım:  powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1 [-Start]
#  -Dev : geliştirici kanalını açar (modülün yanına dev-inbox.txt koyar; müşteri kurulumunda KULLANILMAZ)
#  -Mcp : Claude Code MCP köprüsünün kanalını açar (mcp-inbox.txt; araçlar açık projede çalışır, bkz. mcp\README.md)
param([switch]$Start, [switch]$Dev, [switch]$Mcp, [string]$NetcadPath = "C:\NetCAD853")
$ErrorActionPreference = "Stop"

if (Get-Process Nc32 -ErrorAction SilentlyContinue) { throw "NETCAD açık. Önce NETCAD'i kapatın (modül DLL'i kilitli)." }

$src = Join-Path (Split-Path $PSScriptRoot -Parent) "src\KulAI.Netcad\bin\Release"
$dst = Join-Path $NetcadPath "MODUL\KulAI"
if (-not (Test-Path "$src\KulAI.NETMODUL.dll")) { throw "Önce derleyin: dotnet build src\KulAI.Netcad -c Release" }

$devFlag = Join-Path $src "dev-inbox.txt"
if ($Dev) {
    $inbox = Join-Path (Split-Path $PSScriptRoot -Parent) "dev-inbox"
    New-Item -ItemType Directory -Force $inbox | Out-Null
    [IO.File]::WriteAllText($devFlag, $inbox)
} elseif (Test-Path $devFlag) {
    Remove-Item $devFlag
}

$mcpFlag = Join-Path $src "mcp-inbox.txt"
if ($Mcp -and -not $Dev) {
    $mcpInbox = Join-Path $env:APPDATA "KulAI.Netcad\mcp-inbox"
    New-Item -ItemType Directory -Force $mcpInbox | Out-Null
    [IO.File]::WriteAllText($mcpFlag, $mcpInbox)
} elseif (Test-Path $mcpFlag) {
    Remove-Item $mcpFlag
}

# xcopy bağımsız bir süreçte çalışır: geliştirme ortamındaki dosya sanallaştırmasından etkilenmez.
# xcopy yalnızca ekler; önceki kurulumdan kalan kanal işaretleri hedeften önce silinir.
$log = Join-Path $env:TEMP "kulai-netcad-deploy.txt"
$cmd = "cmd /c del /q `"$dst\dev-inbox.txt`" `"$dst\mcp-inbox.txt`" 2>nul & xcopy `"$src`" `"$dst\`" /E /I /Y /EXCLUDE:$PSScriptRoot\deploy-exclude.txt > `"$log`" 2>&1"
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd } | Out-Null
Start-Sleep -Seconds 3
Write-Host "Kuruldu: $dst"

if ($Start) {
    Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = "`"$NetcadPath\Nc32.exe`""; CurrentDirectory = $NetcadPath } | Out-Null
    Write-Host "NETCAD başlatıldı."
}
