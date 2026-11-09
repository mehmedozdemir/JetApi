# ADR-0010: Postman script'leri: yaygın test kalıpları kurallara çevrilir, gerisi dosyada saklanır

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-09
- **İlgili:** [ADR-0005](0005-assertions-before-scripting.md), [ADR-0008](0008-checks-jsonpath-and-schema.md)

## Bağlam

Postman koleksiyonlarındaki testler JavaScript'tir (`pm.test`, `pm.expect`, `pm.environment.set`). JetApi M3'te kodsuz
kontroller ve değişken çıkarma kuralları getirdi ([ADR-0005](0005-assertions-before-scripting.md)); JavaScript çalıştırma
M5'e kadar yok. M2'deki içe aktarma script'leri atıyor, yalnızca raporda yerini yazıyordu. Bu iki sorun yaratır:

- Postman'den gelen ekipler testlerini elle yeniden yazmak zorunda kalır.
- Atılan script kaybolur; M5'te script desteği geldiğinde geri getirilemez.

## Karar

- **Çeviri:** İsteklerin test script'lerindeki yaygın ifadeler kontrol ve çıkarma kurallarına çevrilir (`JetApi.Import`,
  `PostmanScripts`). Desteklenenler [import.md](../import.md) dosyasında listelenir: status, `to.be.ok`, yanıt süresi,
  header, body metni, `pm.response.json()` üzerinden JSONPath'e çevrilen değer kontrolleri (`eql`, `include`, `above`,
  `below`, `exist`, `match`, `have.property`, `be.true/false/null`, `not` biçimleri) ve `pm.environment/collectionVariables/
  globals/variables.set(...)` ile eski `tests[...]` / `postman.set...Variable` biçimleri.
- Çeviri **tanıdığı ifadeleri** işler. Bir script'te tanınmayan herhangi bir ifade kalırsa (koşul, döngü, `length`, nesne
  karşılaştırması, `console.log`…) çeviri "kısmi" sayılır.
- **Saklama:** Tamamı çevrilen test script'i saklanmaz. Kısmi çevrilen ya da hiç çevrilemeyen test script'i, pre-request
  script'leri ve klasör/koleksiyon düzeyindeki script'ler dosyada **aynen** saklanır: istek, `folder.yaml` ve
  `collection.yaml` dosyalarında yeni `scripts` bölümü (`preRequest`, `test`). JetApi bunları çalıştırmaz.
- **Görünürlük:** İçe aktarma raporu her script için ne olduğunu yazar (çevrildi / kısmen çevrildi / saklandı, çalışmıyor).
  Uygulamada isteğin *Testler* sekmesi saklanan script'i bir notla gösterir; `jetapi` CLI, koşudaki script'li istek sayısını
  uyarı olarak yazar. Böylece "her şey geçti" sonucu çalışmayan testleri gizlemez.
- Uygulama script'leri düzenlemez, ama istek kaydedilirken korur.

## Sonuçlar

- Postman'in önerdiği hazır test parçalarıyla yazılmış koleksiyonlar kod yazmadan çalışır hâle gelir.
- Hiçbir script kaybolmaz; M5'te script desteği geldiğinde saklananlar oradan çalıştırılabilir. O zaman kısmen çevrilen
  script'lerde çevrilen ifadeler hem kural hem script olarak bulunacağı için birlikte nasıl çalışacakları M5'te ele alınır.
- Dosya biçimine ekleme yapıldı (`scripts`). `schemaVersion` artmaz; eski sürümler bilinmeyen anahtar yüzünden bu dosyaları
  atlar ([file-format.md](../file-format.md#sürümleme)).
- Çeviri düzenli ifadelerle yapılır, tam bir JavaScript ayrıştırıcısı değildir. Yanlış çeviri riskine karşı kural: emin
  olunmayan her ifade çevrilmez ve script saklanır.
