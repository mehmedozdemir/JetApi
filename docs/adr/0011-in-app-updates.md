# ADR-0011: Uygulama içi güncelleme: GitHub Releases'tan, isteğe bağlı denetim, doğrulanmış kurulum

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-10
- **İlgili:** [packaging.md](../packaging.md), [requirements.md](../requirements.md) (APP-1…3, NFR-6)

## Bağlam

JetApi kurulum gerektirmeyen bir zip paketi olarak dağıtılıyor. Kullanıcılar yeni sürümden ancak birisi haber verirse
haberdar oluyor. Güncellemek için zip'i indirip açmaları ve dosyaları elle değiştirmeleri gerekiyor. Hangi sürümü
kullandıklarını ve sürümde neyin değiştiğini de uygulamada göremiyorlar.

Kısıtlar:

- **NFR-6:** Kullanıcı açıkça istemeden ağ isteği yapılmaz, telemetri yoktur.
- **Doğruluk önce gelir:** Yarım kalan bir güncelleme uygulamayı açılamaz hale getirmemeli. İndirilen dosya
  doğrulanmadan çalıştırılmamalı.
- **Sertifika yok:** Paketler imzasız (MSIX ve imzalı kurulum v1.0 hedefinde).
- **Platform:** Yalnızca Windows paketleri zorunlu.

## Karar

- **Kaynak: GitHub Releases.** Uygulama `api.github.com/repos/mehmedozdemir/JetApi/releases` adresini oturum açmadan
  okur. Bunun için depo herkese açıktır. Taslak sürümler görünmez. Ön sürümler yalnızca zaten ön sürüm kullananlara
  önerilir. İstek yalnızca `JetApi/<sürüm>` user agent'ını taşır, başka veri gönderilmez.
- **Denetim isteğe bağlıdır.** Kullanıcı Ayarlar → Hakkında'da ya da komut paletinde *Güncellemeleri denetle* der. Bunun
  dışında *Açılışta güncellemeleri denetle* seçeneği vardır. Bu seçenek **varsayılan olarak kapalıdır** (NFR-6) ve
  açıksa denetim, açılıştan 5 saniye sonra yapılır (NFR-1). Açılıştaki denetim yeni sürüm bulursa yalnızca üst çubukta
  bir düğme belirir. Pencere açılmaz.
- **Sürüm notları:** Uygulama kendi `CHANGELOG.md` kopyasını kaynak olarak taşır. *Sürüm notları* penceresi ağsız
  çalışır. Release iş akışı, GitHub sürümünün açıklamasına CHANGELOG'daki ilgili bölümü yazar. Güncelleme penceresi yeni
  sürümlerin notlarını oradan gösterir.
- **Doğrulama:** Paket `JetApi-<sürüm>-<rid>.zip`, sürümdeki `SHA256SUMS.txt` ile karşılaştırılır. Listede kaydı
  olmayan ya da özeti tutmayan paket kurulmaz. Bu dosya adları ve biçimi artık uygulamayla yapılan bir sözleşmedir.
  Değiştirilirse eski sürümler güncellenemez.
- **Kurulum (Windows paketi):**
  1. Paket `%LOCALAPPDATA%\JetApi\updates\<sürüm>` klasörüne indirilir ve açılır.
  2. Kullanıcı *Yeniden başlat ve kur* der. Önce kaydedilmemiş istekler sorulur.
  3. Windows'ta çalışan bir exe'nin üzerine yazılamaz ama adı değiştirilebilir. Bu yüzden her dosyanın adı
     `*.jetapi-old` yapılır ve yenisi kopyalanır. Bir adım başarısız olursa o ana kadar değiştirilen dosyalar geri
     konur ve uygulama eski sürümde çalışmaya devam eder.
  4. Yeni sürüm `--updated-from <eski sürüm>` ile başlatılır ve eskisi kapanır.
  5. Yeni sürüm eski dosyaları ve indirme klasörünü siler, sonra "neler değişti" penceresini gösterir.
- **Kendini güncellemeyen kopyalar:** macOS ve Linux paketleri, kaynak koddan çalışan kopyalar (yanında `JetApi.dll`
  olanlar) ve klasörüne yazamayan kopyalar (ör. `Program Files`) güncellemeyi gösterir ama kurmaz. Bunun nedenini
  yazar ve *İndirme sayfasını aç* düğmesini sunar.

## Değerlendirilen seçenekler

- **Ayarlanabilir güncelleme adresi (iç ağ sunucusu, `latest.json`):** Gizli depoyla çalışırdı ama her sürümde ayrı bir
  yayın adımı ve bakımı gereken bir sunucu getirirdi. Depo açık kaynak ve MIT lisanslı olduğu için GitHub yeterli.
  Gerekirse adres ileride ayara çevrilebilir: `GitHubReleases` bir arayüzün (`IUpdateSource`) arkasında.
- **Ayarlarda GitHub token'ı (depo gizli kalır):** Her kullanıcının token üretip saklaması gerekirdi. Token'ın güvenli
  saklanması (SEC-3) henüz yok.
- **Hazır güncelleme kütüphanesi (Velopack, Squirrel):** Kurulum biçimini değiştirir (kurulum programı, delta paketler,
  kendi dosya düzeni). Zip paketiyle ve "kurulumsuz" ilkesiyle uyuşmaz. Ayrıca bir bağımlılık ve açılışa ek yük
  getirir. İmzalı MSIX'e geçildiğinde (v1.0) yeniden değerlendirilecek.
- **Ayrı bir güncelleyici süreç ya da PowerShell betiği:** Dosyaları uygulama kapandıktan sonra değiştirir. Yeniden
  adlandırma yöntemi buna gerek bırakmıyor, test edilebilir ve tek bir süreçte çalışıyor.

## Sonuçlar

- Sürüm çıkarırken `CHANGELOG.md` bölümü düzgün yazılmalıdır: hem uygulamadaki *Sürüm notları* hem de GitHub sürüm
  açıklaması oradan gelir.
- Paket adları (`JetApi-<sürüm>-<rid>.zip`) ve `SHA256SUMS.txt` değiştirilemez. Değişirse bu ADR'nin yerine yenisi
  yazılır ve eski sürümler için geçiş planlanır.
- Paketler imzasız olduğu için doğrulama, indirmenin GitHub'daki dosyayla aynı olduğunu garanti eder. GitHub hesabının
  ele geçirilmesine karşı koruma sağlamaz. İmzalama v1.0 hedefinde.
- GitHub'ın oturumsuz API sınırı (IP başına saatte 60 istek) elle ve açılışta yapılan denetim için yeterlidir. Sınır
  aşılırsa kullanıcıya açıklayıcı bir mesaj gösterilir.
