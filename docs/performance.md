# Performans ölçümü

Açılış süresi (NFR-1) ve boşta bellek kullanımı (NFR-2) paketlenmiş uygulamayla ölçülür:

```bash
build/package.sh win-x64
build/measure.sh win-x64 5      # 5 çalıştırma, medyan
```

`build/measure.sh` önce 500 istekli bir workspace üretir (`build/perf/generate-workspace.cs`: 5 koleksiyon × 10 klasör × 10
istek, iki environment). Ardından uygulamayı `JetApi --measure <workspace> <rapor.json>` ile başlatır. Bu modda uygulama
kullanıcı ayarlarına dokunmaz, verilen workspace'i açar ve şunları JSON olarak yazıp kapanır:

| Alan | Anlamı |
|---|---|
| `windowShownMs` | İşlemin başlamasından ana pencerenin açılmasına kadar geçen süre |
| `workspaceReadyMs` | Workspace yüklenip ilk karesi çizilene kadar geçen süre (**NFR-1**) |
| `idleWorkingSetMb` | 5 sn boşta bekledikten sonra working set (**NFR-2**) |
| `idlePrivateMb` | Aynı anda private bellek |
| `managedHeapMb` | Yönetilen heap (GC zorlanmadan) |

İlk çalıştırma soğuk disk önbelleğini, sonrakiler sıcak açılışı gösterir. Gerçek soğuk açılış için makine yeniden
başlatıldıktan sonra tek çalıştırma yapılır.

## Sonuçlar

### 2026-11-08, Windows 11 x64, `0.1.0-alpha.1`, ReadyToRun

| Ölçüm | Medyan (5 çalıştırma) | Hedef | Durum |
|---|---|---|---|
| Workspace hazır | 1.201 ms (ilk çalıştırma 2.312 ms) | < 1.500 ms | ✅ sıcak, ⚠️ ilk çalıştırma |
| Pencere açıldı | 569 ms | | |
| Boşta working set | 263 MB | < 200 MB | ❌ |
| Boşta private bellek | 190 MB | | |
| Yönetilen heap | 16 MB | | |

Gözlemler:

- Bellek workspace boyutundan bağımsızdır: 10 istekli workspace ile de working set 240–265 MB'tır. Yönetilen heap yalnızca
  6–16 MB'tır. Tüketimin büyük kısmı yerel taraftadır: Skia/GPU işleme yığını ve belleğe eşlenen tek dosyalık uygulama.
- ReadyToRun kapalıyken bellek aynı kalır, açılış 1,7–2,5 sn'ye çıkar. ReadyToRun bu yüzden açıktır.
- NFR-2 için sıradaki adımlar: Avalonia işleme modlarının (GPU/yazılım) ve yazı tipi önbelleğinin etkisini ölçmek,
  kullanılmayan TextMate gramerlerini yüklememek, tek dosya yerine klasör yayınını karşılaştırmak.
