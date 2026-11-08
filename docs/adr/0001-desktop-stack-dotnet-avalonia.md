# ADR-0001: Masaüstü teknolojisi — .NET 10 + Avalonia

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-08

## Bağlam

Windows, macOS ve Linux'ta çalışan, hızlı ve düşük bellekli bir masaüstü uygulaması gerekiyor. Ekibin ana dili C#.
Uygulamanın kalbi bir HTTP/gRPC istemcisi; aynı motorun CI'da çalışacak bir CLI'da da kullanılması gerekiyor.

## Değerlendirilen seçenekler

| Seçenek | Artı | Eksi |
|---|---|---|
| **.NET + Avalonia** | Ekip C# biliyor; tek dil; native render (Skia), düşük bellek; olgun `HttpClient` ve `Grpc.Net.Client`; aynı kod CLI'da kullanılabilir | Web'e göre bileşen ekosistemi daha dar (kod editörü gibi bileşenler için AvaloniaEdit vb. gerekir) |
| .NET MAUI | Microsoft desteği | Linux desteği yok, masaüstü deneyimi zayıf |
| Tauri (Rust + Web) | Küçük kurulum, web ekosistemi | Ekip Rust bilmiyor; iki dil ve iki çalışma ortamı |
| Electron (TS) | En geniş ekosistem | Postman'in hantallık sorununu tekrar etme riski; ekip dili değil |
| .NET + Photino/Blazor Hybrid | C# + web bileşenleri | Platform WebView farkları, daha az olgun |

## Karar

**.NET 10 (LTS) + Avalonia 12 + CommunityToolkit.Mvvm.** İş mantığı arayüzden bağımsız kütüphanelerde yazılır.

## Sonuçlar

- Ekip mevcut bilgisiyle verimli çalışır. Motor kütüphaneleri hem masaüstü uygulamasında hem CLI'da kullanılır.
- Kod/JSON editörü için AvaloniaEdit (TextMate gramerleri) kullanılacak. Büyük yanıtlar için sanallaştırma gerekli.
- Dağıtım: self-contained, trimmed paketler (Windows MSIX/zip, macOS dmg, Linux AppImage/tar). Native AOT, Avalonia
  uyumluluğu doğrulandıktan sonra değerlendirilecek.
