# Workspace Dosya Formatı (schemaVersion 1)

Bir workspace, genellikle bir Git deposu olan bir klasördür. Her öğe ayrı ve küçük bir YAML dosyasıdır.
Tasarım hedefleri:

- **Okunabilir diff.** Varsayılan değerler yazılmaz. Anahtar sırası sabittir. Çok satırlı içerik `|` bloğu olarak yazılır. Satır sonları her zaman LF'dir.
- **Az çakışma.** Bir isteği değiştirmek yalnızca o isteğin dosyasını değiştirir. Sıralama, üst klasörün `order` listesinde tutulur.
- **Elle düzenlenebilir.** Bilinmeyen anahtarlar (yazım hataları) sessizce yutulmaz, hata olarak raporlanır.
- **Dayanıklı.** Okunamayan bir dosya atlanır ve raporlanır, workspace'in geri kalanı yüklenir. Yazma atomiktir. Kaydetme hiçbir dosyayı silmez; silme her zaman açık bir işlemdir.

## Dizin yapısı

```
my-workspace/
├── jetapi.yaml                          # workspace manifest
├── environments/
│   ├── dev.yaml
│   └── prod.yaml
├── collections/
│   └── user-service/                    # koleksiyon anahtarı = dizin adı
│       ├── collection.yaml
│       ├── list-users.http.yaml         # istek anahtarı = dosya adı
│       ├── get-user.grpc.yaml
│       └── admin/                       # klasör anahtarı = dizin adı
│           ├── folder.yaml
│           └── create-user.http.yaml
├── protos/                              # (öneri) proto dosyaları
└── .jetapi/                             # makineye özel durum (geçmiş, arayüz, OAuth token'ları) — .gitignore'da
```

**Anahtarlar** (`key`) kardeşler arasında benzersizdir. Büyük/küçük harf farkı gözetilmez, çünkü Windows ve macOS dosya
sistemleri harf duyarsızdır. Uygulama yeni öğeler için anahtarı addan üretir: `Kullanıcı Güncelle` → `kullanici-guncelle`.

## `jetapi.yaml`

```yaml
schemaVersion: 1
name: Şirket API'leri
description: Ortak istekler            # opsiyonel
globals:                                # opsiyonel, en düşük öncelikli değişkenler
- name: userAgent
  value: JetApi
collections:                            # görüntüleme sırası; listede olmayanlar alfabetik eklenir
- user-service
environments:
- dev
- prod
```

## `environments/<key>.yaml`

```yaml
schemaVersion: 1
name: Development
variables:
- name: baseUrl
  value: http://localhost:5000
- name: token
  value: dev-token
  secret: true          # arayüzde maskelenir (varsayılan false)
- name: legacyHost
  value: old.example.com
  enabled: false        # çözümlemede yok sayılır (varsayılan true)
```

## `collections/<key>/collection.yaml`

```yaml
schemaVersion: 1
name: User Service
description: ...               # opsiyonel, Markdown
auth:                          # opsiyonel, varsayılan: none
  type: bearer
  token: '{{token}}'
variables:                     # opsiyonel, global'den yüksek, environment'tan düşük öncelik
- name: version
  value: v1
proto:                         # opsiyonel, gRPC istekleri için
  importPaths:                 # workspace köküne göre; boşsa workspace kökü
  - protos
  files:                       # import path'lere göre
  - users/v1/users.proto
order:                         # alt öğelerin sırası
- list-users
- admin
- get-user
```

## `folder.yaml`

```yaml
schemaVersion: 1
name: Admin
auth:                          # opsiyonel, varsayılan: inherit
  type: basic
  username: admin
  password: '{{adminPassword}}'
order:
- create-user
```

## `<key>.http.yaml` — REST isteği

```yaml
schemaVersion: 1
name: Create user
description: ...                # opsiyonel
method: POST                    # varsayılan GET
url: '{{baseUrl}}/users'
params:                         # query parametreleri, URL'ye eklenir
- name: dryRun
  value: true
  enabled: false
  description: Sadece doğrula
headers:
- name: X-Trace
  value: '{{$guid}}'
auth:                           # opsiyonel, varsayılan: inherit
  type: none
body:                           # opsiyonel, varsayılan: gövde yok
  type: json
  content: |-
    {
      "name": "Ayşe"
    }
settings:                       # opsiyonel, yalnızca varsayılandan farklı olanlar yazılır
  followRedirects: false        # varsayılan true
  validateCertificates: false   # varsayılan true
  timeoutMs: 5000               # varsayılan: uygulama ayarı (30 sn)
```

### Body türleri

| `type` | Alanlar | Gönderilen `Content-Type` |
|---|---|---|
| `json` | `content` | `application/json` |
| `xml` | `content` | `application/xml` |
| `text` | `content` | `text/plain; charset=utf-8` |
| `form` | `fields` (name/value/enabled/description listesi) | `application/x-www-form-urlencoded` |
| `multipart` | `parts`: her biri `name` + (`value` **veya** `file`), opsiyonel `contentType`, `enabled` | `multipart/form-data` |
| `file` | `file` (workspace köküne göre yol) | `application/octet-stream` |

Kullanıcının yazdığı `Content-Type` header'ı her zaman varsayılanı ezer.

## `<key>.grpc.yaml` — gRPC isteği

```yaml
schemaVersion: 1
name: Get user
url: http://localhost:5001       # https:// = TLS, http:// = plaintext (h2c)
service: users.v1.UserService    # paket dahil tam ad
method: GetUser
metadata:
- name: x-tenant
  value: acme
auth:
  type: none
message: |-                      # proto3 JSON eşlemesi
  {
    "id": "42"
  }
settings:
  validateCertificates: false
  deadlineMs: 3000
```

## `tests` ve `extract` — yanıt kontrolleri ve değişken çıkarma

REST ve gRPC istek dosyalarında isteğe bağlı iki bölüm ([ADR-0005](adr/0005-assertions-before-scripting.md),
[ADR-0008](adr/0008-checks-jsonpath-and-schema.md)). Her gönderimde çalışır; değerlerde `{{değişken}}` kullanılabilir.

```yaml
tests:
- check: status          # status | header | json | body | time | schema
  op: equals
  value: 200
- check: header
  name: Content-Type
  op: contains
  value: application/json
- check: json
  path: $.items[?@.active].id   # JSONPath (RFC 9535)
  op: exists
- check: time            # milisaniye
  op: lessThan
  value: 500
- check: schema          # op yok; value bir JSON Schema
  value: |-
    { "type": "object", "required": ["id"] }
  enabled: false         # yalnızca kapalıyken yazılır
extract:
- from: json             # json | header | status | body
  path: $.access_token
  variable: token
  to: environment        # runtime (varsayılan, yazılmaz) | environment | collection | global
- from: header
  name: Location
  variable: nextUrl
```

| `check` | Hedef | Değer | Not |
|---|---|---|---|
| `status` | — | sayı | gRPC'de status kodu numarası (0 = OK) |
| `header` | `name` (büyük/küçük harf duyarsız) | metin | aynı adda birden fazla header `, ` ile birleşir |
| `json` | `path` | JSON ya da metin | birden fazla düğüm seçen yol bir dizi gibi kontrol edilir |
| `body` | — | metin | gRPC'de yanıt mesajının JSON hali |
| `time` | — | milisaniye | |
| `schema` | — | JSON Schema | `op` yazılmaz; Draft 7, 2019-09, 2020-12 |

`op`: `equals`, `notEquals`, `greaterThan`, `lessThan`, `contains`, `notContains`, `matches` (düzenli ifade, metnin
herhangi bir yerinde), `exists`, `notExists`.

- `equals`: sayılar değere göre (`200` = `200.0`); JSON değerleri yapıya göre karşılaştırılır. Metin bir JSON değeriyse
  hem `Ayşe` hem `"Ayşe"` yazılabilir.
- `contains`: metinde alt metin; dizide eşit bir eleman; nesnede bu adda bir alan.
- `extract`: JSON metinleri tırnaksız, diğer JSON değerleri JSON metni olarak alınır. Değer bulunamazsa değişken
  değiştirilmez ve sonuçta neden bulunamadığı yazılır. Çalışma anı değişkenleri dosyaya yazılmaz; uygulama açıkken
  (ya da bir koşu boyunca) geçerlidir ve environment'taki aynı adlı değişkeni ezer.

## `scripts` — içe aktarmadan saklanan Postman script'leri

İstek, `folder.yaml` ve `collection.yaml` dosyalarında isteğe bağlı bölüm ([ADR-0010](adr/0010-postman-scripts.md)).
Postman'den içe aktarmada kurallara çevrilemeyen JavaScript burada **aynen** saklanır. JetApi bu script'leri
**çalıştırmaz**; uygulama düzenlemez ama istek kaydedilirken korur.

```yaml
scripts:
  preRequest: |-
    pm.variables.set('ts', Date.now());
  test: |-
    pm.test('liste dolu', function () {
        pm.expect(pm.response.json().items.length).to.be.above(0);
    });
```

Boş script yazılmaz. Bölüm yalnızca saklanacak bir script varsa bulunur.

## Auth türleri

| `type` | Alanlar | REST | gRPC |
|---|---|---|---|
| `inherit` | — | Üst klasör/koleksiyonun auth'u | Aynı |
| `none` | — | Auth yok | Aynı |
| `bearer` | `token` | `Authorization: Bearer <token>` | `authorization` metadata |
| `basic` | `username`, `password` | `Authorization: Basic …` (UTF-8) | `authorization` metadata |
| `apikey` | `key`, `value`, `in: header\|query` (varsayılan header) | Header veya query parametresi | Yalnızca metadata |
| `oauth2` | Aşağıya bakın | `Authorization: Bearer <access token>` | `authorization` metadata |

Kullanıcı açıkça bir `Authorization` header'ı yazdıysa, auth sekmesi bu header'ı ezmez.

### `oauth2`

```yaml
auth:
  type: oauth2
  grant: authorization_code      # zorunlu: client_credentials | authorization_code | password
  tokenUrl: '{{idp}}/oauth/token'
  authUrl: '{{idp}}/authorize'   # yalnızca authorization_code
  clientId: '{{clientId}}'
  clientSecret: '{{clientSecret}}' # public client'ta boş bırakılır
  scope: openid profile          # boşluklarla ayrılmış
  audience: https://api.example.com  # opsiyonel ek parametre (Auth0 vb.)
  username: ayse                 # yalnızca password
  password: '{{password}}'       # yalnızca password
  redirectUri: http://127.0.0.1:7777/callback  # varsayılan http://127.0.0.1:0/callback (0 = boş bir port)
  pkce: false                    # varsayılan true (S256)
  clientAuth: body               # varsayılan basic: client id/secret Basic header'da; body: form alanlarında
```

- Varsayılan değerler ve boş alanlar yazılmaz. Seçili grant'te kullanılmayan dolu alanlar (ör. grant değiştirildiğinde
  kalan `username`) korunur ve yok sayılır.
- Bütün metin alanları `{{değişken}}` içerebilir. Client secret ve şifreyi environment değişkenine koymak önerilir (ADR-0004).
- `redirectUri` bir loopback adresi olmalıdır (`127.0.0.1`, `localhost` veya `[::1]`, `http`). Oturum açma yanıtı bu
  adreste JetApi tarafından karşılanır (RFC 8252). Sunucu sabit bir port istiyorsa port açıkça yazılır.
- `clientAuth: basic` iken client id ve secret RFC 6749'a göre form kodlamasıyla (`application/x-www-form-urlencoded`)
  kodlanıp Basic header'a konur. Secret boşsa (public client) yalnızca `client_id` body'de gönderilir.
- **Token'lar bu dosyalara yazılmaz.** Workspace başına `.jetapi/tokens.json` dosyasında, çözümlenmiş yapılandırmanın
  özetiyle anahtarlanarak saklanır; farklı environment'ların token'ları karışmaz ([ADR-0007](adr/0007-oauth2-tokens.md)).

## Sürümleme

- Her dosya `schemaVersion` taşır. Desteklenenden yeni bir sürüm okunduğunda dosya atlanır ve "JetApi'yi güncelleyin" uyarısı verilir. Dosya asla üzerine yazılmaz.
- Formatı bozan bir değişiklik `schemaVersion`'ı artırır, CHANGELOG'da geçiş notuyla belgelenir ve uygulama eski sürümü otomatik olarak yükseltir.
- Yeni bir auth veya body türü ya da `tests`/`extract`/`scripts` bölümleri gibi **eklemeler** `schemaVersion`'ı artırmaz: mevcut dosyalar aynen okunur. Yeni türü
  kullanan bir dosyayı eski bir JetApi sürümü okuyamaz; dosyayı tanılama mesajıyla atlar (`oauth2` için: bilinmeyen
  `grant` anahtarı ya da bilinmeyen `auth.type`) ve üzerine yazmaz. `oauth2` bir koleksiyonun `collection.yaml` dosyasındaysa
  eski sürümde koleksiyonun tamamı atlanır. Ekipteki herkesin `oauth2` kullanmadan önce JetApi'yi güncellemesi gerekir.
