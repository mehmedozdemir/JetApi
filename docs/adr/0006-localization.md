# ADR-0006: Türkçe ve İngilizce arayüz

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-08

## Karar

- Arayüz metinleri `.resx` kaynak dosyalarındadır: `Strings.resx` (İngilizce, varsayılan ve geri dönüş dili) ve
  `Strings.tr.resx` (Türkçe).
- İlk açılışta işletim sistemi dili kullanılır. Kullanıcı dili ayarlardan değiştirebilir (yeniden başlatma gerekmez).
- Motor kütüphanelerinin kullanıcıya dönük mesajları (hata açıklamaları) da kaynak dosyalarına taşınır. Teknik loglar
  İngilizce kalır.
- Kod, yorumlar, commit mesajları ve dosya formatı anahtarları İngilizcedir. Proje dokümantasyonu Türkçedir.
- Sayı ve tarih biçimlendirmesi arayüz dilini izler. Dosyalara ve ağa yazılan değerler her zaman `InvariantCulture` ile
  biçimlendirilir.

## Sonuçlar

- Her yeni arayüz metni iki dilde eklenir. CI, eksik çeviri anahtarlarını denetler (M1).
