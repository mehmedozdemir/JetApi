# Changelog

Bu projedeki tüm önemli değişiklikler bu dosyada belgelenir.
Biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/) standardını, sürümleme [Semantic Versioning](https://semver.org/lang/tr/) kurallarını izler.

## [Unreleased]

### Changed

- gRPC mesaj editörü artık tüm genişliği kullanır; yanındaki *Alanlar* paneli kaldırıldı. Alan listesi araç çubuğundaki
  *Alanlar (n)* düğmesiyle açılır pencerede görünür.

### Added

- gRPC mesaj editöründe alan önerisi: alan adı yazılacak yerde `"` ya da harf yazınca veya Ctrl+Space ile, imlecin
  bulunduğu mesajın (iç içe mesajlar, repeated mesaj elemanları ve map değerleri dahil) henüz yazılmamış alanları
  türleriyle listelenir. Yazdıkça süzülür, Enter/Tab alanı türüne uygun bir değerle ekler ve değeri seçer, eksik virgülü
  tamamlar. Oneof'un bir üyesi yazılmışsa diğerleri önerilmez.

## [0.3.1] - 2026-11-10

Uygulama içi güncelleme sürümü: Hakkında, sürüm notları, güncelleme denetimi ve Windows paketinde tek tıkla kurulum. Ayrıca renk paletleri ve koleksiyon ağacında sürükle-bırak. Bu sürüm bir kez elle kurulur; sonraki sürümler uygulamanın içinden gelir.

### Added

- Renk paletleri: Ayarlar → Görünüm'de açık ve koyu tema için ayrı ayrı seçilir; yalnızca kullanılan temanın
  paletleri listelenir. Açık: JetApi, One Light, Solarized Light,
  Quiet Light, Yüksek kontrast. Koyu: JetApi, One Dark, Nord, Dracula, Monokai, Solarized Dark, Yüksek kontrast. Seçim
  yeniden başlatmadan uygulanır; kod editörünün sözdizimi renkleri de palete uyar. *Sistemle aynı* tema seçiliyken sistem
  açık/koyu değiştikçe seçilen iki paletten uygun olanı kullanılır. Tüm paletlerde metin kontrastı WCAG AA'yı karşılar
  (testle denetlenir).
- Koleksiyon ağacında sürükle-bırak tamamlandı: satırın üst/alt kısmına bırakmak öne/arkaya, klasörün ortasına bırakmak
  içine koyar; nereye düşeceği çizgi ya da çerçeveyle gösterilir. Klasör ve istekler başka koleksiyona taşınabilir,
  koleksiyonlar kendi aralarında sıralanabilir. Kapalı klasörün üzerinde beklenince açılır, kenarlara yaklaşınca liste
  kayar. Klavyeyle Alt+↑ / Alt+↓ (ve sağ tık → *Yukarı taşı* / *Aşağı taşı*) aynı düzeyde bir sıra taşır.
- Hakkında, sürüm notları ve güncellemeler ([ADR-0011](docs/adr/0011-in-app-updates.md)):
  - **Hakkında:** Ayarlar → *Hakkında* bölümü sürümü ve commit'i, çalışma ortamını ve işletim sistemini gösterir.
    *Hata bildirimi için bilgileri kopyala* düğmesi bu bilgileri panoya alır. Sürüm notları, GitHub sayfası, sorun
    bildirme ve lisans bağlantıları da buradadır.
  - **Sürüm notları:** Her sürümde neyin değiştiği uygulamanın kendi CHANGELOG kopyasından, ağ olmadan gösterilir.
    Komut paletinden ve karşılama ekranının alt satırından açılır.
  - **Güncelleme denetimi:** Ayarlar → Hakkında'dan ya da komut paletinden *Güncellemeleri denetle*. İstenirse açılışta
    da yapılır (*Açılışta güncellemeleri denetle*, varsayılan kapalı). Yeni sürüm bulunduğunda üst çubukta bir düğme
    belirir. Güncelleme penceresi yeni sürümlerin notlarını gösterir.
  - **Kurulum (Windows paketi):** Paket indirilir, `SHA256SUMS.txt` ile doğrulanır ve JetApi yeniden başlatılarak
    kurulur. Önce kaydedilmemiş istekler sorulur. Bir dosya değiştirilemezse eski sürüm olduğu gibi kalır. Güncellemeden
    sonraki ilk açılışta yalnızca yeni sürümlerin notları gösterilir. macOS ve Linux paketleri ile kaynak koddan
    çalışan kopyalar indirme sayfasını açar.
  - GitHub sürüm açıklaması artık CHANGELOG'daki ilgili bölümdür.

### Fixed

- Komut paleti İngilizce arayüzde Türkçe Windows'ta büyük `I` içeren komutları bulamıyordu (ör. "install" yazınca
  "Install…"). Artık arama arayüz dilinin harf kurallarını kullanır.
- Kaydedilmemiş değişikliği olan bir istek (ya da içinde bulunduğu klasör) taşınınca sekmesi eski yolda kalıyordu;
  kaydedince isteğin bir kopyası koleksiyon köküne ekleniyordu. Artık sekmeler taşınan istekleri değişiklikleriyle
  birlikte izler.
- Bir klasör, adı onunla başlayan kardeş klasörün içine sürüklenemiyordu (ör. `api` → `api-v2`).
- Sekmeyi farenin orta tuşuyla kapatmak yalnızca sekme yazısının üstünde çalışıyordu; artık sekmenin her yerinde çalışır.

## [0.3.0] - 2026-11-09

Test otomasyonu sürümü (M3): kodsuz testler ve değişken çıkarma, koleksiyon çalıştırıcı, `jetapi` komut satırı aracı ve CI raporları, Postman test script'lerinin içe aktarılması.

### Added

- Postman test script'leri içe aktarılırken yaygın ifadeler (`pm.response.to.have.status`, `pm.expect(json.x).to.eql(…)`, yanıt süresi, header, body, `pm.environment.set(…)` ve benzerleri) testlere ve çıkarma kurallarına çevrilir. Çevrilemeyen script'ler, pre-request script'leri ve klasör/koleksiyon script'leri dosyada yeni `scripts` bölümünde aynen saklanır ama çalışmaz; rapor, isteğin *Testler* sekmesi ve `jetapi` bunu söyler (ADR-0010, [import.md](docs/import.md)).
- `jetapi` komut satırı aracı: `jetapi run <workspace> [koleksiyon/klasör] -e <environment>` koleksiyonu uygulamayla aynı kurallarla çalıştırır; veri dosyası, tekrar, bekleme, `--bail`, `--var`, `--persist`, `--lang`. Çıkış kodları: 0 geçti, 1 kaldı, 2 yanlış girdi. `--junit` ve `--html` ile rapor. Her sürümde platform başına tek dosyalık arşiv (`jetapi-cli-<sürüm>-<rid>`). GitHub Actions, GitLab, Azure DevOps ve Jenkins örnekleri: [docs/cli.md](docs/cli.md).
- Çalıştırıcıda *Rapor* düğmesi: biten koşu HTML ya da JUnit XML olarak kaydedilir.
- Koleksiyon çalıştırıcı: ağaçta koleksiyon ya da klasöre sağ tık → *Çalıştır…* (ya da komut paleti). İstek seçimi, tekrar, istekler arası bekleme, ilk hatada durma, CSV/JSON veri dosyasıyla her satır için bir tekrar; sonuçlar canlı ve tekrar başına gruplu. Çalıştırma mantığı uygulama ve CLI için ortak `JetApi.Runner` projesinde (ADR-0009).
- Testler: isteğin *Testler* sekmesinde kodsuz kontroller (status, header, JSON alanı, body, süre, JSON Schema) ve değişken çıkarma kuralları; her gönderimde çalışır, sonuçlar satırların yanında ve yanıttaki *Test sonuçları* sekmesinde. JSON yanıtta bir değere sağ tıklayarak test ya da çıkarma kuralı eklenir. Çıkarılan değerler çalışma anı değişkeni (dosyaya yazılmaz, komut paletinden temizlenir), environment, koleksiyon veya global değişken olur. HTTP ve gRPC isteklerinde.
- Yanıt kontrolleri ve değişken çıkarma için motor (`JetApi.Testing`) ve dosya biçimi: istek dosyalarında `tests`
  (status, header, JSONPath, body, süre, JSON Schema; `equals`, `contains`, `matches`, `exists`…) ve `extract` (JSONPath,
  header, status, body → çalışma anı, environment, koleksiyon veya global değişken). JSONPath RFC 9535'e göre kendi
  uygulamamızdır; JSON Schema için BSD-3-Clause lisanslı LateApexEarlySpeed.Json.Schema (ADR-0008). Arayüz sonraki adımda.
- CI artık her dalda çalışır (önceden yalnızca olmayan `main` dalında).

### Fixed

- Environment olarak "Yok" seçimi hatırlanmıyordu; workspace yeniden açılınca ilk environment seçili geliyordu.
- Ölçüm modu (`--measure`) ayarlarda GPU çizimi seçiliyse onunla ölçüyordu; artık her zaman yazılım çizimiyle ölçer.

## [0.2.0] - 2026-11-09

Postman'den geçiş sürümü (M2). İlk ön sürüm `0.1.0-alpha.1` (2026-11-08) ayrı bir bölüm olarak yazılmadığı için
o sürümdeki M0 ve M1 değişiklikleri de burada listelenir.

### Added

- OpenAPI 3.0/3.1 ve Swagger 2.0 içe aktarma (JSON ve YAML): tag’lere göre klasörler, `baseUrl` ve parametre değişkenleri, şemadan örnek body, güvenlik şemalarından auth. Postman ile aynı içe aktarma komutu ve raporu.
- Ayarlar penceresi (Ctrl+,): tema, arayüz yazı tipi ve boyutu, editör yazı tipi ve puntosu, yanıtlarda satır kaydırma, dil, varsayılan zaman aşımı, tanımsız değişken uyarısı, geçmiş kaydı ve çizim modu. Değişiklikler anında uygulanır.
- OAuth 2.0 motoru: client credentials, authorization code (PKCE S256, tarayıcı ve loopback adresiyle oturum açma) ve password grant'leri; token yenileme; client kimlik bilgilerini Basic header'da veya body'de gönderme; sunucu hataları için açıklayıcı TR/EN mesajlar. Yapılandırma YAML'da (`auth.type: oauth2`), token'lar Git dışında `.jetapi/tokens.json` önbelleğinde, environment'lar arasında karışmadan saklanır (ADR-0007). REST ve gRPC isteklerinde `Authorization` olarak gönderilir. Auth sekmesinde (istek, klasör, koleksiyon) OAuth 2.0 seçilir; client credentials ve password'da token gönderirken kendiliğinden alınır ve yenilenir, authorization code'da "Yeni erişim token'ı al" tarayıcıda oturum açtırır. Postman ve OpenAPI'deki OAuth 2.0 ayarları da içe aktarılır.
- cURL: URL alanına yapıştırılan cURL komutu (bash, Windows cmd, PowerShell sözdizimi; Chrome/Firefox "Copy as cURL" dahil) isteği kurar; komut paletinden panodaki cURL ile yeni istek; *cURL olarak kopyala*. Taşınamayanlar içe aktarma raporunda.
- Kod örneği: istek satırındaki `</>` düğmesi isteği cURL (bash, cmd), PowerShell, C# (HttpClient), JavaScript (fetch) ve Python (requests) olarak gösterir ve kopyalar.
- Değişken kartı: istek editörlerinde bir `{{değişken}}`in üzerine gelince değeri ve kaynağı düzenlenebilir bir kartta açılır; Kaydet değeri tanımlı olduğu environment, koleksiyon veya global değişkenlere yazar. Tanımsız değişken buradan istenen yere eklenir.
- *Değişken olarak ayarla…*: editörlerde ve tablo hücrelerinde seçili metin sağ tıkla environment, koleksiyon veya global değişkene çevrilir ve yerine `{{ad}}` yazılır. Editörlerin sağ tık menüsünde kes/kopyala/yapıştır/tümünü seç de var.
- Yanıt paneli isteğin altına alınabilir: durum çubuğundaki düğme, Ctrl+Alt+V, komut paleti veya Ayarlar → Görünüm. Seçim ve ayırıcının konumu hatırlanır.
- Workspace değiştirme: üst çubuktaki workspace menüsü yalnızca kayıtlı workspace'leri listeler ve tıklananı aynı pencerede açar; "Başka bir klasör aç…" ile yenisi eklenir. Komut paletinden de geçilir. Her workspace'in açık sekmeleri hatırlanır.
- Arayüz puntosu ayarı (Ayarlar → Görünüm): yazılar 11–18 punto arasında ayarlanır, başlıklar ve küçük yazılar birlikte değişir.
- Yanıttan değişkene: yanıt editöründe seçilen değer sağ tık → *Değişkene kaydet…* ile yeni veya mevcut bir değişkene yazılır.
- `{{` otomatik tamamlama: istek alanlarında `{{` yazınca görünen değişkenler (dinamikler dahil) değer ve kaynaklarıyla listelenir; Enter/Tab adı ve `}}`'yi ekler.
- Proto dosyası seçme: koleksiyonun gRPC proto bölümünde *Dosya ekle…* ve *Klasör ekle…*; proto'su olmayan gRPC isteğinde *Proto dosyası seç…* dosyayı koleksiyona ekler ve hemen yükler. Import path'ler gerekirse kendiliğinden eklenir; workspace dışındaki dosyalar için uyarı gösterilir.
- Cookie jar (isteğe bağlı, Ayarlar → İstekler): sunucuların koyduğu cookie’ler aynı siteye giden sonraki isteklerle gönderilir; workspace başına bellekte tutulur, diske yazılmaz; komut paletinden temizlenir.
- Postman’den içe aktarma: Collection v2.0/v2.1 ve environment dosyaları (birden fazla dosya birlikte). Kenar çubuğundaki + menüsünden, komut paletinden veya karşılama ekranından. İçe aktarma raporu, neyin farklı çevrildiğini ve neyin aktarılamadığını (script’ler, örnek yanıtlar, kontrol edilecek dosya yolları) gösterir.
- Ürün vizyonu, gereksinimler, mimari, dosya formatı, yol haritası ve ADR'ler.
- `JetApi.Core`: workspace/koleksiyon/klasör/istek/environment modeli, auth mirası, katmanlı `{{değişken}}` çözümleme (iç içe değişkenler, döngü tespiti, dinamik değişkenler).
- `JetApi.Storage`: Git dostu YAML workspace formatı (schemaVersion 1); atomik yazma, değişmeyen dosyaya dokunmama, hatalı dosyalar için tanılama.
- `JetApi.Http`: REST isteği hazırlama ve gönderme; tüm body türleri, auth, yönlendirme/TLS/zaman aşımı ayarları, DNS/bağlantı/TTFB/indirme ölçümü, gönderilen isteğin birebir raporu.
- `JetApi.Grpc`: `.proto` dosyalarını çalışma anında yükleme, proto3 JSON eşlemesine uygun JSON↔protobuf codec (well-known type'lar dahil), örnek mesaj şablonu ve unary çağrı.
- Masaüstü uygulaması (onaylı tasarım): karşılama ekranı, koleksiyon ağacı (sağ tık menüsü, sürükle-bırak, filtre), sekmeler, REST ve gRPC editörleri, sözdizimi vurgulama ve arama, yanıt görüntüleyici (pretty/raw/hex, header'lar, zamanlama, gönderilen istek), environment editörü, koleksiyon/klasör ayarları, komut paleti (Ctrl+K), klavye kısayolları, açık/koyu tema, Türkçe/İngilizce arayüz.
- Workspace oturumu: her değişiklik anında diske yazılır; aktif environment makineye özel saklanır; diskteki değişiklikler (`git pull`) otomatik yüklenir.
- İstek geçmişi (`.jetapi/history.jsonl`, Git dışı).
- Gönderimden önce tanımsız değişken uyarısı.
- Motor mesajlarının Türkçe/İngilizce sunulması: istek hazırlama, bağlantı, gRPC, proto/JSON ve workspace dosyası mesajları arayüz dilini izler.
- Paketleme: Windows zip, macOS dmg, Linux AppImage ve tar.gz (`build/package.sh`); etiketle taslak GitHub Release üreten iş akışı; uygulama ikonu.
- Performans ölçümü: `JetApi --measure` modu ve `build/measure.sh` ile açılış süresi ve boşta bellek raporu.
- Editörlerde değişken vurgulama (tanımlı/tanımsız ayrı renk) ve üzerine gelince çözümlenmiş değer ile kaynağı gösterme.
- HTTP bağlantı hataları için hata türü (`HttpErrorKind`); arayüz bunları seçili dilde açıklar.
- Windows, macOS ve Linux üzerinde CI.

### Changed

- Adres altındaki değişken etiketleri kaldırıldı; yerine isteğin gerçekte gideceği adres (değişkenler çözülmüş, gizliler maskeli, sorgu parametreleri eklenmiş) gösterilir. Adreste değişken yoksa satır görünmez.
- Arayüz sıkılaştırıldı: yazılar bir kademe küçük (temel 13 pt), düğme ve alanlar daha alçak, ağaçta girinti yarıya indi. Yazı boyutları artık `Tokens.axaml`da tek yerde.
- İkonlar: sağ tık menüleri, komut paleti, Ayarlar bölümleri, Gönder/Kaydet düğmeleri, Biçimlendir ve proto bağlantıları ile bilgi/uyarı notları ikon aldı.
- Arayüz makyajı: daha az çerçeve, vurgu rengi yalnızca ana eylemde; body türü ve görünüm seçimi segmented control; sekme rozetleri; yanıt süresi ve boyutu chip olarak; istek adı başlık olarak; kopyala ve kaydet ikon butonlar; sade karşılama ekranı; durum çubuğunda kısa yol ve Git dalı. Soluk metnin kontrastı AA seviyesine çıkarıldı. Yanıttaki IPv4 adresleri `[::ffff:…]` biçimi yerine sade gösterilir.
- Kenar çubuğu ikon şeridiyle Koleksiyonlar, Environments ve Geçmiş arasında geçer; environment listesi kenar çubuğunda, detayı ana alanda açılır. Environment'larda sağ tık menüsü (aktif yap, yeniden adlandır, çoğalt, sil) ve F2 / Ctrl+D / Delete kısayolları; isteklerin menüsüne "Aç" ve Ctrl+D eklendi.
- Koleksiyon ekranı: koleksiyona tıklayınca açılır; koleksiyon değişkenleri, auth, proto dosyaları ve ad sekmelerde düzenlenir. Koleksiyon değişkenleri environment listesinden buraya taşındı. Ekrandan çıkarken değişiklikler kaydedilir.
- URL ve gRPC sunucu alanında değişkenler alanın içinde vurgulanır; URL alanında Enter isteği gönderir.
- Windows'ta arayüz varsayılan olarak yazılımla çizilir; boşta bellek 263 MB'tan 145 MB'a iner. `JETAPI_RENDERING=gpu` ile GPU kullanılır.

### Fixed

- gRPC isteğinde "Proto yüklendi" satırının özeti dar pencerede bağlantıların üzerine biniyordu; artık kısalır, tamamı ipucunda.
- Workspace kapatılırken, başka workspace açılırken veya uygulamadan çıkılırken kaydedilmemiş istekler sorulmadan atılıyordu; artık kaydetme, atma veya vazgeçme sorulur.
- Dar pencerede veya büyük arayüz boyutunda istek ve yanıt sekmeleri yandaki panele taşıyordu; artık yatayda kaydırılır.
- Dil değiştirmek environment'ı "kaydedilmemiş" gösteriyordu.
- Environment veya koleksiyon değişkenine ad yazılınca satırın checkbox, gizli ve sil kontrolleri ancak ekran yenilenince çıkıyordu.
- Klavyeyle gezerken görünmeyen panel ayırıcısı odak alıyordu; ağaç öğeleri ve sekmeler ekran okuyucuya adsız sunuluyordu.
- Ağ ve TLS hatalarında mesajın iki kez başlıklanması ("Network error: Network error: ...").
- Makinede birden fazla NuGet kaynağı tanımlıyken derlemenin NU1507 ile kırılması: repodaki `nuget.config` paketleri yalnızca nuget.org'dan alır.
