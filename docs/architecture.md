# Mimari

## Genel bakış

JetApi, .NET 10 üzerinde çalışan bir masaüstü uygulamasıdır. Arayüz Avalonia ile yazılır; iş mantığı arayüzden
bağımsız kütüphanelerde durur. Böylece aynı motor masaüstü uygulamasında, CLI'da (M3) ve testlerde kullanılır.

```
┌──────────────────────────┐   ┌──────────────────────────┐
│   JetApi.Desktop         │   │   JetApi.Cli  (M3)       │
│   Avalonia + MVVM        │   │   CI'da koleksiyon koşar │
└────────────┬─────────────┘   └────────────┬─────────────┘
             │                              │
   ┌─────────┼───────────────┬──────────────┼──────────┐
   ▼         ▼               ▼              ▼          ▼
┌───────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐
│JetApi.Storage │  │ JetApi.Http  │  │ JetApi.Grpc  │  │ JetApi.Import (M2)   │
│YAML workspace │  │ REST motoru  │  │ proto+codec  │  │ Postman/OpenAPI/cURL │
└───────┬───────┘  └──────┬───────┘  └──────┬───────┘  └──────────┬───────────┘
        └─────────────────┴────────┬────────┴─────────────────────┘
                                   ▼
                        ┌─────────────────────┐
                        │    JetApi.Core      │
                        │ Domain + değişkenler│
                        │   (I/O yok)         │
                        └─────────────────────┘
```

### Bağımlılık kuralları

- `JetApi.Core` hiçbir projeye ve hiçbir I/O'ya bağımlı değildir. Saf domain modeli ve kurallardır.
- Motor projeleri (`Storage`, `Http`, `Grpc`, `Import`) yalnızca `Core`'a bağımlıdır, birbirlerine bağımlı değildir.
- Arayüz (`Desktop`) ve `Cli` motorları birleştirir. Motorlar arayüzü bilmez.
- Döngüsel bağımlılık yoktur. Yeni bir proje eklemek ADR gerektirir.

## Projeler

| Proje | Sorumluluk | Önemli tipler |
|---|---|---|
| `JetApi.Core` | Workspace, koleksiyon, klasör, istek ve environment modeli (immutable `record`'lar); auth mirası; değişken çözümleme; OAuth 2.0 yapılandırması ve token modeli | `Workspace`, `ApiCollection`, `HttpRequestDefinition`, `GrpcRequestDefinition`, `AuthResolver`, `VariableResolver`, `OAuth2Auth`, `OAuth2Token` |
| `JetApi.Storage` | Workspace'in Git dostu YAML dosyalarına yazılması ve okunması ([dosya formatı](file-format.md)); makineye özel OAuth token önbelleği | `WorkspaceStore`, `WorkspaceLayout`, `OAuth2TokenStore` |
| `JetApi.Http` | İsteği hazırlama (değişken, auth, URL, body) ve gönderme; zamanlama ölçümü; OAuth 2.0 token alma, yenileme ve tarayıcıyla oturum açma | `HttpRequestPreparer`, `HttpRequestExecutor`, `OAuth2Client`, `AuthorizationCodeFlow` |
| `JetApi.Grpc` | `.proto` dosyalarını çalışma anında yükleme; JSON ↔ protobuf codec; unary çağrı | `ProtoSchemaLoader`, `ProtoJsonCodec`, `ProtoJsonTemplate`, `GrpcRequestPreparer`, `GrpcInvoker` |
| `JetApi.Desktop` | Avalonia arayüzü. `Services`: workspace oturumu, istek çalıştırıcı, proto önbelleği, geçmiş, dosya izleyici, ayarlar, dialoglar. `ViewModels`: MVVM (CommunityToolkit.Mvvm), UI'dan bağımsız ve test edilebilir. `Views`: AXAML. `Localization`: TR/EN `.resx` | `WorkspaceSession`, `RequestRunner`, `WorkspaceViewModel`, `HttpRequestTabViewModel`, `GrpcRequestTabViewModel` |

## Temel akışlar

### REST isteği gönderme

```
HttpRequestDefinition ──┐
AuthResolver.Resolve ───┼─► HttpRequestPreparer.Prepare ─► PreparedHttpRequest ─► HttpRequestExecutor.ExecuteAsync ─► HttpExecutionResult
VariableResolver ───────┘        │                                                       │
                                 └─ hatalar + çözümlenemeyen değişkenler                 └─ status, header'lar, body, zamanlama,
                                    (gönderimden önce kullanıcıya)                          gönderilen istek, hata mesajı
```

1. **Hazırlama** saf bir fonksiyondur: değişkenleri çözer, auth'u uygular, URL'yi kurar, body'yi oluşturur ve doğrular. Ağ erişimi yoktur, birim testi kolaydır.
2. **Gönderme**, uygulama boyunca tek bir `HttpRequestExecutor` örneğiyle yapılır. Bağlantılar havuzlanır (tarayıcı gibi). Ağ ve HTTP hataları exception olarak değil, `Outcome` alanıyla döner.
3. **Zamanlama**: DNS ve TCP bağlantı süresi `SocketsHttpHandler.ConnectCallback` içinde ölçülür. İlk bayt süresi (TTFB) başlangıçtan header'lar gelene kadardır ve TLS el sıkışmasını içerir. Bağlantı yeniden kullanıldıysa DNS ve bağlantı süreleri `null` olur.

### gRPC çağrısı

```
ProtoSettings ─► ProtoSchemaLoader.Load ─► ProtoSchema (önbelleğe alınır, proto dosyaları değişince yenilenir)
GrpcRequestDefinition + ProtoSchema ─► GrpcRequestPreparer ─► PreparedGrpcCall (mesaj binary'ye çevrilmiş)
                                                              └─► GrpcInvoker.InvokeUnaryAsync ─► GrpcCallResult (yanıt JSON'a çevrilmiş)
```

Kod üretimi yoktur. Mesajlar ham bayt olarak taşınır ve `ProtoJsonCodec` ile dönüştürülür (bkz. [ADR-0003](adr/0003-dynamic-grpc-without-codegen.md)).

### OAuth 2.0

```
OAuth2Auth (YAML) ─► OAuth2Auth.Resolve(değişkenler) ─► OAuth2Client.GetTokenAsync ─► OAuth2Token ─► OAuth2TokenStore.Set
                                                         │  client_credentials, password: doğrudan token isteği
                                                         └─ authorization_code: AuthorizationCodeFlow (tarayıcı + loopback + PKCE)
                                                            ─► ExchangeCodeAsync
HttpRequestPreparer.Prepare(..., tokens: auth => store.Get(auth)) ─► Authorization: Bearer <token>   (token yoksa hazırlama hatası)
```

1. Token'lar YAML'da değil, `.jetapi/tokens.json` dosyasında, çözümlenmiş yapılandırmanın özetiyle anahtarlanarak durur ([ADR-0007](adr/0007-oauth2-tokens.md)).
2. Hazırlayıcılar (`HttpRequestPreparer`, `GrpcRequestPreparer`) saf kalır: token'ı çağıranın verdiği fonksiyondan alır. Süresi dolan token'ı yenilemek (`RefreshAsync`) çağıranın işidir.
3. Tarayıcıyı açmak çağıranın işidir (`Func<Uri, Task>`); motor yalnızca loopback adresinde dinler, `state` doğrular ve sekmede "kapatabilirsiniz" sayfası gösterir.

## Çapraz kesen kararlar

- **Hata yönetimi:** Kullanıcı girdisinden kaynaklanan sorunlar (geçersiz URL, JSON hatası, bozuk dosya) exception fırlatmaz. Kullanıcıya gösterilecek mesajlarla sonuç nesnesinde döner. Exception'lar programlama hataları ve geri dönülemez durumlar içindir.
- **Immutable model:** Domain nesneleri `record`'dur. Arayüz düzenlemeleri `with` ile yeni nesne üretir. Bu yapı geri al/yinele (undo/redo) ve değişiklik tespitini kolaylaştırır.
- **İş parçacığı güvenliği:** `HttpRequestExecutor` ve `GrpcInvoker` thread-safe'tir ve uygulama boyunca tek örnek olarak yaşar. Hazırlayıcılar saf fonksiyondur.
- **Kullanıcıya dönük metinler:** Her motor (`Http`, `Grpc`, `Storage`) kendi `Messages.resx` (İngilizce) ve `Messages.tr.resx` (Türkçe) dosyasını taşır ve mesajı `CurrentUICulture` diline göre üretir. Böylece CLI da aynı çevirileri kullanır. Arayüz dil değişince `CurrentUICulture` ve `DefaultThreadCurrentUICulture` değerlerini ayarlar. Dış kütüphanelerden gelen ayrıntılar (YAML/JSON ayrıştırıcı, soket, sunucu mesajları) çevrilmeden ayrıntı olarak eklenir. Her test projesi İngilizce kültürü sabitler, iki dilin anahtar eşitliğini ve kodda kullanılan her anahtarın var olduğunu denetler (bkz. [ADR-0006](adr/0006-localization.md)).
- **Kod kalitesi:** Uyarılar hata sayılır (`TreatWarningsAsErrors`), .NET analizörleri `latest-recommended` seviyesindedir, public API XML ile belgelenir, paket sürümleri merkezi olarak sabitlenir ve lock dosyaları commit'lenir.

## Test stratejisi

| Katman | Yaklaşım |
|---|---|
| Core | Saf birim testleri (sahte saat ve rastgelelik enjekte edilir) |
| Storage | Gerçek geçici dizinlerde gidiş-dönüş, dosya içeriğinin birebir kontrolü, hatalı dosya senaryoları |
| Http | Süreç içi gerçek Kestrel sunucusuna karşı uçtan uca testler (yönlendirme, gzip, zaman aşımı, iptal, multipart) |
| Grpc | Codec, Google'ın resmi kod üretimi ve resmi JSON parser/formatter'ı ile çapraz doğrulanır. Çağrılar süreç içi gerçek bir gRPC sunucusuna karşı test edilir |
| Desktop | ViewModel testleri (gerçek geçici workspace'ler, yerel HTTP sunucusu) ve Avalonia headless testleri: her ekran Skia ile çizilir, binding hatası testi başarısız yapar, kısayollar klavye olaylarıyla denenir |

CI her PR'da Windows, macOS ve Linux üzerinde derler ve testleri çalıştırır; format ve güvenlik açığı kontrolü yapar.
