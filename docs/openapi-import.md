# OpenAPI ve Swagger'dan içe aktarma

JetApi, OpenAPI 3.0, OpenAPI 3.1 ve Swagger 2.0 belgelerini (JSON ya da YAML) bir koleksiyon olarak içe aktarır (IMP-3).
Her işlem (path × metot) bir istek olur; gövdeler şemalardan örnek üretilerek doldurulur. Postman içe aktarmasında olduğu
gibi taşınamayan her şey içe aktarma raporunda listelenir; hiçbir şey sessizce atılmaz.

Dönüştürücü `src/JetApi.Import/OpenApiImporter.cs` içindedir ve saftır: dosya ya da ağ erişimi yapmaz.

## Belge okuma

- Dosya `{` ya da `[` ile başlıyorsa JSON, değilse YAML olarak okunur. İkisi de aynı ağaca çevrilir.
- YAML'da tırnaksız değerler YAML 1.2 kurallarıyla yorumlanır (`true`, `null`, sayılar); sayılar yazıldığı gibi kalır
  (`version: 1.0` → `1.0`). `<<` birleştirme anahtarları desteklenir.
- Çok derin ya da alias'larla aşırı büyüyen belgeler reddedilir (hata mesajıyla, istisna fırlatılmadan).
- `$ref`: Belge içi referanslar (`#/components/...`, `#/definitions/...`, JSON Pointer kaçışları `~0`, `~1` ve `%xx`
  dahil) çözülür. Başka dosyaya işaret eden, bulunamayan ya da döngüye giren referanslar raporda uyarı olur ve o kısım
  alınmaz.

## Eşleştirme

| OpenAPI / Swagger | JetApi | Not |
|---|---|---|
| `info.title` | Koleksiyon adı | Anahtar addan üretilir, mevcutlarla çakışmaz |
| `info.description`, `info.version` | Koleksiyon açıklaması | Sürüm "Sürüm: x" satırı olarak eklenir |
| `servers[0].url` (sunucu değişkenleri varsayılanlarıyla) | `baseUrl` koleksiyon değişkeni | Varsayılanı olmayan sunucu değişkeni `{{ad}}` olur; diğer sunucular raporda bilgi |
| Swagger `schemes[0]://host + basePath` | `baseUrl` | |
| Göreli sunucu adresi (`/api`) | Olduğu gibi | Raporda uyarı: tam adres girilmeli |
| Sunucu yok | Boş `baseUrl` | Raporda uyarı |
| Path × metot | İstek, URL `{{baseUrl}}/path` | Ad: `summary`, yoksa `operationId`, yoksa `GET /path` |
| `description`, `deprecated` | İstek açıklaması | Kullanımdan kaldırılanlar "Kullanımdan kaldırıldı." ile başlar |
| İlk tag | Klasör | Sıra: üst düzey `tags` sırası, sonra ilk görülme; tag açıklaması klasöre geçer. Tag'siz işlemler kökte |
| Path parametresi `{id}` | URL'de `{{id}}` + `id` koleksiyon değişkeni | Değer: example, examples, default, enum[0]; yoksa boş |
| Query parametresi | Sorgu parametresi | Zorunlular açık, isteğe bağlılar kapalı. Diziler `explode`/`collectionFormat`'a göre tekrarlanır ya da ayraçla birleşir |
| Header parametresi | Header | `Accept`, `Content-Type`, `Authorization` OpenAPI kuralı gereği atlanır |
| Cookie parametreleri | Tek `Cookie` header'ı | `ad=değer; ...` |
| Path düzeyi parametreler | Her işleme eklenir | Aynı ad ve konumdaki işlem parametresi önceliklidir |
| `requestBody` (Swagger: `body`/`formData`) | Body | Aşağıya bakın |
| `security` + `securitySchemes` (Swagger: `securityDefinitions`) | Koleksiyon auth'u | İşlem düzeyi `security` geçersiz kılar; `security: []` → Auth yok |

### Body

Birden çok medya türü varsa sırayla tercih edilir: `application/json` (ve `+json`), `application/x-www-form-urlencoded`,
`multipart/form-data`, XML, `text/plain`, diğer `text/*`, ikili türler. Seçilmeyenler raporda bilgi olarak yazılır.

| Medya türü | JetApi | Not |
|---|---|---|
| JSON (`+json` dahil) | JSON | `application/json` dışındaki türlerde `Content-Type` header'ı eklenir |
| Form | Form | Şema özellikleri; zorunlular açık, isteğe bağlılar kapalı |
| Multipart | Multipart | `format: binary` ya da ikili `contentMediaType` → boş yollu dosya alanı (raporda uyarı); `encoding.contentType` korunur; nesneler JSON parça olur |
| XML | XML | Örnek yoksa şemadan basit bir XML üretilir (raporda bilgi) |
| `text/*` | Metin | |
| Diğer (`application/octet-stream`, `image/png`…) | Dosya | Yol yer tutucu (`body.png`, `body.bin`); raporda uyarı |

Değer sırası: medya türünün `example`'ı, `examples` içindeki ilk değer, sonra şemadan üretilen örnek. Örnek üretimi:
şemanın kendi `example`/`examples`/`const`/`default` değeri, `enum`'un ilk değeri; nesnelerde tüm yazılabilir özellikler
(`readOnly` atlanır), dizilerde tek öğe, `allOf` birleştirilir, `oneOf`/`anyOf`'ta ilk (null olmayan) seçenek; biçimler
için sabit örnekler (`date-time`, `date`, `uuid`, `email`, `uri`…), sayılarda `minimum` ya da 0, boolean'da `true`.
Kendini içeren şemalar tekrarın başladığı yerde durur (raporda bilgi); derinlik sınırlıdır.

### Auth

Güvenlik gereksinimleri alternatiftir; JetApi'nin gönderebildiği ilk gereksinim kullanılır. Bir gereksinim birden çok
şemayı birlikte isterse yalnızca ilki uygulanır ve raporda yazılır. Kimlik bilgileri koleksiyon değişkenlerine boş ve
`secret` olarak eklenir.

| Şema | JetApi |
|---|---|
| `http` + `bearer` | Bearer `{{token}}` |
| `http` + `basic` (Swagger: `basic`) | Basic `{{username}}` / `{{password}}` |
| `apiKey` header ya da query | API key, değer `{{apiKey}}` (ikinci bir API key şeması kendi adıyla bir değişken alır) |
| `apiKey` cookie | Auth yok; raporda eklenecek `Cookie` header'ı yazılır |
| `oauth2`, `openIdConnect`, diğer `http` şemaları | Auth yok; raporda uyarı |

## Taşınmayanlar

- Yanıtlar ve yanıt şemaları (JetApi istekleri saklar).
- Webhook'lar ve callback'ler (API'nin yaptığı çağrılar; raporda bilgi).
- İşlem ya da path düzeyindeki ayrı `servers` (istek `{{baseUrl}}` kullanır; raporda bilgi).
- Başka dosyadaki referanslar ve `externalValue` örnekleri (raporda).
- OAuth 2.0 ve OpenID Connect (raporda; OAuth 2.0 desteği ayrıca geliyor).
