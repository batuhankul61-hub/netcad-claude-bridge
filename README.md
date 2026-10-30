# Kul AI Asistan — NETCAD 8.5

NETCAD'in içinde, sağ kenarda "Kul AI" panelinde çalışan Türkçe yapay zekâ asistanı.
Model: Gemini (önerilen; yedek modellere otomatik geçer), Ollama (yerel, ücretsiz) ya da Claude.

## Klasörler

| Klasör | İçerik |
|---|---|
| `src\KulAI.Core` | CAD'den bağımsız çekirdek: model sağlayıcıları, ajan döngüsü, ayarlar, sohbet paneli, PDF/görüntü (Civil 3D ile ortak olacak) |
| `src\KulAI.Claude` | Claude sağlayıcısı (ayrı DLL; Anthropic SDK) |
| `src\KulAI.Netcad` | NETCAD modülü (`KulAI.NETMODUL.dll`), panel ve araçlar |
| `tests\GeometryTests` | İfraz geometrisinin birim testleri (`dotnet run`) |
| `tests\data` | Deneme verileri (CSV, Excel, PDF) |
| `scripts` | Kurulum, geliştirme döngüsü, araç şeması doğrulama, senaryo testleri |
| `mcp` | Claude Code MCP köprüsü: modülün araçlarını Claude Code'a açar (`deploy.ps1 -Mcp`, ayrıntı `mcp\README.md`) |

## Araçlar (21)

- **Çizim:** çizgi, poligon/alan, nokta (ad/kod/kot), yazı, daire, tablo; katman oluşturma, aç/kapat
- **Okuma:** çizim bilgisi, nesne arama (katman/tür/ad; alan, uzunluk, kot, köşeler), kullanıcıya seçtirme
- **Silme:** her zaman kullanıcı onayıyla
- **Dosyadan nokta:** Excel/CSV/TXT ve NETCAD .ncn (Ad Y X Z) önizleme ve aktarma (ondalık virgül, Türkçe karakter, Y/X karışıklığı uyarısı)
- **İfraz:** parseli eşit parçalara ya da verilen alanlara bölme (referans kenara dik/paralel)
- **Koordinat sistemleri:** EPSG arama (NETCAD'in EPSG veritabanı) ve dönüşüm (NETCAD projeksiyon motoru), ör. ED50/TM30 → TUREF/TM30
- **Görme:** NETCAD ekranını modele gösterme; PDF sayfalarını görüntü olarak okuma (Windows'un yerleşik PDF motoru, taranmış belgeler dahil)

Panel: Enter gönderir; dosya sürükle-bırak (resim → modele gösterilir, PDF/Excel → yolu mesaja eklenir); Ctrl+V ile panodaki resmi ekleme.
Güvenlik: N'den fazla nesneyi değiştiren işlemler ve silme onay ister; firma standartları "bunu hatırla" ile kaydedilir.

## Kurulum

[Releases](https://github.com/batuhankul61-hub/netcad-claude-bridge/releases) sayfasından zip'i indirip bir klasöre çıkartın, NETCAD'i kapatın:

- **Kul AI asistanı için:** `Kur.bat`'a çift tıklayın, NETCAD'i açıp panelin Ayarlar'ından Gemini anahtarı girin.
- **Claude Code bağlantısı için:** `Claude-Bagla.bat`'a çift tıklayın. API anahtarı gerekmez; Claude Code ve uv yeterli.

Kaldırma: `Kaldir.bat`. Ayrıntılar: `scripts/beta/KURULUM VE TEST REHBERI.txt`.

## Koordinat kuralı

NETCAD API'de x = sağa, y = yukarı. Haritacılıkta **Y = sağa (doğu), X = yukarı (kuzey)**.
Araçlar `[east, north]` = `[harita Y, harita X]` sırası kullanır.

## Geliştirme

```bash
powershell -ExecutionPolicy Bypass -File scripts\dev-cycle.ps1 -CommandFile scripts\tests\araclar.txt
```

Derler, araç şemalarını doğrular, NETCAD'i kapatıp geliştirici kurulumuyla açar, komut dosyasındaki
geliştirici komutlarını çalıştırır (`newproject`, `tool <ad> <json>`, `ask <istek>`, `provider <ad>`, `diag`, `closetest`).
Çizim yapan komutlar yalnızca kaydedilmemiş ya da adı `KulAI-Test` olan projelerde çalışır.

Senaryo testleri (gerçek modelle; NETCAD açık olmalı):

```bash
powershell -ExecutionPolicy Bypass -File scripts\eval.ps1 -Provider gemini
```

Senaryolar `scripts\tests\senaryolar.json` içinde; rapor `tests\eval-sonuclari\`.

## Bilinen durumlar

- NETCAD üst şeridine (ribbon) kendi sekmemiz eklenemiyor; panel açılışta sağ kenara yerleşir.
- `INetcadEPSGSupport.Supported_EPSG_Codes` NETCAD 8.5'te geri dönmüyor; EPSG listesi `Netcad.Epsg` veritabanından okunur.
- Ollama'daki qwen3 görüntü desteklemez; ekran/PDF okuma için Gemini ya da Claude gerekir.

## Sonra yapılacak (unutma)

- **Kul AI Bulut:** müşteri API anahtarı girmesin; lisans kodu + sunucu vekili. API anahtarı asla eklentiye gömülmez.
- İmzalı kurulum paketi (MSI), Civil 3D'nin ortak çekirdeğe taşınması.

## Katkıda bulunanlar

- **Batuhan Kul** ([@batuhankul61-hub](https://github.com/batuhankul61-hub)): proje sahibi
- **Efekan Kul** ([@Tikky1](https://github.com/Tikky1)): geliştirme desteği

## Lisans

[MIT](LICENSE). NETCAD, Netcad Yazılım A.Ş.'nin ürünüdür; bu proje NETCAD'in kendi dosyalarını içermez.
