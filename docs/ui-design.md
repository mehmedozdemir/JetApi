# Arayüz Tasarımı (M1)

Tıklanabilir prototip: https://claude.ai/artifact/N4vd7UoR9GwYreS2QypZfd (onay bekliyor)

## Ekranlar

1. **Karşılama:** Klasör aç, yeni workspace, Postman'den taşı, son açılanlar, TR/EN dil seçimi.
2. **REST istek editörü:** Sol panelde koleksiyon ağacı, üstte sekmeler, istek ve yanıt panelleri yan yana (dar pencerede alt alta).
3. **gRPC istek editörü:** Sunucu URL'si ve TLS/plaintext göstergesi, servis ve metot seçimi, proto durumu, mesaj editörü ve alan listesi.
4. **Environment'lar:** Kapsamlar, öncelik sırası, çözümlenmiş değer sütunu, gizli değer maskeleme, prod uyarısı.
5. **Hata ve uyarı durumları:** Tanımsız değişken, bağlantı reddi, zaman aşımı, gRPC status, JSON hatası, proto hatası, okunamayan dosyalar.
6. **Komut paleti (Ctrl+K)** ve temel kısayollar.

## Tasarım kararları

- **Görsel dil:** Nötr gri zemin, tek vurgu rengi (#3451D1). UI fontu Inter, kod ve değer fontu JetBrains Mono. Metot renkleri GET yeşil, POST turuncu, PUT mavi, DELETE kırmızı, gRPC camgöbeği; hepsi beyaz zeminde WCAG AA kontrastını sağlar.
- **Şeffaflık:** Otomatik eklenen header'lar Headers sekmesinde ayrı listelenir. Auth sekmesi isteğin auth'unun nereden miras alındığını gösterir. Gönderilen istek ağa çıkan haliyle görüntülenir.
- **Değişkenler:** URL altında, kullanılan değişkenin değeri ve kaynağı gösterilir. Tanımsız değişken varsa istek gönderilmeden önce sorulur.
- **Hatalar:** Her hata ne olduğunu, nerede olduğunu (dosya:satır, JSON yolu) ve ne yapılacağını söyler.
- **Klavye:** Her işlem klavyeyle yapılabilir; komut paleti tüm istekleri, environment'ları ve komutları arar.
- **Dar pencere:** Kenar çubuğu ve paneller sarmalanır; sabit genişlik yoktur.
