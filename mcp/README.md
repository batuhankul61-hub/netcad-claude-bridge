# NETCAD MCP köprüsü

Claude Code'un (ya da başka bir MCP istemcisinin) NETCAD'i doğrudan kullanmasını sağlar. AutoCAD tarafındaki
`autocad-mcp` kurulumunun NETCAD karşılığıdır. GitHub'da NETCAD için hazır bir MCP sunucusu olmadığından, Kul AI
modülünün kendi araçları MCP'ye açılır: çizgi, poligon, nokta, yazı, katman, nesne arama, ifraz, koordinat dönüşümü,
dosyadan nokta aktarma, ekran görüntüsü, PDF okuma/çıktı, NETCAD komutu bulma/çalıştırma.

```
Claude Code ──stdio──► mcp\netcad_mcp.py ──<ad>.cmd/.out──► %APPDATA%\KulAI.Netcad\mcp-inbox ◄── Kul AI modülü (NETCAD içinde)
```

## Kurulum

1. NETCAD'i kapatın, modülü MCP kanalıyla kurun:

   ```powershell
   dotnet build src\KulAI.Netcad -c Release
   powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1 -Mcp -Start
   ```

2. Bu klasörde Claude Code'da yeni bir oturum açın. Kök dizindeki `.mcp.json` "netcad" sunucusunu tanıtır; Claude Code
   onaylamanızı ister. Köprü `uv run --script` ile çalışır, bağımlılığını (`mcp`) kendisi kurar.

## Notlar

- Araçlar **açık projede** çalışır (geliştirici kanalındaki "yalnızca KulAI-Test projesi" sınırı bu kipte yoktur).
  Her araç çağrısını Claude Code onaylatır; çizimi değiştiren her araç NETCAD'de tek Ctrl+Z ile geri alınır.
- `-Dev` ile `-Mcp` birlikte verilirse geliştirici kanalı kazanır. İkisi de verilmezse kanal kapanır (müşteri kurulumu).
  `package.ps1` iki işaret dosyasını da pakete koymaz.
- NETCAD kapalıyken araç listesi `mcp\schemas.json` önbelleğinden gelir; çağrılar "NETCAD cevap vermiyor" der.
- NETCAD'de modal bir pencere açıksa komutlar o kapanana kadar bekler (zaman aşımı `NETCAD_MCP_TIMEOUT`, saniye).
