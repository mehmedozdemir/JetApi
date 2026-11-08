# JetApi — Ürün Vizyonu

## Problem

Postman, REST ve gRPC test aracı olarak fiilen standart hâline geldi. Ancak ekibimiz için şu sorunları yaratıyor:

| Sorun | Etkisi |
|---|---|
| **Hantallık** (Electron, bulut senkronizasyonu, ağır arayüz) | Yavaş açılış, yüksek RAM, büyük yanıtlarda takılma |
| **Hesap / bulut zorunluluğu** | Kurumsal veri ve sırlar üçüncü taraf sunucularda; çevrimdışı çalışma zayıf |
| **Git ile uyumsuzluk** | Koleksiyonlar tek dev JSON; diff, code review, branch çalışması zor |
| **Fiyat ve lisans** | Takım özellikleri ücretli, kurum içi kontrol yok |
| **Özellik kalabalığı** | Kullanmadığımız modüller arayüzü ve öğrenmeyi zorlaştırıyor |
| **gRPC ikinci sınıf** | Proto yönetimi ve hata mesajları zayıf |

## Vizyon

> **JetApi, geliştiricilerin ve test uzmanlarının REST ve gRPC API'lerini hızlı, doğru ve Git ile birlikte
> çalışarak test ettiği, hesap gerektirmeyen, açık kaynak bir masaüstü uygulamasıdır.**

## Ürün ilkeleri

Kararlar arasında kalındığında bu sıra ile öncelik verilir:

1. **Doğruluk.** Gönderilen istek, ekranda gösterilenle birebir aynıdır. Uygulamanın kendi eklediği başlıklar (ör. `User-Agent`, `Accept-Encoding`) "gönderilen istek" görünümünde açıkça listelenir. Zamanlama ölçümleri (DNS, bağlantı, ilk bayt, indirme) gerçek ölçümdür. Çözümlenemeyen `{{değişken}}`ler sessizce gönderilmez, uyarı verilir.
2. **Hız.** Hızlı açılış, düşük bellek, 50 MB'lık yanıtta bile donmayan arayüz. Her şey klavyeyle yapılabilir.
3. **Yerel öncelikli (local-first).** Hesap yok, sunucu yok, telemetri yok. Veriler kullanıcının diskinde durur.
4. **Git dostu.** Her istek ayrı, okunabilir bir YAML dosyasıdır. Takım paylaşımı Git ile yapılır: diff, review ve branch doğal olarak çalışır.
5. **REST ve gRPC eşit.** gRPC için `.proto` dosyalarından, kod üretimi gerekmeden çağrı yapılır.
6. **Sade.** Az ama eksiksiz özellik. Her ekran tek bir birincil eyleme odaklanır.
7. **Açık.** Açık kaynak (MIT). Dosya formatı belgelenmiştir; araç bağımlılığı (vendor lock-in) yoktur.

## Kullanıcılar

| Kişi | Hedefi | Kritik ihtiyaçları |
|---|---|---|
| **Geliştirici** | Geliştirdiği API'yi hızla denemek, hata ayıklamak | Hızlı istek, environment geçişi, cURL import/export, proto'dan gRPC çağrısı, net hata mesajları |
| **Test uzmanı** | API'yi sistematik olarak doğrulamak, regresyonu yakalamak | Kodsuz assertion'lar, koleksiyon çalıştırıcı, veri dosyasıyla (CSV) çoklu senaryo, raporlar, CI'da çalıştırma |
| **Takım lideri / sahip** | Ortak, düzenli, güvenli bir API kütüphanesi | Workspace yapısı, Git ile paylaşım, sırların ayrıştırılması (ileride) |

## Kapsam dışı (şimdilik)

Ekip ihtiyaçlarına göre bilinçli olarak dışarıda bırakılanlar. İhtiyaç doğarsa ADR ile yeniden değerlendirilir:

- WebSocket, GraphQL, SSE, mesaj kuyrukları (Kafka/RabbitMQ)
- Bulut senkronizasyonu, hesap sistemi
- Mock server, API tasarım/dokümantasyon portalı, monitörler
- Mobil ve web sürümü

## Başarı ölçütleri

- Ekip, v1.0 ile birlikte günlük REST/gRPC testlerinde Postman'i bırakır.
- Mevcut Postman koleksiyonları ve environment'ları kayıpsız import edilir.
- Test uzmanları regresyon koleksiyonlarını CI'da `jetapi` CLI ile çalıştırır.
- Performans hedefleri (bkz. [requirements.md](requirements.md#fonksiyonel-olmayan-gereksinimler)) her sürümde ölçülür.
