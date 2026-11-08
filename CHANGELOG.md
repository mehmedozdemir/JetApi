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
- Avalonia masaüstü uygulama kabuğu.
- Windows, macOS ve Linux üzerinde CI.
