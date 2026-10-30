# Asistan senaryo testleri: gerçek modelle "ask" çalıştırır, sonra çizimi araçlarla kontrol eder.
# NETCAD geliştirici kurulumuyla açık olmalı (scripts\dev-cycle.ps1 -NoBuild).
# Kullanım:  powershell -ExecutionPolicy Bypass -File scripts\eval.ps1 [-Scenarios tests\senaryolar.json] [-Only ad]
# Her senaryo yeni bir KulAI-Test projesinde çalışır:
#   setup      : önce çalışacak geliştirici komutları ("tool ..." vb.)
#   ask        : asistana verilen istek (Türkçe, kullanıcı gibi)
#   transcript : konuşma dökümünde geçmesi gereken düzenli ifadeler (ör. kullanılan araç adı)
#   checks     : { cmd, expect } — cmd'nin çıktısı expect düzenli ifadesine uymalı
#   -Provider gemini|ollama|anthropic : yalnızca bu test için sağlayıcı (kullanıcı ayarı değişmez)
param([string]$Scenarios = "$PSScriptRoot\tests\senaryolar.json", [string]$Only, [string]$Provider)
$root = Split-Path $PSScriptRoot -Parent
$inbox = Join-Path $root "dev-inbox"
$utf8 = New-Object Text.UTF8Encoding $false

function Send([string]$command, [int]$timeoutSec = 60) {
    $name = "e{0:yyyyMMddHHmmssfff}" -f (Get-Date)
    [IO.File]::WriteAllText("$inbox\$name.cmd", $command, $utf8)
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while (-not (Test-Path "$inbox\$name.out") -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 400 }
    if (Test-Path "$inbox\$name.out") { return [IO.File]::ReadAllText("$inbox\$name.out", [Text.Encoding]::UTF8) }
    Remove-Item "$inbox\$name.cmd" -ErrorAction SilentlyContinue
    if ($command.StartsWith("ask ")) {
        # Asistanı durdur ve dökümü al; yoksa sonraki senaryonun projesinde çalışmaya devam eder.
        [void](Send "cancel" 20)
        $deadline = (Get-Date).AddSeconds(60)
        while (-not (Test-Path "$inbox\$name.out") -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 400 }
        if (Test-Path "$inbox\$name.out") { return "ZAMAN AŞIMI`n" + [IO.File]::ReadAllText("$inbox\$name.out", [Text.Encoding]::UTF8) }
    }
    return "ZAMAN AŞIMI"
}

if (-not (Get-Process Nc32 -ErrorAction SilentlyContinue)) { "NETCAD açık değil."; exit 1 }
$list = [IO.File]::ReadAllText((Resolve-Path $Scenarios), [Text.Encoding]::UTF8) | ConvertFrom-Json
$reportDir = Join-Path $root "tests\eval-sonuclari"
[void](New-Item -ItemType Directory -Force $reportDir)
$report = Join-Path $reportDir ("{0:yyyyMMdd-HHmmss}.txt" -f (Get-Date))
$passed = 0; $failed = 0; $lines = New-Object Collections.Generic.List[string]
if ($Provider) { Send "provider $Provider" 20 }

foreach ($s in $list) {
    if ($Only -and $s.name -notlike $Only) { continue }
    $fail = @()
    [void](Send "closetest" 20)
    [void](Send "newproject" 20)
    foreach ($c in @($s.setup)) { if ($c) { $o = Send $c; if ($o -match '^(HATA|İSTİSNA|REDDEDİLDİ)') { $fail += "kurulum: $c -> $o" } } }

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $transcript = Send ("ask " + $s.ask) 300
    $sec = [math]::Round($sw.Elapsed.TotalSeconds)
    if ($transcript.StartsWith("ZAMAN AŞIMI")) { $fail += "asistan 300 sn içinde bitmedi" }
    if ($transcript -match 'İSTİSNA:') { $fail += "asistan istisnası" }
    foreach ($re in @($s.transcript)) { if ($re -and $transcript -notmatch $re) { $fail += "dökümde yok: $re" } }
    foreach ($chk in @($s.checks)) {
        if (-not $chk) { continue }
        $o = Send $chk.cmd
        if ($o -notmatch $chk.expect) { $fail += "kontrol: $($chk.cmd)`n      beklenen: $($chk.expect)`n      çıktı: $($o.Substring(0, [Math]::Min(400, $o.Length)))" }
    }

    if ($fail.Count -eq 0) { $passed++; $head = "GEÇTİ  $($s.name) ($sec sn)" } else { $failed++; $head = "KALDI  $($s.name) ($sec sn)" }
    $head
    $fail | ForEach-Object { "    - $_" }
    $lines.Add($head); $fail | ForEach-Object { $lines.Add("    - $_") }
    $lines.Add("    --- döküm ---"); $lines.Add($transcript); $lines.Add("")
}
[void](Send "closetest" 20)
$summary = "Toplam: $($passed + $failed), geçen: $passed, kalan: $failed"
$summary; "Ayrıntılı rapor: $report"
$lines.Insert(0, $summary)
[IO.File]::WriteAllLines($report, $lines, $utf8)
if ($failed -gt 0) { exit 1 }
