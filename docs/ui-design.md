# Arayüz Tasarımı (M1)

Tıklanabilir prototip: https://claude.ai/artifact/N4vd7UoR9GwYreS2QypZfd (onaylandı, M1'de uygulandı)

Uygulamadaki karşılıkları `src/JetApi.Desktop/Views/` altındadır. Renkler ve ikonlar `Themes/Tokens.axaml`, ortak kontrol
stilleri `Themes/Styles.axaml` dosyasındadır. `tests/JetApi.Desktop.Tests/ScreenTests.cs` her ekranı gerçek Skia render'ı ile
çizer ve binding hatası olursa başarısız olur. `JETAPI_SCREENSHOTS` ortam değişkeni verilirse ekran görüntülerini o klasöre kaydeder.

## Ekranlar

1. **Karşılama:** Klasör aç, yeni workspace, Postman'den taşı, son açılanlar, TR/EN dil seçimi.
2. **REST istek editörü:** Sol panelde koleksiyon ağacı, üstte sekmeler, istek ve yanıt panelleri yan yana (dar pencerede alt alta).
3. **gRPC istek editörü:** Sunucu URL'si ve TLS/plaintext göstergesi, servis ve metot seçimi, proto durumu, mesaj editörü ve alan listesi.
4. **Environment'lar:** Kapsamlar, öncelik sırası, çözümlenmiş değer sütunu, gizli değer maskeleme, prod uyarısı.
5. **Hata ve uyarı durumları:** Tanımsız değişken, bağlantı reddi, zaman aşımı, gRPC status, JSON hatası, proto hatası, okunamayan dosyalar.
6. **Komut paleti (Ctrl+K)** ve temel kısayollar.

## Tasarım kararları

- **Makyaj turu (2026-11):** [Taslak tuvali](https://claude.ai/artifact/MVZeQN3L3JLKGfK3pskq7a). Vurgu rengi yalnızca ana eylemde, odakta ve aktif sekmede kullanılır; seçimler nötrdür (segmented control, koyu checkbox). Kutu çerçeveleri yerine zemin farkı, 1 px ayırıcılar, 5 basamaklı yazı ölçeği. Sekme rozetleri hap, yanıt metrikleri chip olarak gösterilir. Soluk metin #6B7180'dir (AA).
- **Kenar çubuğu:** Solda ikon şeridi (Koleksiyonlar, Environments, Geçmiş). Panel başlığı tek satırdır: başlık ve + butonu. Environments seçilince liste kenar çubuğunda, seçilen environment'ın detayı ana alanda açılır (Ctrl+E). Environment ve isteklerde sağ tık menüsü: Aktif yap / Aç, Yeniden adlandır (F2), Çoğalt (Ctrl+D), Sil (Delete, onaylı).
- **Koleksiyon ekranı:** Ağaçta koleksiyona tıklamak koleksiyonu açar ve ana alanda koleksiyon ekranını gösterir (daraltmak için ok). Bölümler: Değişkenler, Auth, gRPC proto, Genel. Koleksiyon değişkenleri burada düzenlenir; aynı ad aktif environment'ta da varsa satırda uyarı çıkar. Kaydet yalnızca değişiklik varken etkindir; istek açılınca ya da başka ekrana geçilince değişiklikler kaydedilir. Taslaktaki "koleksiyon sekmesi" yerine ana alanda açılan ekran seçildi; istek sekmeleri yalnızca istek taşır.
- **OAuth 2.0:** Auth sekmesinde türlerden biri. Alanlar grant türüne göre gösterilir (authorization code'da yetkilendirme URL'si, redirect URI ve PKCE; password'da kullanıcı adı ve parola). Altta "Erişim token'ı" kartı: durum (yok / şu saate kadar geçerli / süresi doldu), "Yeni erişim token'ı al" (tarayıcı beklenirken "İptal"), "Token'ı unut". Üstten miras alan istek ve klasörlerde de aynı kart, miras alınan ayarlarla görünür. Form genişliği içerikten bağımsız sabittir (en fazla 560 px).
- **Ayarlar:** Ayrı pencere (üst çubukta ayar ikonu, Ctrl+, veya komut paleti). Değişiklikler anında uygulanır ve kaydedilir; Kaydet butonu yoktur, "Varsayılanlara dön" vardır. Bölümler: Görünüm (tema, arayüz yazı tipi, arayüz boyutu %90–150, editör yazı tipi ve puntosu, yanıtlarda satır kaydırma, canlı önizleme), Dil, İstekler (varsayılan zaman aşımı, tanımsız değişken uyarısı), Geçmiş (kayıt açık/kapalı), Gelişmiş (çizim modu; yeniden başlatma gerekir). Ayarlar kullanıcıya özeldir (`%APPDATA%/JetApi/settings.json`), workspace'e girmez.
- **Görsel dil:** Nötr gri zemin, tek vurgu rengi (#3451D1). UI fontu Inter, kod ve değer fontu JetBrains Mono. Metot renkleri GET yeşil, POST turuncu, PUT mavi, DELETE kırmızı, gRPC camgöbeği; hepsi beyaz zeminde WCAG AA kontrastını sağlar.
- **Şeffaflık:** Otomatik eklenen header'lar Headers sekmesinde ayrı listelenir. Auth sekmesi isteğin auth'unun nereden miras alındığını gösterir. Gönderilen istek ağa çıkan haliyle görüntülenir.
- **Değişkenler:** URL altında, kullanılan değişkenin değeri ve kaynağı gösterilir. URL ve gRPC sunucu alanında, body ve gRPC mesaj editörlerinde `{{değişken}}` tanımlıysa yeşil, tanımsızsa kırmızı vurgulanır. URL alanı tek satırlık bir editördür: Enter gönderir, Tab sonraki alana geçer, yapıştırılan satır sonları boşluğa çevrilir. URL, tablo hücreleri, auth alanları ve editörlerde değişkenin üzerine gelince değeri (gizliyse maskeli) ve kaynağı gösterilir. Tanımsız değişken varsa istek gönderilmeden önce sorulur.
- **Hatalar:** Her hata ne olduğunu, nerede olduğunu (dosya:satır, JSON yolu) ve ne yapılacağını söyler.
- **Klavye:** Her işlem klavyeyle yapılabilir; komut paleti tüm istekleri, environment'ları ve komutları arar.
- **Dar pencere:** Kenar çubuğu ve paneller sarmalanır; sabit genişlik yoktur.
