# ADR-0002: Workspace formatı — istek başına bir YAML dosyası

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-08

## Bağlam

Takım paylaşımı Git üzerinden yapılacak. Postman'in tek dosyalık koleksiyon JSON'u diff, review ve merge için uygun değil.
Format; elle düzenlenebilir, çakışmaya dayanıklı ve ileriye dönük sürümlenebilir olmalı.

## Değerlendirilen seçenekler

| Seçenek | Artı | Eksi |
|---|---|---|
| Koleksiyon başına tek JSON (Postman gibi) | Basit | Her değişiklik aynı dosyada; çakışma ve okunmaz diff |
| **İstek başına YAML dosyası** | Küçük, okunabilir diff; çok satırlı body'ler `\|` bloğunda okunaklı | YAML'ın tuzakları (girinti, tip tahmini) |
| İstek başına JSON dosyası | Katı, tanıdık | Çok satırlı body'ler kaçış karakterleriyle okunmaz hâle gelir |
| Özel DSL (Bruno'nun `.bru` formatı gibi) | Çok okunaklı | Kendi parser'ımız ve araç desteği gerekir |
| SQLite | Hızlı | Git için uygun değil |

## Karar

İstek başına bir YAML dosyası; klasörler dizinlerle; sıralama üst öğenin `order` listesinde; her dosyada `schemaVersion`.
Ayrıntılar: [file-format.md](../file-format.md). Makineye özel durum (geçmiş, arayüz düzeni) `.jetapi/` altında tutulur
ve Git'e girmez.

YAML tuzaklarına karşı alınan önlemler: DTO'lar kesin tipli (her değer string olarak okunur, `1` ile `"1"` farkı yok),
bilinmeyen anahtarlar hata verir, alias'lar kapalıdır, çıktı her zaman uygulama tarafından aynı biçimde üretilir.

## Sonuçlar

- Git diff ve code review doğal olarak çalışır. Aynı anda farklı istekler üzerinde çalışan kişiler çakışmaz.
- Kaydetme yalnızca değişen dosyaları yazar (dosya zaman damgaları gereksiz yere değişmez).
- Silme ve yeniden adlandırma açık işlemlerdir. Okunamayan dosyalar yanlışlıkla silinmez.
- Formattaki kırıcı değişiklikler `schemaVersion` artışı ve otomatik geçiş gerektirir.
