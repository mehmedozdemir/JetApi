# cURL içe aktarma ve kod parçacıkları

JetApi bir cURL komutunu isteğe çevirir (IMP-4) ve bir isteği cURL'e ya da başka bir dilde çalıştırılabilir koda dönüştürür.
İkisi de motor katmanındadır: içe aktarma `JetApi.Import` içindeki `CurlImporter`, kod üretimi `JetApi.Http` içindeki
`CodeSnippetGenerator`. Masaüstü arayüzü henüz bağlanmadı.

## cURL içe aktarma

```csharp
CurlImporter.LooksLikeCurl(text);                                   // "curl", "curl.exe" ya da bir yol + curl ile mi başlıyor?
ImportResult<HttpRequestDefinition> result = CurlImporter.Import(text);
```

Hedef, Postman içe aktarmada olduğu gibi kayıpsız taşımadır: komutun yaptığı istek aynen kurulur, taşınamayan her şey
`result.Issues` içinde raporlanır. Komut çalıştırılmaz, dosya okunmaz. İstek "METOT yol" olarak adlandırılır
(`POST /users`), anahtarı bu addan üretilir.

### Kabuklar

| Kabuk | Kaynak | Desteklenen sözdizimi |
|---|---|---|
| bash / zsh | Chrome ve Firefox "Copy as cURL (bash)", Postman, dokümantasyon | `'...'`, `"..."`, `\` kaçışları, `$'...'` (ANSI-C: `\n`, `\t`, `\xHH`, `\uHHHH`, `\UHHHHHHHH`, sekizlik), `\` + satır sonu |
| Windows cmd | Chrome ve Firefox "Copy as cURL (cmd)" | `^` kaçışları, `^` + satır sonu, `^"`, curl.exe'nin C çalışma zamanı kuralları (`\"`) |
| PowerShell | `curl.exe` ile yazılan komutlar | `'...'` (`''`), `"..."` (`` ` `` kaçışları), `` ` `` + satır sonu, `& curl.exe` |

Kabuk, metne bakılarak seçilir: `^"` ya da `^` + satır sonu cmd, `` ` `` + satır sonu, `` `" ``, `$env:` ya da baştaki `&`
PowerShell, diğerleri bash. Kabuk değişkenleri (`$TOKEN`, `${BASE_URL}`, `$env:API_TOKEN`) JetApi değişkenine
(`{{TOKEN}}`) çevrilir ve raporda listelenir; tek tırnak içindekiler olduğu gibi kalır. Tırnaksız `|`, `;`, `&`, `>`
komutu bitirir (`curl ... | jq .`).

### Seçenekler

| curl | JetApi | Not |
|---|---|---|
| URL, `--url` | URL + sorgu parametreleri | Fazladan URL'ler raporda |
| `-X`, `--request` | Metot | Yoksa: `-I` HEAD, `-G` GET, `-T` PUT, veri ya da form varsa POST, değilse GET |
| `-H`, `--header` | Header | `Ad;` boş değer, `Ad:` atlanır, `-H @dosya` raporda |
| `-d`, `--data`, `--data-ascii`, `--data-binary`, `--data-raw` | Body | Birden çok `-d` `&` ile birleşir; `@dosya` tek başınaysa dosya body'si |
| `--data-urlencode` | Form alanı | `ad=içerik`, `=içerik`, `içerik`; `ad@dosya` raporda |
| `--json` | JSON body | `Accept: application/json` eklenir (yoksa) |
| `-G`, `--get` | Veriler sorguya taşınır | |
| `--url-query` | Sorgu parametresi | |
| `-F`, `--form` | Multipart | `ad=değer`, `ad=@yol`, `ad=<yol` (dosya yükleme olarak, raporda), `;type=`, tırnaklı değerler |
| `--form-string` | Multipart metin alanı | Değer olduğu gibi alınır |
| `-T`, `--upload-file` | Dosya body'si | |
| `-u`, `--user` | Basic auth | Parola yoksa raporda |
| `--oauth2-bearer` | Bearer auth | |
| `Authorization: Bearer …` / `Basic …` header'ı | Bearer / Basic auth | Header kaldırılır; token Auth sekmesinde değişkene taşınabilir |
| `--digest`, `--ntlm`, `--negotiate`, `--anyauth` | Auth yok | Raporda uyarı |
| `-A`, `-e`, `-b`, `-r` | `User-Agent`, `Referer`, `Cookie`, `Range` | `-b dosya` raporda |
| `-L`, `--location` | Yönlendirmeleri izle | curl varsayılan olarak izlemez, içe aktarılan istek de izlemez |
| `-k`, `--insecure` | Sertifikayı doğrulama | |
| `-m`, `--max-time` | Zaman aşımı | |
| `-I`, `--head` | HEAD | |
| `-s`, `-S`, `-v`, `-i`, `-o`, `--compressed`, `--http2`, `--retry`, `--connect-timeout`… | — | Sessizce yok sayılır (yalnızca çıktıyı ya da bağlantıyı etkiler) |
| `-x`, `--proxy`, `-E`, `--cert`, `--cacert`, `--resolve`, `-K`… | — | Raporda: karşılığı henüz yok |
| Bilinmeyen seçenekler | — | Raporda |

Kısa seçenekler birleşebilir (`-sSL`) ve değer bitişik yazılabilir (`-XPOST`, `-HAccept:*/*`, `-d'{}'`).

### Body türü

`Content-Type` header'ı body türünü belirler: `json` içeriyorsa JSON, `xml` içeriyorsa XML,
`application/x-www-form-urlencoded` ise form, diğerleri metin. JetApi'nin bu tür için zaten gönderdiği değerle aynıysa
(örneğin `application/json`) header kaldırılır; farklıysa (`application/json; charset=utf-8`) header kalır ve kazanır.

`Content-Type` yoksa curl veriyi form olarak gönderir. Bu yüzden:

- `ad=değer&...` biçimindeki veri form olur (değerler çözülür, JetApi gönderirken yeniden kodlar).
- JSON gibi görünen veri JSON body olur; raporda bilgi notu vardır (curl bunu form türüyle gönderirdi).
- Diğer metinler metin body olur ve curl'ün gönderdiği `Content-Type: application/x-www-form-urlencoded` header'ı eklenir.

### Sorgu parametreleri

JetApi sorgu parametrelerini gönderirken kendisi kodlar, bu yüzden değerler çözülerek saklanır (`%20` → boşluk).
Bu ancak kayıpsızsa yapılır. Bir çiftte `+` varsa (sunucuya göre boşluk ya da artı), `=` yoksa (`?debug`) ya da kaçış
geçerli UTF-8 değilse, sorgu bütünüyle URL'de bırakılır. `{{değişken}}` yer tutucuları olduğu gibi kalır.

## Kod parçacıkları

```csharp
string code = CodeSnippetGenerator.Generate(preparedRequest, SnippetLanguage.Python);
```

Girdi `HttpRequestPreparer` çıktısıdır: değişkenler çözülmüş, auth header'ları eklenmiştir, yani parçacık JetApi'nin
gönderdiği isteği gönderir. Satırlar `\n` ile ayrılır, sonda satır sonu yoktur.

| Dil | Üretilen kod | Body türleri | Yönlendirme / sertifika / zaman aşımı |
|---|---|---|---|
| `Curl` | bash/zsh için curl, `\` ile satır devamı, tek tırnak (`'\''`) | `--data-raw`, `--data-urlencode`, `--form-string`, `--form 'ad=@"yol";type=…'`, `--data-binary @yol` | `--location`, `--insecure`, `--max-time` |
| `CurlWindows` | cmd için curl (Chrome'un biçimi): `^"…^"`, `^` kaçışları, `%^AD%`, satır sonu `^` + iki satır sonu | Aynı | Aynı |
| `PowerShell` | `Invoke-RestMethod @params` (splatting), `[ordered]` header tablosu | String (ASCII dışı ise UTF-8 bayt), form tablosu, `-Form` (PowerShell 7), `-InFile` | `MaximumRedirection = 0`, `SkipCertificateCheck`, `TimeoutSec` |
| `CSharp` | `HttpClient`, top-level statements, `using var request = new HttpRequestMessage(...)` | `StringContent` (çok satırlı metin raw string literal), `FormUrlEncodedContent`, `MultipartFormDataContent`, `StreamContent` | `HttpClientHandler`: `AllowAutoRedirect`, `DangerousAcceptAnyServerCertificateValidator`; `Timeout` |
| `JavaScript` | `fetch` ve top-level `await` (Node.js 20+ ES modülü) | String (çok satırlı ise template literal), `URLSearchParams`, `FormData`, `openAsBlob` | `redirect: "manual"`, `AbortSignal.timeout`; sertifika için yorum (`NODE_TLS_REJECT_UNAUTHORIZED`) |
| `Python` | `requests.request(...)` | `data=` (ASCII dışı ise UTF-8 bayt), form sözlüğü ya da çift listesi, `files=`, `with open(...)` | `allow_redirects=False`, `verify=False`, `timeout=` |

Notlar:

- JSON ve diğer metin body'leri girintisiyle korunur.
- Açık bir `Content-Type` header'ı body türünün yerine geçer. Multipart'ta `Content-Type` her zaman araca bırakılır,
  çünkü boundary'yi o seçer.
- Tekrarlanan header'lar curl, C# ve JavaScript'te (çift listesi) tekrarlı kalır; sözlük kullanan PowerShell ve
  Python'da virgülle birleştirilir (`Cookie` için `; `).
- PowerShell'de multipart parçalarının içerik türü taşınamaz; JavaScript'te metin parçalarının içerik türü taşınamaz.
- cmd çıktısı Komut İstemi penceresine yapıştırmak içindir; `.bat` dosyasında `%` farklı işlendiği için çalışmaz.
- Üretilen her dilin çıktısı yerel bir echo sunucusuna karşı çalıştırılarak doğrulandı. Testler dil ve body türü başına
  beklenen çıktıyı sabitler; cURL ve cmd çıktıları ayrıca `CurlImporter` ile geri okunup aynı isteği verdiği doğrulanır.
