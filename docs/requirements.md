# Gereksinimler

Öncelikler [yol haritasındaki](roadmap.md) kilometre taşlarına karşılık gelir.
**Durum** sütunu: ✅ hazır · 🟡 kısmen hazır · 🔜 planlandı.

## Fonksiyonel gereksinimler

### Workspace ve koleksiyonlar

| ID | Gereksinim | Öncelik | Durum |
|---|---|---|---|
| WS-1 | Bir klasörü workspace olarak açma ve yeni workspace oluşturma (`jetapi.yaml`) | M1 | ✅ |
| WS-2 | Son açılan workspace'ler listesi, workspace'ler arası hızlı geçiş | M1 | ✅ |
| WS-3 | Diskteki değişiklikleri izleme (ör. `git pull` sonrası) ve arayüzü yenileme | M1 | ✅ |
| COL-1 | Koleksiyon, iç içe klasör ve istek oluşturma, yeniden adlandırma, silme | M1 | ✅ |
| COL-2 | Sürükle-bırak ile sıralama ve taşıma | M1 | ✅ |
| COL-3 | İsteği çoğaltma (duplicate) | M1 | ✅ |
| COL-4 | Koleksiyon ve klasör düzeyinde auth tanımlama, alt öğelerin miras alması | M1 | ✅ |
| COL-5 | Koleksiyon/klasör/istek açıklaması (Markdown) | M1 | ✅ |
| COL-6 | Koleksiyon ağacında arama | M1 | ✅ |

### Environment ve değişkenler

| ID | Gereksinim | Öncelik | Durum |
|---|---|---|---|
| ENV-1 | Environment oluşturma, düzenleme, çoğaltma, silme; aktif environment seçimi | M1 | ✅ |
| VAR-1 | `{{değişken}}` sözdizimi; öncelik: global < koleksiyon < environment < çalışma anı | M1 | ✅ |
| VAR-2 | İç içe değişkenler (`baseUrl = {{scheme}}://{{host}}`) ve döngü tespiti | M1 | ✅ |
| VAR-3 | Dinamik değişkenler: `$guid`, `$timestamp`, `$timestampMs`, `$isoTimestamp`, `$randomInt` | M1 | ✅ |
| VAR-4 | Editörde değişken vurgulama; üzerine gelince çözümlenmiş değer ve kaynağı | M1 | ✅ Body ve gRPC mesaj editörlerinde vurgulama (tanımlı/tanımsız ayrı renk); URL, tablo hücreleri, auth alanları ve editörlerde üzerine gelince değer ve kaynak |
| VAR-5 | Tanımsız değişken uyarısı (gönderimden önce) | M1 | ✅ |
| VAR-6 | Gizli (secret) işaretli değişkenlerin arayüzde maskelenmesi | M1 | ✅ |

### REST

| ID | Gereksinim | Öncelik | Durum |
|---|---|---|---|
| REST-1 | Tüm HTTP method'ları ve özel method'lar | M1 | ✅ |
| REST-2 | Query parametreleri ve header'lar (aç/kapa, açıklama) | M1 | ✅ |
| REST-3 | Body: JSON, XML, text, form-urlencoded, multipart (dosya dahil), binary dosya | M1 | ✅ |
| REST-4 | Yanıt: durum, header'lar (tekrarlananlar dahil), body (pretty/raw/hex), boyut | M1 | ✅ |
| REST-5 | Zamanlama: DNS, TCP bağlantı, ilk bayt, indirme, toplam; bağlantı yeniden kullanımı | M1 | ✅ |
| REST-6 | Gönderilen isteğin birebir görünümü (uygulamanın eklediği header'lar dahil) | M1 | ✅ |
| REST-7 | İstek bazında: yönlendirme takibi, TLS doğrulama, zaman aşımı | M1 | ✅ |
| REST-8 | İsteği iptal etme | M1 | ✅ |
| REST-9 | Büyük yanıtlarda bellek koruması (kesme + uyarı) | M1 | ✅ |
| REST-10 | Yanıtı dosyaya kaydetme | M1 | ✅ |
| REST-11 | Cookie jar (workspace bazında, isteğe bağlı) | M2 | 🔜 |
| REST-12 | Proxy ayarları (sistem proxy'si varsayılan) | M2 | ✅ |
| REST-13 | İstemci sertifikası (mTLS) | M4 | 🔜 |

### gRPC

| ID | Gereksinim | Öncelik | Durum |
|---|---|---|---|
| GRPC-1 | Koleksiyon düzeyinde `.proto` dosyaları ve import path'leri | M1 | ✅ |
| GRPC-2 | Proto derleme hatalarının dosya/satır/kolon ile gösterilmesi | M1 | ✅ |
| GRPC-3 | Servis ve metot seçimi; istek mesajı için örnek JSON şablonu | M1 | ✅ |
| GRPC-4 | Unary çağrı; JSON ↔ protobuf (proto3 JSON eşlemesi, well-known type'lar) | M1 | ✅ |
| GRPC-5 | Metadata, auth (Bearer/Basic/API key metadata olarak) | M1 | ✅ |
| GRPC-6 | Yanıt: status kodu/adı/detayı, header'lar, trailer'lar, süre | M1 | ✅ |
| GRPC-7 | Deadline, TLS doğrulama, plaintext (h2c) | M1 | ✅ |
| GRPC-8 | JSON hatalarının alan yolu ile gösterilmesi (`$.user.roles[2]: ...`) | M1 | ✅ |
| GRPC-9 | Server reflection ile şema keşfi | M4 | 🔜 |
| GRPC-10 | Server, client ve bidirectional streaming | M4 | 🔜 |
| GRPC-11 | `google.rpc.Status` detaylarının (error details) çözülmesi | M4 | 🔜 |

### Kimlik doğrulama

| ID | Gereksinim | Öncelik | Durum |
|---|---|---|---|
| AUTH-1 | None, Inherit, Bearer, Basic, API Key (header/query) | M1 | ✅ |
| AUTH-2 | OAuth 2.0: client credentials, authorization code (+PKCE), password; token önbelleği ve yenileme | M2 | 🔜 |
| AUTH-3 | Digest, NTLM (kurumsal ihtiyaç doğarsa) | Sonra | 🔜 |

### Import / export

| ID | Gereksinim | Öncelik | Durum |
|---|---|---|---|
| IMP-1 | Postman Collection v2.1 import (klasörler, auth, değişkenler, body türleri) | M2 | ✅ v2.0/v2.1; aktarılamayanlar (script, OAuth 2.0, örnek yanıtlar) raporda ([import.md](import.md)) |
| IMP-2 | Postman Environment import | M2 | ✅ |
| IMP-3 | OpenAPI 3.x / Swagger 2.0 import | M2 | 🔜 |
| IMP-4 | cURL import (yapıştır) ve cURL export | M2 | 🔜 |
| IMP-5 | Kod parçası üretimi (C# HttpClient, curl, JavaScript fetch, Python requests) | M2 | 🔜 |
| IMP-6 | Postman test script'lerinin (`pm.test`) import'u | M3 | 🔜 |

### Test ve otomasyon

| ID | Gereksinim | Öncelik | Durum |
|---|---|---|---|
| TEST-1 | Kodsuz assertion'lar: status, header, JSONPath, yanıt süresi, body içerir/eşittir, JSON Schema | M3 | 🔜 |
| TEST-2 | Yanıttan değişken çıkarma (ör. token'ı JSONPath ile al → environment) | M3 | 🔜 |
| TEST-3 | Koleksiyon/klasör çalıştırıcı: sıra, gecikme, tekrar, hata durumunda durma | M3 | 🔜 |
| TEST-4 | Veri odaklı çalıştırma: CSV/JSON dosyasındaki her satır için koşma | M3 | 🔜 |
| TEST-5 | Raporlar: JUnit XML (CI), HTML | M3 | 🔜 |
| TEST-6 | CLI: `jetapi run <koleksiyon> -e <env>` (Windows/macOS/Linux, CI için) | M3 | 🔜 |
| TEST-7 | JavaScript pre-request/test script'leri (Postman `pm` API alt kümesi) | M5 | 🔜 |

### Geçmiş ve arayüz

| ID | Gereksinim | Öncelik | Durum |
|---|---|---|---|
| HIS-1 | İstek geçmişi (yerel, Git'e girmez), geçmişten tekrar gönderme/kaydetme | M1 | ✅ |
| UI-1 | Sekmeli istek editörü, kaydedilmemiş değişiklik göstergesi | M1 | ✅ |
| UI-2 | Komut paleti (Ctrl/Cmd+K), klavye kısayolları (Ctrl/Cmd+Enter: gönder, Ctrl/Cmd+S: kaydet) | M1 | ✅ |
| UI-3 | Açık/koyu tema, sistem temasını izleme | M1 | ✅ |
| UI-4 | JSON/XML sözdizimi vurgulama, katlama, arama | M1 | 🟡 vurgulama ve arama (Ctrl+F) hazır; katlama sonraki adım |
| I18N-1 | Türkçe ve İngilizce arayüz; varsayılan işletim sistemi dili, ayarlardan değiştirilebilir | M1 | ✅ |

### Sırlar (secrets)

| ID | Gereksinim | Öncelik | Durum |
|---|---|---|---|
| SEC-1 | Faz 1: sırlar environment dosyalarında, Git'e girebilir (bilinçli karar, bkz. ADR-0004) | M1 | ✅ |
| SEC-2 | Faz 2: `*.local.yaml` ile kişisel, Git'e girmeyen değer üzerine yazma | M5 | 🔜 |
| SEC-3 | Faz 2: secret değerlerin işletim sistemi anahtarlığında (Windows Credential Manager, macOS Keychain, libsecret) tutulması | M5 | 🔜 |

## Fonksiyonel olmayan gereksinimler

| ID | Gereksinim | Hedef |
|---|---|---|
| NFR-1 | Soğuk açılış (orta segment dizüstü, 500 istekli workspace) | < 1,5 sn |
| NFR-2 | Boşta bellek kullanımı | < 200 MB |
| NFR-3 | 50 MB yanıt | Arayüz donmaz; görüntüleme sanallaştırılmış |
| NFR-4 | İstek başına uygulama ek yükü (hazırlama + kayıt) | < 5 ms |
| NFR-5 | Platformlar | Windows 10+, macOS 13+, Linux x64 (Ubuntu 22.04+), arm64 dahil |
| NFR-6 | Gizlilik | Telemetri yok; kullanıcının açıkça tetiklemediği hiçbir ağ isteği yok |
| NFR-7 | Dayanıklılık | Bozuk bir dosya workspace'in geri kalanının yüklenmesini engellemez; dosyaya yazma atomiktir |
| NFR-8 | Uyumluluk | Dosya formatı `schemaVersion` ile sürümlenir; eski sürümler yeni formatı bozmaz, uyarır |
| NFR-9 | Kalite | CI'da üç işletim sisteminde derleme ve test; format ve analizör ihlali yok; bilinen açığı olan paket yok |
| NFR-10 | Erişilebilirlik | Tüm etkileşimler klavyeyle yapılabilir; WCAG AA kontrast |
