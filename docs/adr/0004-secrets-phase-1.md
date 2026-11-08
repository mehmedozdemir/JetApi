# ADR-0004: Sırlar — 1. fazda environment dosyalarında

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-08

## Bağlam

Environment'lar token, şifre gibi sırlar içerir. İdeal çözüm bu değerleri Git'in dışında, işletim sistemi anahtarlığında
tutmaktır. Ancak bu çözüm ek karmaşıklık getirir ve ilk sürümleri geciktirir. Ürün sahibi, ilk aşamada sırların Git'e
girebileceğine karar verdi.

## Karar

**Faz 1 (M1):** Değişkenler, `secret: true` olanlar dahil, environment dosyalarına düz metin olarak yazılır. `secret`
bayrağı şimdilik yalnızca arayüzde maskeleme için kullanılır. Dokümantasyon ve arayüz, ekiplerin üretim (prod) sırlarını
bu dosyalara koymamasını açıkça önerir.

**Faz 2 (M5):**
1. `environments/<key>.local.yaml` dosyası (Git'e girmez, `.gitignore`'da) kişisel değerlerle ortak değerlerin üzerine yazar.
2. `secret: true` değişkenlerin değerleri işletim sistemi anahtarlığına taşınır (Windows Credential Manager, macOS
   Keychain, Linux libsecret). Dosyada yalnızca adları kalır.

## Sonuçlar

- M1 basit kalır ve ekip hemen kullanmaya başlayabilir.
- Modelde (`Variable.IsSecret`) gerekli bilgi şimdiden vardır. Faz 2'de dosya formatına geriye uyumlu bir alan eklenir.
- Risk: Faz 2'ye kadar Git geçmişine giren sırlar orada kalır. Bu nedenle prod sırları için uyarı gösterilir.
