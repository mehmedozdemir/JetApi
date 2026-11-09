# Postman'den içe aktarma

OpenAPI / Swagger belgeleri de aynı komutla içe aktarılır; eşleştirmesi [openapi-import.md](openapi-import.md) dosyasındadır.

JetApi, Postman'den dışa aktarılan koleksiyon (Collection v2.0 ve v2.1) ve environment dosyalarını içe aktarır.
Hedef kayıpsız taşımadır. Taşınamayan her şey içe aktarma raporunda listelenir; hiçbir şey sessizce atılmaz.

## Nasıl kullanılır

- **Açık bir workspace'te:** Kenar çubuğunda Koleksiyonlar başlığındaki **+** menüsünden *İçe aktar (Postman, OpenAPI)…*
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
| Auth: oauth2 (client credentials, authorization code ±PKCE, password) | OAuth 2.0 | Token aktarılmaz, Auth sekmesinden yeni token alınır (raporda bilgi). Loopback olmayan redirect URI (ör. `oauth.pstmn.io`) `http://127.0.0.1:0/callback` olur; raporda uyarı |
| Diğer auth türleri (oauth2 implicit, digest, AWS…) | Auth yok | Başka kimlik bilgisi miras alınıp yanlışlıkla gönderilmesin diye; raporda uyarı |
| Koleksiyon değişkenleri | Koleksiyon değişkenleri | Devre dışı ve `secret` türü korunur |
| `followRedirects`, `strictSSL` | İstek ayarları | |
| Environment değerleri | Environment | `enabled` ve `secret` korunur |
| Dinamik değişkenler (`$guid`, `$randomUUID`, `$timestamp`, `$isoTimestamp`, `$randomInt`) | Aynı | Desteklenmeyenler (`$randomEmail` vb.) raporda |
| İsteğin test script'i | Testler (`tests`) ve çıkarma kuralları (`extract`) | Aşağıdaki ifadeler çevrilir; tanınmayan bir ifade varsa script dosyada saklanır |
| Pre-request script'i; klasör ve koleksiyon script'leri | Dosyada saklanır (`scripts`) | Çalıştırılmaz; raporda uyarı |

## Test script'lerinin çevrilmesi

JetApi henüz JavaScript çalıştırmaz (M5). İsteklerin test script'lerindeki yaygın ifadeler kodsuz kurallara çevrilir
([ADR-0010](adr/0010-postman-scripts.md)). `pm.test("ad", function () { … })` sarmalayıcıları ve yorumlar yok sayılır;
testlerin adları taşınmaz. `json`, `pm.response.json()` ya da `var json = pm.response.json();` (veya
`JSON.parse(responseBody)`) ile tanımlanan bir değişkendir; `json.a.b[0]["c-d"]` → `$.a.b[0]['c-d']`.

| Postman | JetApi |
|---|---|
| `pm.response.to.have.status(200)`, `pm.expect(pm.response.code).to.eql(200)`, `tests[…] = responseCode.code === 200` | `status equals 200` |
| `pm.response.to.be.ok` / `to.be.success` | `status equals 200` / `status greaterThan 199` ve `lessThan 300` |
| `pm.expect(pm.response.responseTime).to.be.below(500)`, `tests[…] = responseTime < 500` | `time lessThan 500` |
| `pm.response.to.have.header("X")`, `…header("X", "v")`, `pm.expect(pm.response.headers.get("X")).to.include("v")` | `header X exists / equals v / contains v` |
| `pm.response.to.have.body("…")`, `pm.expect(pm.response.text()).to.include("…")`, `tests[…] = responseBody.has("…")` | `body equals / contains` |
| `pm.expect(json.x).to.eql(v)` / `.to.equal(v)` / `.to.not.eql(v)` | `json $.x equals / notEquals v` |
| `.to.include(v)`, `.to.not.include(v)` | `contains` / `notContains` |
| `.to.be.above(n)`, `.to.be.below(n)` | `greaterThan` / `lessThan` |
| `.to.exist`, `.to.not.exist`, `.to.be.ok` | `exists` / `notExists` |
| `.to.be.true` / `.false` / `.null` | `equals true / false / null` |
| `.to.match(/re/)`, `/re/i` | `matches` (`i` → `(?i)`) |
| `pm.expect(json).to.have.property("id")`, `…property("page", 1)` | `$.id exists`, `$.page equals 1` |
| `pm.environment.set("t", json.token)` | çıkarma: `$.token` → `t`, environment |
| `pm.collectionVariables.set`, `pm.globals.set`, `pm.variables.set`; `postman.setEnvironmentVariable`, `postman.setGlobalVariable` | koleksiyon, global, çalışma anı; environment, global |
| …`.set("x", pm.response.headers.get("H"))`, `pm.response.code`, `pm.response.text()` / `responseBody` | header, status, body çıkarması |

Çevrilmeyenler (script saklanır): koşullar ve döngüler, `length`, nesne/dizi karşılaştırması (`to.eql({…})`), tür
kontrolleri (`to.be.a('string')`), `_` (lodash), `console.log`, `pm.sendRequest`, `/re/g` gibi bayraklar. Rapor her istek
için sonucu yazar: *dönüştürüldü* (bilgi), *kısmen dönüştürüldü* ya da *saklandı, çalışmıyor* (uyarı).

## Taşınmayanlar

- **JavaScript'in kendisi.** Çevrilemeyen script'ler dosyada saklanır ama çalışmaz (M5'te script desteği gelir).
  Uygulamada isteğin *Testler* sekmesi bunu bir notla gösterir; `jetapi` CLI koşu başında uyarı yazar.
- **Kayıtlı örnek yanıtlar.** Raporda bilgi olarak gösterilir.
- **Desteklenmeyen auth türleri.** Yukarıya bakın.

Rapor, uyarıları (dikkat gerektirenler) önce, bilgi notlarını (farklı çevrilenler) sonra gösterir.
