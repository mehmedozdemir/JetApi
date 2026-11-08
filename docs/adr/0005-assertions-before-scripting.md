# ADR-0005: Test — önce kodsuz assertion'lar, sonra JavaScript

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-08

## Bağlam

Test uzmanları yanıtları doğrulamak, yanıttan değer çıkarıp sonraki isteklerde kullanmak ve koleksiyonları CI'da
çalıştırmak istiyor. Postman bunu JavaScript script'leriyle (`pm.test`) yapıyor. Script'ler güçlüdür ama hata yapmaya
açıktır, review'ı zordur ve bir JS motoru gerektirir.

## Karar

1. **M3 — Kodsuz assertion'lar:** Status kodu, header, JSONPath (`$.data.items[0].id`), yanıt süresi, body içerir/eşittir,
   JSON Schema; karşılaştırma operatörleri (`==`, `!=`, `>`, `<`, `contains`, `matches`, `exists`). Değişken çıkarma da
   aynı mekanizmayla yapılır: JSONPath/header → runtime veya environment değişkeni. Bunlar istek YAML dosyasında
   bildirimsel (declarative) olarak tutulur ve Git'te okunaklıdır.
2. **M3 — Çalıştırıcı ve CLI:** `jetapi run <koleksiyon> -e <env> [--data data.csv] [--reporter junit,html]`. Çıkış kodu
   CI için anlamlıdır (0 = hepsi geçti).
3. **M5 — JavaScript script'leri:** Saf .NET JS motoru [Jint](https://github.com/sebastienros/jint) ile, Postman `pm` API'sinin
   yaygın kullanılan alt kümesi. Amaç, Postman koleksiyonlarındaki mevcut testlerin taşınabilmesidir. Script'ler sandbox'ta
   çalışır: dosya sistemi ve ağ erişimi yoktur, zaman ve bellek sınırı vardır.

## Sonuçlar

- Çoğu doğrulama kod yazmadan, okunabilir biçimde yapılır.
- JS desteği gelene kadar Postman'den import edilen script'ler dosyada korunur ama çalıştırılmaz. Kullanıcı bu durumda uyarılır.
