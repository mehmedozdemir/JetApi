# JetApi

> A fast, local-first, Git-friendly desktop client for testing REST and gRPC APIs. Open source (MIT).

**JetApi**; geliştiricilerin ve test uzmanlarının REST ve gRPC API'lerini hızlı, doğru ve Git ile birlikte çalışarak test
ettiği, hesap gerektirmeyen bir masaüstü uygulamasıdır. Postman'e hafif ve şeffaf bir alternatif olarak tasarlanmıştır.

- **Hızlı ve hafif:** .NET 10 ve Avalonia ile native masaüstü uygulaması
- **Hesap yok, bulut yok, telemetri yok:** Veriler sizin diskinizde
- **Git dostu:** Her istek ayrı, okunabilir bir YAML dosyası; ekip paylaşımı Git ile
- **REST ve gRPC eşit:** `.proto` dosyalarından kod üretimi olmadan gRPC çağrısı
- **Doğru:** Gönderilen istek birebir gösterilir; DNS/bağlantı/ilk bayt/indirme süreleri ayrı ayrı ölçülür

> **Durum:** M0 tamamlandı. Çekirdek motorlar (workspace, değişkenler, REST, gRPC) hazır ve test edildi. Masaüstü arayüzü
> M1'de geliştirilecek. Ayrıntılar için [yol haritasına](docs/roadmap.md) bakın.

## Dokümantasyon

| Doküman | İçerik |
|---|---|
| [Vizyon](docs/vision.md) | Problem, ürün ilkeleri, kullanıcılar, kapsam dışı |
| [Gereksinimler](docs/requirements.md) | Önceliklendirilmiş fonksiyonel ve fonksiyonel olmayan gereksinimler |
| [Mimari](docs/architecture.md) | Projeler, bağımlılık kuralları, akışlar, test stratejisi |
| [Dosya formatı](docs/file-format.md) | Workspace'in diskteki YAML formatı |
| [Yol haritası](docs/roadmap.md) | Kilometre taşları ve sürümler |
| [ADR'ler](docs/adr/README.md) | Mimari karar kayıtları |

## Gereksinimler

- [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) (sürüm `global.json` ile sabitlenmiştir)
- Windows 10+, macOS 13+ veya Linux x64/arm64

## Hızlı başlangıç

```bash
git clone https://github.com/mehmedozdemir/JetApi.git
cd JetApi
dotnet restore JetApi.slnx
dotnet build JetApi.slnx
dotnet test --solution JetApi.slnx
dotnet run --project src/JetApi.Desktop
```

## Yapılandırma

Uygulama ortam değişkeni veya sır gerektirmez. Workspace'e özel tüm ayarlar workspace klasöründeki YAML dosyalarındadır
([dosya formatı](docs/file-format.md)). Makineye özel durum `.jetapi/` klasöründe tutulur ve Git'e eklenmemelidir.

## Proje yapısı

```
src/
  JetApi.Core/       Domain modeli ve değişken çözümleme (I/O yok)
  JetApi.Storage/    YAML workspace okuma/yazma
  JetApi.Http/       REST motoru
  JetApi.Grpc/       Proto yükleme, JSON↔protobuf codec, gRPC çağrısı
  JetApi.Desktop/    Avalonia masaüstü uygulaması
tests/               Her proje için test projesi (xUnit v3, Microsoft Testing Platform)
docs/                Vizyon, gereksinimler, mimari, ADR'ler, yol haritası
```

## Katkı

Katkı süreci, branch stratejisi ve commit formatı için [CONTRIBUTING.md](CONTRIBUTING.md) dosyasına bakın.

## Lisans

[MIT](LICENSE)
