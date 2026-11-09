# ADR-0009: Koleksiyon çalıştırıcı ayrı bir projede, gönderim bir arayüzün arkasında

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-09
- **İlgili:** [ADR-0005](0005-assertions-before-scripting.md), [ADR-0008](0008-checks-jsonpath-and-schema.md)

## Bağlam

M3'te koleksiyon ve klasörler hem uygulamada (çalıştırıcı ekranı) hem CI'da (`jetapi` komut satırı aracı) çalıştırılacak.
Sıra, tekrar, veri dosyası satırları, istekler arası bekleme, ilk hatada durma, koşu boyunca geçerli değişkenler, kontroller,
değişken çıkarma ve sonuç modeli iki yerde de aynı olmalı. İsteği hazırlayıp göndermek (değişkenler, auth, OAuth token'ları,
cookie'ler, proto önbelleği) ise uygulamada `Desktop/Services/RequestRunner` içindedir.

## Karar

- Çalıştırma mantığı `JetApi.Runner` adlı yeni bir projededir: `CollectionRunner`, `RunOptions`, `RunSummary`,
  `RequestRunResult` ve veri dosyası okuyucu `DataFile` (CSV: RFC 4180, virgül ya da noktalı virgül; JSON: nesne dizisi).
  Yalnızca `Core` ve `Testing`'e bağımlıdır.
- İsteğin nasıl gönderileceği `IRequestSender` arayüzünün arkasındadır. Uygulama bunu `RunSender` ile (sekmelerle aynı
  hazırlık, auth, OAuth ve cookie'ler), CLI kendi göndericisiyle sağlar.
- Koşu değişkenleri: her tekrar veri satırıyla başlar; koşuda çıkarılan değerler sonraki isteklere ve tekrarlara geçer ve
  satırı ezer. Kurallardaki environment/koleksiyon/global hedefleri yalnızca istenirse (`PersistExtractions`) diske yazılır;
  aksi hâlde değerler koşuyla biter. Bir istek, yanıt alınamazsa ya da bir kontrolü kalırsa başarısız sayılır.
- Çalıştırıcı çağıranın bağlamında devam eder (`ConfigureAwait(true)`): uygulamanın geri bildirimleri arayüz iş parçacığında
  gelir; CLI'da bağlam yoktur.

## Sonuçlar

- Uygulama ve CLI aynı kuralla çalışır; sonuç modeli ve raporlar (JUnit, HTML) tek yerde yazılır.
- `IRequestSender` uygulamanın gönderim altyapısını (OAuth, cookie, proto önbelleği) çalıştırıcıya taşımadan kullanmayı sağlar.
  CLI için bu altyapının bir kısmı paylaşılan bir yere taşınmalıdır (D aşaması).
- Mimari diyagramda motorlarla arayüzler arasında yeni bir katman vardır: `Runner`, motorları değil `Core` ve `Testing`'i kullanır.
