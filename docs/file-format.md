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
└── .jetapi/                             # makineye özel durum (geçmiş, arayüz) — .gitignore'da
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

## Auth türleri

| `type` | Alanlar | REST | gRPC |
|---|---|---|---|
| `inherit` | — | Üst klasör/koleksiyonun auth'u | Aynı |
| `none` | — | Auth yok | Aynı |
| `bearer` | `token` | `Authorization: Bearer <token>` | `authorization` metadata |
| `basic` | `username`, `password` | `Authorization: Basic …` (UTF-8) | `authorization` metadata |
| `apikey` | `key`, `value`, `in: header\|query` (varsayılan header) | Header veya query parametresi | Yalnızca metadata |

Kullanıcı açıkça bir `Authorization` header'ı yazdıysa, auth sekmesi bu header'ı ezmez.

## Sürümleme

- Her dosya `schemaVersion` taşır. Desteklenenden yeni bir sürüm okunduğunda dosya atlanır ve "JetApi'yi güncelleyin" uyarısı verilir. Dosya asla üzerine yazılmaz.
- Formatı bozan bir değişiklik `schemaVersion`'ı artırır, CHANGELOG'da geçiş notuyla belgelenir ve uygulama eski sürümü otomatik olarak yükseltir.
