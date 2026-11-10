# Paketleme ve sürüm

JetApi kurulum gerektirmeyen, kendi .NET çalışma zamanını içeren (self-contained) tek dosyalık bir uygulama olarak dağıtılır.
Kod ReadyToRun ile önceden derlenir; bu açılış süresini yaklaşık %40 kısaltır (bkz. [performance.md](performance.md)).

## Paketler

| Platform | RID | Paket |
|---|---|---|
| Windows x64 / arm64 | `win-x64`, `win-arm64` | `JetApi-<sürüm>-<rid>.zip` |
| macOS Apple silicon / Intel | `osx-arm64`, `osx-x64` | `JetApi-<sürüm>-<rid>.dmg` (`JetApi.app`) |
| Linux x64 / arm64 | `linux-x64`, `linux-arm64` | `JetApi-<sürüm>-<rid>.AppImage` ve `.tar.gz` |
| Komut satırı (her platform) | yukarıdakiler | `jetapi-cli-<sürüm>-<rid>.zip` (Windows) ya da `.tar.gz`: tek dosya `jetapi` ([CLI](cli.md)) |

Paketler henüz imzalı değildir. Windows SmartScreen ve macOS Gatekeeper ilk açılışta uyarı verir. macOS'ta uygulamaya sağ
tıklayıp **Aç** demek yeterlidir. MSIX ve imzalı kurulum paketleri sertifika gerektirdiği için v1.0 hedefindedir
([yol haritası](roadmap.md)).

## Yerelde paket üretme

```bash
build/package.sh win-x64            # dist/JetApi-<sürüm>-win-x64.zip ve dist/jetapi-cli-<sürüm>-win-x64.zip
build/package.sh linux-x64 0.1.0    # sürüm verilmezse Directory.Build.props içindeki <Version> kullanılır
```

- Windows'ta Git Bash ile çalışır. Paketler `dist/`, ara çıktılar `artifacts/` klasörüne yazılır; ikisi de Git dışıdır.
- `.dmg` yalnızca macOS'ta (`hdiutil`, `codesign`), AppImage yalnızca Linux'ta üretilir. AppImage için `appimagetool` sabit
  bir sürümden indirilir; `desktop-file-validate` komutu (`desktop-file-utils` paketi) kurulu olmalıdır.
- Hedef RID'ler `src/Directory.Build.props` içinde listelenir. Bu sayede `dotnet publish -r <rid>` locked mode'da çalışır ve
  lock dosyalarını değiştirmez. Yeni bir platform eklenirse listeye eklenip `dotnet restore` çalıştırılır ve lock dosyaları
  commit'lenir.

## Sürüm çıkarma

1. `CHANGELOG.md` içindeki `[Unreleased]` bölümünü sürüm numarasıyla kapatın, `Directory.Build.props` içindeki `<Version>`
   değerini güncelleyin.
2. `vX.Y.Z` etiketini atıp gönderin: `git tag v0.1.0 && git push origin v0.1.0`.
3. [Release iş akışı](../.github/workflows/release.yml) altı paketi kendi işletim sisteminde üretir, `SHA256SUMS.txt`
   ekler ve **taslak** bir GitHub Release oluşturur. Sürüm açıklaması `CHANGELOG.md` içindeki o sürümün bölümüdür;
   uygulamanın güncelleme penceresi bu metni gösterir. Windows paketleri zorunludur; macOS ve Linux paketleri isteğe
   bağlıdır, biri başarısız olursa release onsuz oluşturulur. `-` içeren etiketler (`v0.1.0-beta.1`) ön sürüm olarak işaretlenir.
4. Taslağı kontrol edip yayınlayın.

## Uygulama içi güncelleme

Ayrıntılar ve gerekçe: [ADR-0011](adr/0011-in-app-updates.md).

- Uygulama yayınlanmış sürümleri `https://api.github.com/repos/mehmedozdemir/JetApi/releases` adresinden okur.
  **Taslaklar görünmez**: kullanıcılar bir sürümü ancak taslak yayınlandıktan sonra görür. Bunun için depo herkese açık
  olmalıdır.
- Denetim yalnızca kullanıcı isteyince yapılır (Ayarlar → Hakkında, komut paleti). Ayarlardaki *Açılışta güncellemeleri
  denetle* seçeneği varsayılan olarak kapalıdır.
- Windows paketi kendini günceller: `JetApi-<sürüm>-win-<arch>.zip` indirilir, `SHA256SUMS.txt` ile doğrulanır,
  `%LOCALAPPDATA%\JetApi\updates` klasörüne açılır ve yeniden başlatılınca kurulur. Değiştirilen dosyalar
  `*.jetapi-old` adını alır ve sonraki açılışta silinir. Kurulum yarıda kalırsa eski dosyalar geri konur.
- **Sözleşme:** Paket adları ve `SHA256SUMS.txt` (biçim: `sha256sum` çıktısı) eski sürümlerin güncellenebilmesi için
  değiştirilmemelidir.
- macOS ve Linux paketleri, kaynak koddan çalışan kopyalar ve klasörüne yazamayan kopyalar (ör. `Program Files`) yeni
  sürümü gösterir ve indirme sayfasını açar.
- Sürüm notları penceresi uygulamaya gömülü `CHANGELOG.md` kopyasını okur (`EmbeddedResource`), ağ gerekmez.

## Uygulama ikonu

Kaynak `build/icon.svg` dosyasıdır; glif arayüzdeki `Icon.Logo` ile aynıdır. `python build/make-icons.py` (Pillow gerekir)
`src/JetApi.Desktop/Assets/` altındaki `.ico`, `.icns` ve `.png` dosyalarını yeniden üretir.
