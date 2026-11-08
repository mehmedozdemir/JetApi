# Katkı Rehberi

## Geliştirme ortamı

1. .NET SDK 10.0 kurun (`global.json` sürümü belirler).
2. `dotnet restore JetApi.slnx && dotnet build JetApi.slnx && dotnet test --solution JetApi.slnx`
3. Editör: `.editorconfig` kuralları otomatik uygulanır. Kaydederken formatlama açık olmalıdır.

## Branch stratejisi

- `main` her zaman çalışır durumdadır ve korumalıdır. Doğrudan commit atılmaz.
- Her iş ayrı bir branch'te yapılır: `feature/<kısa-ad>`, `fix/<kısa-ad>`, `docs/<kısa-ad>`, `refactor/<kısa-ad>`, `chore/<kısa-ad>`.
- PR'lar squash veya rebase ile birleştirilir. Birleştirilen branch silinir.

## Commit mesajları

[Conventional Commits](https://www.conventionalcommits.org/tr/) kullanılır:

```
<tip>(<kapsam>): <kısa açıklama, emir kipi, İngilizce, ≤72 karakter>

[gövde: ne ve neden]

[footer: BREAKING CHANGE: ..., Closes #123]
```

Tipler: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`.
Kapsamlar: `core`, `storage`, `http`, `grpc`, `desktop`, `cli`, `import`, `docs`.

## Pull request

- PR şablonundaki kontrol listesini doldurun.
- CI (format, analizörler, üç işletim sisteminde test, güvenlik açığı kontrolü) yeşil olmadan birleştirme yapılmaz.
- Yeni davranış testle gelir. Hata düzeltmeleri, hatayı yeniden üreten bir testle gelir.
- Kullanıcıya dönük değişiklikler `CHANGELOG.md` dosyasının `[Unreleased]` bölümüne eklenir.
- Dosya formatını değiştiren her şey [file-format.md](docs/file-format.md) dosyasını günceller. Kırıcı değişiklikler `schemaVersion` artışı ve geçiş notu gerektirir.
- Geri dönülmesi zor kararlar (yeni proje, yeni bağımlılık, format değişikliği) için `docs/adr/` altına bir ADR ekleyin.

## Kod standartları

- Uyarılar hata sayılır. Analizör kuralı bastırılacaksa `SuppressMessage` ile ve gerekçesiyle yapılır.
- Public API XML dokümantasyonu zorunludur.
- Yeni paketler `Directory.Packages.props` dosyasına sabit sürümle eklenir. Lock dosyaları commit'lenir.
- Kullanıcı girdisinden kaynaklanan hatalar exception değil, açıklayıcı mesajlarla sonuç nesnesi olarak döner.
- Dosyaya veya ağa yazılan sayı ve tarihler `CultureInfo.InvariantCulture` ile biçimlendirilir.
