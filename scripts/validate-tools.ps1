# Araçların JSON şemalarını NETCAD'i açmadan doğrular: modül DLL'ini yansıma ile yükler, her NetcadTool
# alt sınıfını oluşturup SchemaJson'u ayrıştırır. Bozuk şema varsa çıkış kodu 1 olur.
$ErrorActionPreference = "Stop"
$bin = Join-Path (Split-Path $PSScriptRoot -Parent) "src\KulAI.Netcad\bin\Release"
$tools = "C:\NetCAD853\TOOLS\UTILS\NETTOOLS"

[AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($s, $e)
    $short = (New-Object Reflection.AssemblyName $e.Name).Name
    # Kaynak (dil) derlemeleri ve PowerShell'in kendi derlemeleri aranmaz: aksi halde işleyici kendini tekrar tetikler.
    if ($short.EndsWith(".resources") -or $short.StartsWith("System.Management")) { return $null }
    $n = $short + ".dll"
    foreach ($dir in $bin, $tools) { $p = Join-Path $dir $n; if (Test-Path $p) { return [Reflection.Assembly]::LoadFrom($p) } }
    return $null
})
$json = [Reflection.Assembly]::LoadFrom((Join-Path $tools "Newtonsoft.Json.dll"))
$jobject = $json.GetType("Newtonsoft.Json.Linq.JObject")
$parse = $jobject.GetMethod("Parse", [Type[]]@([string]))
$asm = [Reflection.Assembly]::LoadFrom((Join-Path $bin "KulAI.NETMODUL.dll"))
$base = $asm.GetType("KulAI.Netcad.Tools.NetcadTool")
$flags = [Reflection.BindingFlags]"Instance,NonPublic,Public"
$bad = 0; $count = 0
foreach ($t in $asm.GetTypes() | Where-Object { $base.IsAssignableFrom($_) -and -not $_.IsAbstract }) {
    $count++
    $tool = [Activator]::CreateInstance($t)
    $schema = $t.GetProperty("SchemaJson", $flags).GetValue($tool)
    try { [void]$parse.Invoke($null, @($schema)) }
    catch { $bad++; $inner = $_.Exception; while ($inner.InnerException) { $inner = $inner.InnerException }; "GEÇERSİZ ŞEMA: $($t.Name): $($inner.Message)" }
}
"Araç şemaları: $count araç, $bad hatalı"
if ($bad -gt 0) { exit 1 }
