# Yol Haritası

Her kilometre taşı kullanılabilir bir sürümle biter. Kapsam, [gereksinimlerdeki](requirements.md) önceliklere karşılık gelir.
Sürümler [Semantic Versioning](https://semver.org/lang/tr/) izler. 1.0'a kadar dosya formatı dışındaki her şey değişebilir.

## M0 — Temel ✅ (bu sürüm)

Amaç: Geri dönüşü zor kararları vermek ve en riskli teknik parçaları kanıtlamak.

- [x] Analiz: vizyon, kullanıcılar, gereksinimler, kapsam dışı
- [x] Mimari ve ADR'ler (teknoloji, dosya formatı, gRPC yaklaşımı, sırlar, test, dil)
- [x] .NET 10 çözümü: merkezi paket yönetimi, lock dosyaları, analizörler, uyarılar hata
- [x] `JetApi.Core`: domain modeli, auth mirası, değişken çözümleme (öncelik, iç içe, döngü tespiti, dinamik değişkenler)
- [x] `JetApi.Storage`: Git dostu YAML workspace (okuma, yazma, tanılama, atomik yazma)
- [x] `JetApi.Http`: istek hazırlama ve gönderme, zamanlama ölçümü, net hata mesajları
- [x] `JetApi.Grpc`: çalışma anında proto, JSON↔protobuf codec (resmi kütüphaneyle çapraz doğrulanmış), unary çağrı
- [x] Avalonia masaüstü kabuğu
- [x] CI: Windows/macOS/Linux üzerinde derleme ve test, format ve güvenlik açığı kontrolü

## M1 — Masaüstü MVP → `v0.1.0` (büyük kısmı tamamlandı)

Amaç: Ekip günlük REST ve unary gRPC testlerini JetApi ile yapabilsin.

- [x] **Arayüz tasarımı** onaylandı ve uygulandı: [prototip ve kararlar](ui-design.md)
- [x] Workspace oluşturma/açma, son açılanlar, diskteki değişiklikleri izleme (`git pull` sonrası otomatik yenileme)
- [x] Koleksiyon ağacı: oluşturma, yeniden adlandırma, silme, çoğaltma, sürükle-bırak, filtre, sağ tık menüsü
- [x] REST istek editörü: method/URL, params, headers, auth, body (tüm türler), ayarlar
- [x] Yanıt görüntüleyici: pretty/raw/hex, header'lar, zamanlama, gönderilen istek, kopyala/dosyaya kaydet, büyük yanıtlarda sanallaştırılmış editör
- [x] gRPC editörü: proto ayarları ve hata listesi, servis/metot seçimi, şablon, canlı JSON doğrulama, metadata, yanıt/status/trailer görünümü
- [x] Environment editörü (environment, global ve koleksiyon kapsamları), çözümlenmiş değer, gizli değer maskeleme, tanımsız değişken uyarısı
- [x] Geçmiş (`.jetapi/`, Git dışı); geçmişten yeniden açma
- [x] Klavye kısayolları ve komut paleti; açık/koyu tema; Türkçe/İngilizce (yeniden başlatmadan)
- [x] Editör içinde değişken vurgulama ve üzerine gelince değer gösterme
- [x] Motor mesajlarının tamamen yerelleştirilmesi (HTTP, gRPC/proto/JSON ve workspace mesajları TR/EN)
- [x] Paketleme: Windows (zip), macOS (dmg), Linux (AppImage/tar.gz); etiketle taslak GitHub Release ([paketleme](packaging.md)). MSIX, imzalı paketlerle birlikte v1.0'da
- [x] Performans ölçümü: açılış süresi ve bellek (NFR-1, NFR-2) ([sonuçlar](performance.md))
- [x] Boşta belleği NFR-2 hedefinin altına indirmek: Windows'ta yazılımla çizim, 263 → 145 MB (macOS/Linux ölçülmedi)

## M2 — Postman'den geçiş → `v0.2.0`

- [x] Postman Collection v2.1 ve Environment import (import raporu: neyin taşındığı, neyin taşınamadığı) ([eşleştirme](import.md))
- [x] OpenAPI 3.x / Swagger 2.0 import ([eşleştirme](openapi-import.md))
- [ ] cURL import (yapıştır) ve export; kod parçası üretimi (C#, curl, JS, Python)
- [ ] OAuth 2.0 (client credentials, authorization code + PKCE, password), token önbelleği
- [ ] Cookie jar (isteğe bağlı)

## M3 — Test otomasyonu → `v0.3.0`

- [ ] Kodsuz assertion'lar ve yanıttan değişken çıkarma (ADR-0005)
- [ ] Koleksiyon/klasör çalıştırıcı (sıra, gecikme, tekrar, hata durumunda durma)
- [ ] CSV/JSON veri dosyasıyla veri odaklı çalıştırma
- [ ] `jetapi` CLI (dotnet tool + bağımsız ikili), JUnit XML ve HTML raporları, CI örnekleri (GitHub Actions, GitLab, Azure DevOps, Jenkins)

## M4 — İleri gRPC → `v0.4.0`

- [ ] Server reflection ile şema keşfi
- [ ] Server, client ve bidirectional streaming
- [ ] mTLS / istemci sertifikası (REST için de)
- [ ] `google.rpc.Status` hata detaylarının çözülmesi

## M5 — Güvenlik ve script → `v0.5.0`

- [ ] Sırlar faz 2: `*.local.yaml` ile kişisel değerler, işletim sistemi anahtarlığı (ADR-0004)
- [ ] JavaScript pre-request/test script'leri (Jint, `pm` API alt kümesi, sandbox)

## v1.0.0 — Kararlı sürüm

- [ ] Dosya formatı dondurulur (geriye uyumluluk garantisi)
- [ ] Performans hedefleri karşılanır ve CI'da ölçülür
- [ ] İmzalı kurulum paketleri; isteğe bağlı (opt-in) güncelleme kontrolü
- [ ] Kullanıcı dokümantasyonu (TR/EN)
