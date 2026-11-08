# ADR-0007: OAuth 2.0 — token'lar makineye özel önbellekte

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-09

## Bağlam

OAuth 2.0 desteği (AUTH-2) iki tür veri getirir:

1. **Yapılandırma:** grant türü, token ve yetkilendirme URL'leri, client id/secret, scope, audience, kullanıcı adı/şifre,
   redirect URI, PKCE ve client kimlik doğrulama biçimi. Bunlar ekipçe paylaşılır ve `{{değişken}}` içerebilir.
2. **Token'lar:** access token, refresh token, bitiş zamanı ve verilen scope. Bunlar kısa ömürlüdür, kişiye ve makineye
   özeldir ve doğrudan yetki verir.

Postman token'ları koleksiyonun içinde tutar ve paylaşırken sızdırır. JetApi'nin dosyaları Git'e girer (ADR-0002).
ADR-0004 sırların 1. fazda environment dosyalarında durmasını kabul etmiştir ama bu yalnızca kullanıcının bilerek yazdığı
değerler içindir; bir token'ın kendiliğinden Git geçmişine girmesi kabul edilemez.

## Karar

- **Yapılandırma YAML'dadır**, diğer auth türleri gibi: `auth: { type: oauth2, grant: ..., tokenUrl: ..., ... }`
  ([dosya formatı](../file-format.md)). Client secret ve şifre ADR-0004'e tabidir: `{{değişken}}` ile environment'a
  konması önerilir.
- **Token'lar asla YAML'a ve Git'e yazılmaz.** Workspace başına `.jetapi/tokens.json` dosyasında tutulur
  (`OAuth2TokenStore`, `JetApi.Storage`). `.jetapi/` zaten Git dışıdır; ayrıca dosyayı yazan kod, dizine `*` içeren bir
  `.jetapi/.gitignore` koyar. Böylece JetApi ile oluşturulmamış workspace'lerde de token commit'lenmez. Unix'te dosya
  yalnızca kullanıcı tarafından okunabilir (`600`).
- **Anahtar, çözümlenmiş yapılandırmanın özetidir:** SHA-256(grant, token URL, client id, scope, audience, password
  grant'te kullanıcı adı). Değişkenler çözümlendikten sonra hesaplandığı için `dev` ve `prod` environment'ları aynı
  isteği kullansa bile token'ları karışmaz. Secret ve şifre anahtara girmez: değişmeleri token'ı geçersiz kılmaz. Dosyada
  URL veya client id açık metin olarak görünmez.
- **Motor sorumlulukları ayrıdır:** `OAuth2Client` (`JetApi.Http`) token alır ve yeniler; `AuthorizationCodeFlow` tarayıcı
  ile oturum açmayı loopback adresinde (RFC 8252, `127.0.0.1`, boş port) karşılar ve PKCE S256 kullanır;
  `HttpRequestPreparer` ve `GrpcRequestPreparer` saf kalır: token'ı çağıranın verdiği bir fonksiyondan alır, token yoksa
  "token alın" hatası döner. Token modeli (`OAuth2Token`) iki motorun da kullanabilmesi için `JetApi.Core`'dadır.
- **Önbellek bir kolaylıktır:** bozuk dosya yok sayılır, yazılamayan dosya kullanıcıyı durdurmaz; en kötü durumda
  kullanıcı yeni bir token alır. Süresi dolmuş ve refresh token'ı olmayan token'lar yazarken atılır.

## Sonuçlar

- Token'lar paylaşılmaz: her ekip üyesi kendi token'ını alır. Bu bilinçli bir tercihtir.
- Token'lar diskte düz metindir (makineye özel ve Git dışında olsa da). ADR-0004'ün 2. fazında (M5) işletim sistemi
  anahtarlığına geçildiğinde `OAuth2TokenStore` aynı arayüzle anahtarlığa taşınır; dosya biçimi `version` alanı taşır.
- Yeni bir auth türü eklemek dosya formatını bozmaz (`schemaVersion` 1 kalır); eski sürümler bu auth'u içeren dosyayı
  tanılama mesajıyla atlar, dosyanın üzerine yazmaz.
