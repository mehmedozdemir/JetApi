# Mimari Karar Kayıtları (ADR)

Geri dönülmesi zor her karar burada kayıt altına alınır. Yeni bir karar için en son numarayı bir artırın ve
mevcut kayıtlardaki başlık yapısını kullanın. Kabul edilmiş bir ADR değiştirilmez: yerine yenisi yazılır ve eskisinin
durumu "Yerini aldı: ADR-XXXX" yapılır.

| No | Karar | Durum |
|---|---|---|
| [0001](0001-desktop-stack-dotnet-avalonia.md) | Masaüstü teknolojisi: .NET 10 + Avalonia | Kabul edildi |
| [0002](0002-git-friendly-yaml-workspace.md) | Workspace formatı: istek başına bir YAML dosyası | Kabul edildi |
| [0003](0003-dynamic-grpc-without-codegen.md) | gRPC: kod üretimi olmadan, çalışma anında proto | Kabul edildi |
| [0004](0004-secrets-phase-1.md) | Sırlar: 1. fazda environment dosyalarında | Kabul edildi |
| [0005](0005-assertions-before-scripting.md) | Test: önce kodsuz assertion'lar, sonra JavaScript | Kabul edildi |
| [0006](0006-localization.md) | Türkçe ve İngilizce arayüz | Kabul edildi |
