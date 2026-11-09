# ADR-0008: Testler — dosyadaki biçim, kendi JSONPath'imiz, JSON Schema için LateApexEarlySpeed

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-09
- **İlgili:** [ADR-0005](0005-assertions-before-scripting.md) (önce kodsuz assertion'lar)

## Bağlam

ADR-0005, yanıt kontrollerinin ve değişken çıkarmanın istek YAML'ında bildirimsel olarak tutulmasına karar verdi. M3'te
bunun biçimi, JSONPath ve JSON Schema desteğinin nereden geleceği belirlenmeliydi.

.NET'te en kapsamlı seçenek olan `JsonPath.Net` ve `JsonSchema.Net` (json-everything) güncel sürümlerinde MIT değildir:
paketler "Open Source Maintenance Fee" sözleşmesiyle (`OSMFEULA`) dağıtılır ve gelir elde eden kullanıcılardan bakım ücreti
bekler. JetApi MIT lisanslıdır ve bir şirkette kullanılır; bu koşulu bağımlılık olarak almak istemiyoruz. Diğer adaylar:
`JsonCons.JsonPath` (Apache-2.0, son sürüm 2021), `Hyperbee.Json` (Roslyn betik motorunu getirir), `NJsonSchema` (MIT,
Newtonsoft.Json ve Namotion.Reflection getirir), `LateApexEarlySpeed.Json.Schema` (BSD-3-Clause, System.Text.Json tabanlı,
JSON Schema resmî test setini geçer, aktif).

## Karar

1. **Biçim:** İstek dosyalarına iki isteğe bağlı bölüm eklenir: `tests` (kontroller) ve `extract` (değişken çıkarma).
   Operatörler kelimeyle yazılır (`equals`, `lessThan`…); `==`, `>` gibi semboller YAML'da tırnak gerektirip okunaklılığı
   bozar. Ayrıntı: [dosya formatı](../file-format.md#tests-ve-extract--yanıt-kontrolleri-ve-değişken-çıkarma). Bu bir
   eklemedir, `schemaVersion` 1 kalır: eski sürümler bu bölümleri içeren dosyayı tanılama mesajıyla atlar, üzerine yazmaz.
2. **JSONPath:** RFC 9535'i kendimiz uygularız (`JetApi.Testing`): ad, joker, indeks, dilim, birleşim, alt düğümler ve
   filtreler (karşılaştırma, varlık, `!`, `&&`, `||`, `length`, `count`, `match`, `search`, `value`). RFC'deki örneklerle
   test edilir. Bağımlılık yoktur, davranış elimizdedir.
3. **JSON Schema:** `LateApexEarlySpeed.Json.Schema` (BSD-3-Clause) kullanılır.
4. Değerlendirme ve çıkarma `JetApi.Testing` adlı yeni bir motor projesindedir; yalnızca `Core`'a bağımlıdır. Çıkarılan
   değerin nereye yazılacağı (çalışma anı değişkeni varsayılan; environment, koleksiyon, global) çağıran tarafındır.

## Sonuçlar

- Lisans temizdir: yeni bağımlılık BSD-3-Clause; JSONPath için dış bağımlılık yoktur.
- JSONPath'in doğruluğu bizim sorumluluğumuzdadır. RFC örnekleri test edilir; bir hata bulunursa test eklenerek düzeltilir.
- `LateApexEarlySpeed.Json.Schema`, uzak şemaları çözebilmek için `Microsoft.Extensions.Http` 3.1 ve ilgili eski paketleri
  getirir. Bilinen güvenlik açığı yoktur (CI açık paketleri denetler); gerekirse merkezi geçişli sürüm sabitlemeyle
  yükseltilir. Uzak `$ref` çözümü kullanılmaz.
- JSON Schema kütüphanesi değişirse yalnızca `ResponseChecker.EvaluateSchema` etkilenir.
