# Postman'den içe aktarma

JetApi, Postman'den dışa aktarılan koleksiyon (Collection v2.0 ve v2.1) ve environment dosyalarını içe aktarır.
Hedef kayıpsız taşımadır. Taşınamayan her şey içe aktarma raporunda listelenir; hiçbir şey sessizce atılmaz.

## Nasıl kullanılır

- **Açık bir workspace'te:** Kenar çubuğunda Koleksiyonlar başlığındaki **+** menüsünden *Postman'den içe aktar…*
  seçilir ya da komut paletinde (Ctrl+K) aynı komut çalıştırılır.
- **Karşılama ekranında:** *Postman'den taşı* önce dosyaları, sonra workspace klasörünü sorar. Boş bir klasör seçilirse
  yeni bir workspace oluşturulur.

Birden fazla dosya birlikte seçilebilir; koleksiyon ve environment dosyaları içeriklerine göre ayırt edilir. Okunamayan ya
da tanınmayan bir dosya raporda gösterilir, diğer dosyaların içe aktarılmasını durdurmaz. Aynı koleksiyon iki kez içe
aktarılırsa ikisi de kalır (`shop-api`, `shop-api-2`).

## Eşleştirme

| Postman | JetApi | Not |
|---|---|---|
| Koleksiyon adı, açıklaması | Koleksiyon | Anahtar addan üretilir, mevcutlarla çakışmaz |
| Klasör (iç içe) | Klasör | Açıklama ve auth dahil |
| İstek: metot, header'lar | İstek | Devre dışı header'lar devre dışı olarak kalır |
| URL ve `query` | URL + sorgu parametreleri | Devre dışı parametreler korunur |
| Yol değişkeni `:id` | Değeriyle değiştirilir | Değeri boşsa `{{id}}` olur, raporda uyarı |
| Body `raw` (json, xml, text) | JSON, XML, metin | Dil yoksa `Content-Type` header'ına bakılır |
| Body `urlencoded` | Form | |
| Body `formdata` | Multipart | Dosya alanının yolu raporda: bu makinede bulunmalı |
| Body `file` | Dosya | Yol raporda |
| Body `graphql` | JSON (`query`, `variables`) | Postman'in gönderdiği biçim; raporda bilgi |
| Auth: bearer, basic, apikey, noauth | Aynı türler | Belirtilmemişse üstten miras |
| Diğer auth türleri (OAuth 2.0, digest, AWS…) | Auth yok | Başka kimlik bilgisi miras alınıp yanlışlıkla gönderilmesin diye; raporda uyarı |
| Koleksiyon değişkenleri | Koleksiyon değişkenleri | Devre dışı ve `secret` türü korunur |
| `followRedirects`, `strictSSL` | İstek ayarları | |
| Environment değerleri | Environment | `enabled` ve `secret` korunur |
| Dinamik değişkenler (`$guid`, `$randomUUID`, `$timestamp`, `$isoTimestamp`, `$randomInt`) | Aynı | Desteklenmeyenler (`$randomEmail` vb.) raporda |

## Taşınmayanlar

- **Pre-request ve test script'leri.** Kodsuz doğrulamalar M3'te, JavaScript script'leri M5'te gelir. Her script'in
  bulunduğu yer raporda yazılır.
- **Kayıtlı örnek yanıtlar.** Raporda bilgi olarak gösterilir.
- **Desteklenmeyen auth türleri.** Yukarıya bakın.

Rapor, uyarıları (dikkat gerektirenler) önce, bilgi notlarını (farklı çevrilenler) sonra gösterir.
