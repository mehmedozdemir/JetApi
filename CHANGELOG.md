# Changelog

Bu projedeki tüm önemli değişiklikler bu dosyada belgelenir.
Biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) standardını, sürümleme [Semantic Versioning](https://semver.org/lang/tr/) kurallarını izler.

## [Unreleased]

### Added

- Ayarlar penceresi (Ctrl+,): tema, arayüz yazı tipi ve boyutu, editör yazı tipi ve puntosu, yanıtlarda satır kaydırma, dil, varsayılan zaman aşımı, tanımsız değişken uyarısı, geçmiş kaydı ve çizim modu. Değişiklikler anında uygulanır.
- Postman’den içe aktarma: Collection v2.0/v2.1 ve environment dosyaları (birden fazla dosya birlikte). Kenar çubuğundaki + menüsünden, komut paletinden veya karşılama ekranından. İçe aktarma raporu, neyin farklı çevrildiğini ve neyin aktarılamadığını (script’ler, OAuth 2.0, örnek yanıtlar, kontrol edilecek dosya yolları) gösterir.
- Ürün vizyonu, gereksinimler, mimari, dosya formatı, yol haritası ve ADR'ler.
- `JetApi.Core`: workspace/koleksiyon/klasör/istek/environment modeli, auth mirası, katmanlı `{{değişken}}` çözümleme (iç içe değişkenler, döngü tespiti, dinamik değişkenler).
- `JetApi.Storage`: Git dostu YAML workspace formatı (schemaVersion 1); atomik yazma, değişmeyen dosyaya dokunmama, hatalı dosyalar için tanılama.
- `JetApi.Http`: REST isteği hazırlama ve gönderme; tüm body türleri, auth, yönlendirme/TLS/zaman aşımı ayarları, DNS/bağlantı/TTFB/indirme ölçümü, gönderilen isteğin birebir raporu.
- `JetApi.Grpc`: `.proto` dosyalarını çalışma anında yükleme, proto3 JSON eşlemesine uygun JSON↔protobuf codec (well-known type'lar dahil), örnek mesaj şablonu ve unary çağrı.
- Masaüstü uygulaması (onaylı tasarım): karşılama ekranı, koleksiyon ağacı (sağ tık menüsü, sürükle-bırak, filtre), sekmeler, REST ve gRPC editörleri, sözdizimi vurgulama ve arama, yanıt görüntüleyici (pretty/raw/hex, header'lar, zamanlama, gönderilen istek), environment editörü, koleksiyon/klasör ayarları, komut paleti (Ctrl+K), klavye kısayolları, açık/koyu tema, Türkçe/İngilizce arayüz.
- Workspace oturumu: her değişiklik anında diske yazılır; aktif environment makineye özel saklanır; diskteki değişiklikler (`git pull`) otomatik yüklenir.
- İstek geçmişi (`.jetapi/history.jsonl`, Git dışı).
- Gönderimden önce tanımsız değişken uyarısı.
- Motor mesajlarının Türkçe/İngilizce sunulması: istek hazırlama, bağlantı, gRPC, proto/JSON ve workspace dosyası mesajları arayüz dilini izler.
- Paketleme: Windows zip, macOS dmg, Linux AppImage ve tar.gz (`build/package.sh`); etiketle taslak GitHub Release üreten iş akışı; uygulama ikonu.
- Performans ölçümü: `JetApi --measure` modu ve `build/measure.sh` ile açılış süresi ve boşta bellek raporu.
- Editörlerde değişken vurgulama (tanımlı/tanımsız ayrı renk) ve üzerine gelince çözümlenmiş değer ile kaynağı gösterme.
- HTTP bağlantı hataları için hata türü (`HttpErrorKind`); arayüz bunları seçili dilde açıklar.
- Windows, macOS ve Linux üzerinde CI.

### Changed

- Arayüz makyajı: daha az çerçeve, vurgu rengi yalnızca ana eylemde; body türü ve görünüm seçimi segmented control; sekme rozetleri; yanıt süresi ve boyutu chip olarak; istek adı başlık olarak; kopyala ve kaydet ikon butonlar; sade karşılama ekranı; durum çubuğunda kısa yol ve Git dalı. Soluk metnin kontrastı AA seviyesine çıkarıldı. Yanıttaki IPv4 adresleri `[::ffff:…]` biçimi yerine sade gösterilir.
- Kenar çubuğu ikon şeridiyle Koleksiyonlar, Environments ve Geçmiş arasında geçer; environment listesi kenar çubuğunda, detayı ana alanda açılır. Environment'larda sağ tık menüsü (aktif yap, yeniden adlandır, çoğalt, sil) ve F2 / Ctrl+D / Delete kısayolları; isteklerin menüsüne "Aç" ve Ctrl+D eklendi.
- Koleksiyon ekranı: koleksiyona tıklayınca açılır; koleksiyon değişkenleri, auth, proto dosyaları ve ad sekmelerde düzenlenir. Koleksiyon değişkenleri environment listesinden buraya taşındı. Ekrandan çıkarken değişiklikler kaydedilir.
- URL ve gRPC sunucu alanında değişkenler alanın içinde vurgulanır; URL alanında Enter isteği gönderir.
- Windows'ta arayüz varsayılan olarak yazılımla çizilir; boşta bellek 263 MB'tan 145 MB'a iner. `JETAPI_RENDERING=gpu` ile GPU kullanılır.

### Fixed

- Dar pencerede veya büyük arayüz boyutunda istek ve yanıt sekmeleri yandaki panele taşıyordu; artık yatayda kaydırılır.
- Dil değiştirmek environment'ı "kaydedilmemiş" gösteriyordu.
- Environment veya koleksiyon değişkenine ad yazılınca satırın checkbox, gizli ve sil kontrolleri ancak ekran yenilenince çıkıyordu.
- Klavyeyle gezerken görünmeyen panel ayırıcısı odak alıyordu; ağaç öğeleri ve sekmeler ekran okuyucuya adsız sunuluyordu.
- Ağ ve TLS hatalarında mesajın iki kez başlıklanması ("Network error: Network error: ...").
- Makinede birden fazla NuGet kaynağı tanımlıyken derlemenin NU1507 ile kırılması: repodaki `nuget.config` paketleri yalnızca nuget.org'dan alır.
