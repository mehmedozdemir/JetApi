# Komut satırı: `jetapi`

`jetapi`, bir workspace'teki koleksiyonları, klasörleri ya da tek bir isteği uygulamayı açmadan çalıştırır. Uygulamadaki
koleksiyon çalıştırıcısıyla aynı kuralları kullanır (`JetApi.Runner`, [ADR-0009](adr/0009-runner-project.md)): sıra, tekrar,
veri dosyası, istekler arası bekleme, ilk hatada durma, testler (`tests`) ve değişken çıkarma (`extract`). CI'da JUnit XML ve
HTML raporu yazar.

## Kurulum

Her sürümde platform başına tek dosyalık, .NET kurulumu gerektirmeyen bir arşiv vardır:

| Platform | Dosya |
|---|---|
| Windows x64 / arm64 | `jetapi-cli-<sürüm>-win-x64.zip`, `…-win-arm64.zip` (içinde `jetapi.exe`) |
| macOS Apple silicon / Intel | `jetapi-cli-<sürüm>-osx-arm64.tar.gz`, `…-osx-x64.tar.gz` |
| Linux x64 / arm64 | `jetapi-cli-<sürüm>-linux-x64.tar.gz`, `…-linux-arm64.tar.gz` |

Arşivi açıp `jetapi`'yi `PATH`'e eklemek yeterlidir. Kaynaktan çalıştırmak için:

```bash
dotnet run --project src/JetApi.Cli -- run <workspace>
build/package.sh win-x64            # dist/jetapi-cli-<sürüm>-win-x64.zip (docs/packaging.md)
```

## Kullanım

```text
jetapi run <workspace> [<koleksiyon>[/<klasör>…]] [seçenekler]
```

- `<workspace>`: `workspace.yaml`'ın bulunduğu klasör.
- `<koleksiyon>/<klasör>/…`: her parça bir anahtar (dosya/klasör adı) ya da görünen addır (büyük/küçük harf fark etmez).
  Son parça bir istek de olabilir. Verilmezse tüm koleksiyonlar sırayla çalışır.

| Seçenek | Anlamı |
|---|---|
| `-e, --env <ad>` | Environment (anahtar ya da ad). Verilmezse environment kullanılmaz; uygulamada seçili olan **kullanılmaz**, böylece CI sonucu makineye bağlı olmaz. |
| `-d, --data <dosya>` | CSV ya da JSON veri dosyası; her satır bir tekrar, her sütun bir değişken (biçim uygulamadakiyle aynı) |
| `-n, --iterations <n>` | Veri dosyası yokken tekrar sayısı (varsayılan 1) |
| `--delay <ms>` | İstekler arası bekleme |
| `--bail` | İlk başarısız istekte dur (yanıt yok ya da bir test kaldı) |
| `--timeout <ms>` | Kendi süre sınırı olmayan isteklerin süre sınırı (varsayılan 30000) |
| `--var <ad=değer>` | Workspace'teki değişkenlerin üstüne yazan bir değişken; tekrarlanabilir. Gizli değerleri (token, parola) CI'ın gizli değişkenlerinden vermek için. |
| `--junit <dosya>` | JUnit XML raporu |
| `--html <dosya>` | Tek dosyalık HTML raporu (dış dosya yok, açık/koyu temaya uyar) |
| `--persist` | Kuralı environment, koleksiyon ya da global diyen çıkarılan değerleri dosyalara kaydet. Varsayılan kapalıdır: değerler koşuyla biter ve workspace değişmez. |
| `--lang <en\|tr>` | Çıktının dili (varsayılan sistem dili) |
| `--no-color` | Renksiz çıktı. Çıktı bir dosyaya ya da boruya gidiyorsa ve `NO_COLOR` tanımlıysa renk zaten kullanılmaz. |

Değişken öncelik sırası, yüksekten düşüğe: veri satırı ve koşuda çıkarılan değerler, `--var`, environment, koleksiyon, global.

OAuth 2.0: workspace'in token önbelleği (`.jetapi/`) kullanılır. Süresi dolan token yenilenir; client credentials ve password
akışları gerekirse yeni token alır. Authorization code akışı tarayıcı gerektirdiği için CLI'da token alamaz; önce uygulamada
token alınmalıdır. CI için client credentials önerilir. Çerezler koşu boyunca saklanır ve sonraki isteklere gönderilir.

### Çıkış kodları

| Kod | Anlamı |
|---|---|
| `0` | Her istek yanıt aldı ve her test geçti |
| `1` | Bir test kaldı, bir istek yanıt alamadı ya da koşu `--bail` ile durdu |
| `2` | Yanlış argüman; workspace, environment, hedef ya da veri dosyası bulunamadı/okunamadı; rapor yazılamadı |

### Örnek çıktı

```text
JetApi 0.3.0 · Users · Development

Iteration 1/2 · email=ali@example.com
  ✓ POST Create user · 201 Created · 84 ms
      ✓ Status equals 201
      ✓ $.id exists
  ✗ GET Get user · 404 Not Found · 12 ms
      ✗ Status equals 200 · Expected 200, got 404.

Failed · 4 requests · 5 checks passed · 1 failed · 0.3 s
JUnit report: /work/reports/junit.xml
```

### Raporlar

- **JUnit XML:** her tekrar bir `testsuite`, her test bir `testcase` (`classname` = koleksiyon ve klasör anahtarları,
  `name` = "İstek · test"). Testi olmayan bir istek, yanıt aldıysa geçen tek bir `testcase`'tir; yanıt alamayan istek `error` olur.
- **HTML:** özet (geçti/kaldı, istek ve test sayıları, süre) ve tekrar başına her istek ile testleri.

Uygulamada çalıştırıcı ekranındaki **Rapor** düğmesi aynı raporları kaydeder.

## CI örnekleri

Örneklerde workspace depo içindeki `api/` klasörüdür ve gizli değer CI'ın gizli değişkeninden `--var` ile verilir.

### GitHub Actions

```yaml
jobs:
  api-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: jetapi
        run: |
          curl -fsSL -o jetapi.tar.gz https://github.com/mehmedozdemir/JetApi/releases/download/v0.3.0/jetapi-cli-0.3.0-linux-x64.tar.gz
          tar -xzf jetapi.tar.gz
      - name: API tests
        run: ./jetapi run api Users -e Staging --var token=${{ secrets.API_TOKEN }} --junit reports/junit.xml --html reports/report.html
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: api-report
          path: reports/
      - uses: mikepenz/action-junit-report@v5
        if: always()
        with:
          report_paths: reports/junit.xml
```

### GitLab CI

```yaml
api-tests:
  image: mcr.microsoft.com/dotnet/runtime-deps:10.0
  script:
    - apt-get update && apt-get install -y curl
    - curl -fsSL -o jetapi.tar.gz https://github.com/mehmedozdemir/JetApi/releases/download/v0.3.0/jetapi-cli-0.3.0-linux-x64.tar.gz
    - tar -xzf jetapi.tar.gz
    - ./jetapi run api Users -e Staging --var token=$API_TOKEN --junit reports/junit.xml --html reports/report.html
  artifacts:
    when: always
    paths: [reports/]
    reports:
      junit: reports/junit.xml
```

### Azure DevOps

```yaml
steps:
  - pwsh: |
      Invoke-WebRequest https://github.com/mehmedozdemir/JetApi/releases/download/v0.3.0/jetapi-cli-0.3.0-win-x64.zip -OutFile jetapi.zip
      Expand-Archive jetapi.zip -DestinationPath tools
      ./tools/jetapi.exe run api Users -e Staging --var token=$(API_TOKEN) --junit $(Build.ArtifactStagingDirectory)/junit.xml --html $(Build.ArtifactStagingDirectory)/report.html
    displayName: API tests
  - task: PublishTestResults@2
    condition: always()
    inputs:
      testResultsFormat: JUnit
      testResultsFiles: $(Build.ArtifactStagingDirectory)/junit.xml
  - publish: $(Build.ArtifactStagingDirectory)
    condition: always()
    artifact: api-report
```

### Jenkins

```groovy
pipeline {
  agent any
  environment { API_TOKEN = credentials('api-token') }
  stages {
    stage('API tests') {
      steps {
        sh '''
          curl -fsSL -o jetapi.tar.gz https://github.com/mehmedozdemir/JetApi/releases/download/v0.3.0/jetapi-cli-0.3.0-linux-x64.tar.gz
          tar -xzf jetapi.tar.gz
          ./jetapi run api Users -e Staging --var token=$API_TOKEN --junit reports/junit.xml --html reports/report.html
        '''
      }
    }
  }
  post {
    always {
      junit 'reports/junit.xml'
      archiveArtifacts artifacts: 'reports/**'
    }
  }
}
```

## Sınırlar

- Yalnızca unary gRPC çağrıları ve HTTP istekleri çalışır (uygulamadaki gibi).
- `dotnet tool` paketi henüz yok; NuGet'e yayın ayrı bir karar olarak bekliyor.
