# Changelog

Bu projedeki tüm önemli değişiklikler bu dosyada belgelenir.
Biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) standardını, sürümleme [Semantic Versioning](https://semver.org/lang/tr/) kurallarını izler.

## [Unreleased]

### Added

- Ürün vizyonu, gereksinimler, mimari, dosya formatı, yol haritası ve ADR'ler.
- `JetApi.Core`: workspace/koleksiyon/klasör/istek/environment modeli, auth mirası, katmanlı `{{değişken}}` çözümleme (iç içe değişkenler, döngü tespiti, dinamik değişkenler).
- `JetApi.Storage`: Git dostu YAML workspace formatı (schemaVersion 1); atomik yazma, değişmeyen dosyaya dokunmama, hatalı dosyalar için tanılama.
- `JetApi.Http`: REST isteği hazırlama ve gönderme; tüm body türleri, auth, yönlendirme/TLS/zaman aşımı ayarları, DNS/bağlantı/TTFB/indirme ölçümü, gönderilen isteğin birebir raporu.
- `JetApi.Grpc`: `.proto` dosyalarını çalışma anında yükleme, proto3 JSON eşlemesine uygun JSON↔protobuf codec (well-known type'lar dahil), örnek mesaj şablonu ve unary çağrı.
- Masaüstü uygulaması (onaylı tasarım): karşılama ekranı, koleksiyon ağacı (sağ tık menüsü, sürükle-bırak, filtre), sekmeler, REST ve gRPC editörleri, sözdizimi vurgulama ve arama, yanıt görüntüleyici (pretty/raw/hex, header'lar, zamanlama, gönderilen istek), environment editörü, koleksiyon/klasör ayarları, komut paleti (Ctrl+K), klavye kısayolları, açık/koyu tema, Türkçe/İngilizce arayüz.
- Workspace oturumu: her değişiklik anında diske yazılır; aktif environment makineye özel saklanır; diskteki değişiklikler (`git pull`) otomatik yüklenir.
- İstek geçmişi (`.jetapi/history.jsonl`, Git dışı).
- Gönderimden önce tanımsız değişken uyarısı.
- Editörlerde değişken vurgulama (tanımlı/tanımsız ayrı renk) ve üzerine gelince çözümlenmiş değer ile kaynağı gösterme.
- HTTP bağlantı hataları için hata türü (`HttpErrorKind`); arayüz bunları seçili dilde açıklar.
- Windows, macOS ve Linux üzerinde CI.
