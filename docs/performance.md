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
| `gcCommittedMb` | GC'nin işletim sisteminden ayırdığı bellek (boş alan dahil) |

İlk çalıştırma soğuk disk önbelleğini, sonrakiler sıcak açılışı gösterir. Gerçek soğuk açılış için makine yeniden
başlatıldıktan sonra tek çalıştırma yapılır.

## Sonuçlar

### 2026-11-08, Windows 11 x64, `0.1.0-alpha.1`, ReadyToRun, yazılımla çizim (güncel varsayılan)

| Ölçüm | Medyan (5 çalıştırma) | Hedef | Durum |
|---|---|---|---|
| Workspace hazır | 1.331 ms (sıcak çalıştırmalar 981–1.331 ms) | < 1.500 ms | ✅ |
| Pencere açıldı | 583 ms | | |
| Boşta working set | 145 MB | < 200 MB | ✅ |
| Boşta private bellek | 72 MB | | |

### Bellek deneyleri (aynı makine, 3 çalıştırmanın medyanı)

| Deney | Açılış | Working set | Private | GC'nin ayırdığı |
|---|---|---|---|---|
| GPU ile çizim (ANGLE/Direct3D, eski varsayılan) | 1.206 ms | 265 MB | 191 MB | 19 MB |
| **Yazılımla çizim (Skia CPU)** | 1.241 ms | **156 MB** | **77 MB** | 19 MB |
| GPU + eşzamanlı GC kapalı (`DOTNET_gcConcurrent=0`) | 1.514 ms | 291 MB | 209 MB | 12 MB |
| GPU + `DOTNET_GCConserveMemory=5` | 1.384 ms | 266 MB | 192 MB | 19 MB |

**Karar:** Windows'ta varsayılan çizim yazılımla yapılır. GPU yolu açılışı hızlandırmadan yaklaşık 110 MB bellek harcıyor.
Bedeli, çok büyük ekranlarda (4K) kaydırma ve animasyonların CPU'ya binmesidir. JetApi form ağırlıklıdır ve büyük
yanıtlar sanallaştırılmış editörde gösterilir. `JETAPI_RENDERING=gpu` ortam değişkeni GPU'ya döner. GC ayarları
belleği düşürmedi; varsayılanlar kalır. macOS ve Linux'ta henüz ölçüm yapılmadı, oradaki varsayılanlara dokunulmadı.

### 2026-11-08, Windows 11 x64, `0.1.0-alpha.1`, ReadyToRun, GPU ile çizim (ilk ölçüm)

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
- Bu ölçümden sonra çizim modu deneyleri yapıldı (yukarıda).
