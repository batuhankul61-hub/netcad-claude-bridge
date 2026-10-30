# PlotSheet (ölçekli PDF çıktı) denemesi — NETCAD gerektirmez.
# Deneme verisinden (olcum.ncn + bir parsel + Türkçe yazı) PDF üretir, sayfayı görüntüye çevirir.
param([string]$Out = "$PSScriptRoot\eval-sonuclari\pdf-cikti-denemesi.pdf", [string]$Scale, [string]$Paper)
$core = "$PSScriptRoot\..\src\KulAI.Core\bin\Release\KulAI.Core.dll"
$asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $core))
$sheet = New-Object KulAI.Core.Media.PlotSheet
$sheet.Title = "Deneme Paftası — Ölçüm Noktaları"
$sheet.Subtitle = "Kul AI Asistan · ğüşıöç ĞÜŞİÖÇ"

foreach ($line in [IO.File]::ReadAllLines("$PSScriptRoot\data\olcum.ncn")) {
    $c = $line -split '\s+'
    $p = New-Object KulAI.Core.Media.PlotSheet+PointShape
    $p.E = [double]::Parse($c[1], [Globalization.CultureInfo]::InvariantCulture)
    $p.N = [double]::Parse($c[2], [Globalization.CultureInfo]::InvariantCulture)
    $p.Name = $c[0]; $p.Rgb = 0x0000FF
    $sheet.Shapes.Add($p)
}
$poly = New-Object KulAI.Core.Media.PlotSheet+PolyShape
$poly.Closed = $true; $poly.Rgb = 0xC00000
foreach ($xy in @(@(452300.12, 4512800.50), @(452341.80, 4512806.10), @(452338.40, 4512851.95), @(452296.05, 4512843.20))) {
    $poly.Points.Add([ValueTuple[double, double]]::new($xy[0], $xy[1]))
}
$sheet.Shapes.Add($poly)
$t = New-Object KulAI.Core.Media.PlotSheet+TextShape
$t.E = 452310; $t.N = 4512825; $t.HeightM = 2; $t.AngleRad = 0.2; $t.Text = "101 Ada 5 Parsel — Şahin Işık"; $t.Rgb = 0
$sheet.Shapes.Add($t)

$scaleArg = if ($Scale) { [Nullable[double]][double]$Scale } else { $null }
$r = $sheet.Save($Out, $(if ($Paper) { $Paper } else { $null }), $scaleArg, $null)
"Kâğıt: $($r.Paper) $(if ($r.Landscape) { 'yatay' } else { 'dikey' }), ölçek 1/$($r.Scale), nesne: $($r.ShapeCount) → $Out"

$png = [IO.Path]::ChangeExtension($Out, ".jpg")
$res = [KulAI.Core.Media.PdfPages]::Render($Out, 1, 1, 1600)
[IO.File]::WriteAllBytes($png, [Convert]::FromBase64String($res.Images[0].Base64))
"Önizleme: $png"
