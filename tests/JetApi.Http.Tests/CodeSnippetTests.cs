using System.Text;
using JetApi.Core.Model;
using JetApi.Core.Variables;

namespace JetApi.Http.Tests;

public sealed class CodeSnippetTests
{
    private static readonly PreparedHttpRequest GetRequest = new(
        "GET", new Uri("https://api.example.com/users?page=2&sort=name"), [new("Accept", "application/json")], PreparedBody.None, HttpRequestSettings.Default);

    private static readonly PreparedHttpRequest JsonRequest = new(
        "POST", new Uri("https://api.example.com/users"), [new("Authorization", "Bearer t0ken")],
        new BytesBody(Encoding.UTF8.GetBytes("{\n  \"name\": \"Ada\",\n  \"active\": true\n}"), "application/json"), HttpRequestSettings.Default);

    private static readonly PreparedHttpRequest FormRequest = new(
        "POST", new Uri("https://api.example.com/login"), [], new FormBody([new("user", "ada"), new("note", "a b&c")]), HttpRequestSettings.Default);

    private static readonly PreparedHttpRequest MultipartRequest = new(
        "POST", new Uri("https://api.example.com/reports"), [],
        new MultipartBody([new("title", "Report", false, null), new("file", "/data/report.pdf", true, "application/pdf")]), HttpRequestSettings.Default);

    private static readonly PreparedHttpRequest FileRequest = new(
        "PUT", new Uri("https://api.example.com/blobs/1"), [], new FileBody("/data/blob.bin", "application/octet-stream"), HttpRequestSettings.Default);

    private static PreparedHttpRequest TextRequest(string text, IReadOnlyList<KeyValuePair<string, string>>? headers = null, string method = "POST") =>
        new(method, new Uri("https://api.example.com/echo"), headers ?? [], new BytesBody(Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8"), HttpRequestSettings.Default);

    private static string Generate(PreparedHttpRequest request, SnippetLanguage language) => CodeSnippetGenerator.Generate(request, language);

    // Escaping

    [Fact]
    public void Curl_ClosesAndReopensSingleQuotes() =>
        Generate(TextRequest("it's $HOME `id` \\n"), SnippetLanguage.Curl).ShouldContain("--data-raw 'it'\\''s $HOME `id` \\n'");

    [Fact]
    public void Curl_KeepsNewlinesAndUnicodeInsideQuotes() =>
        Generate(TextRequest("Merhaba\nDünya ✓ 🚀"), SnippetLanguage.Curl).ShouldContain("--data-raw 'Merhaba\nDünya ✓ 🚀'");

    [Fact]
    public void CurlWindows_EscapesForCmdAndTheCRuntime()
    {
        var code = Generate(TextRequest("say \"hi\" & 100%PATH% ^ <x> C:\\dir\\ \\\"q\\\"\nnext"), SnippetLanguage.CurlWindows);

        // A quote is \" for curl.exe and ^-escaped for cmd; backslashes double only before a quote.
        code.ShouldContain("--data-raw ^\"say ^\\^\"hi^\\^\" ^& 100%^PATH% ^^ ^<x^> C:^\\dir^\\ ^\\^\\^\\^\"q^\\^\\^\\^\"^\n\nnext^\"");
    }

    [Fact]
    public void CurlWindows_DoublesTrailingBackslashesBeforeTheClosingQuote() =>
        Generate(TextRequest("C:\\temp\\"), SnippetLanguage.CurlWindows).ShouldContain("--data-raw ^\"C:^\\temp^\\^\\^\"");

    [Fact]
    public void PowerShell_DoublesEverySingleQuote() =>
        Generate(TextRequest("it's ‘smart’ $env:PATH `n"), SnippetLanguage.PowerShell).ShouldContain("$body = 'it''s ‘‘smart’’ $env:PATH `n'");

    [Fact]
    public void PowerShell_SendsNonAsciiBodiesAsUtf8Bytes()
    {
        Generate(TextRequest("Dünya"), SnippetLanguage.PowerShell).ShouldContain("Body = [System.Text.Encoding]::UTF8.GetBytes($body)");
        Generate(TextRequest("World"), SnippetLanguage.PowerShell).ShouldContain("Body = $body");
    }

    [Fact]
    public void CSharp_UsesALongerRawLiteralWhenTheTextHasTripleQuotes() =>
        Generate(TextRequest("a\n\"\"\"b\"\"\"\n"), SnippetLanguage.CSharp).ShouldContain(
            "var content = new StringContent(\"\"\"\"\n    a\n    \"\"\"b\"\"\"\n\n    \"\"\"\");");

    [Fact]
    public void CSharp_EscapesSingleLineText() =>
        Generate(TextRequest("tab\t\"q\" \\ \r\u0001 ✓"), SnippetLanguage.CSharp).ShouldContain(
            "new StringContent(\"tab\\t\\\"q\\\" \\\\ \\r\\u0001 ✓\")");

    [Fact]
    public void CSharp_UsesARegularLiteralForMultiLineTextWithCarriageReturns() =>
        Generate(TextRequest("a\r\nb"), SnippetLanguage.CSharp).ShouldContain("new StringContent(\"a\\r\\nb\")");

    [Fact]
    public void CSharp_EscapesLoneSurrogatesButKeepsPairs() =>
        Generate(GetRequest with { Headers = [new("X-Text", "\ud800 🚀")] }, SnippetLanguage.CSharp).ShouldContain("TryAddWithoutValidation(\"X-Text\", \"\\uD800 🚀\");");

    [Fact]
    public void CSharp_UsesVerbatimStringsForWindowsPaths() =>
        Generate(FileRequest with { Body = new FileBody(@"C:\data\""x"".bin", "application/octet-stream") }, SnippetLanguage.CSharp)
            .ShouldContain("File.OpenRead(@\"C:\\data\\\"\"x\"\".bin\")");

    [Fact]
    public void JavaScript_EscapesTemplateLiterals() =>
        Generate(TextRequest("${a} `b` \\ $c\nnext"), SnippetLanguage.JavaScript).ShouldContain("body: `\\${a} \\`b\\` \\\\ $c\nnext`,");

    [Fact]
    public void JavaScript_EscapesDoubleQuotedStrings() =>
        Generate(TextRequest("say \"hi\"\t\u2028"), SnippetLanguage.JavaScript).ShouldContain("body: \"say \\\"hi\\\"\\t\\u2028\",");

    [Fact]
    public void Python_EscapesQuotesOnlyWhereTheyCouldCloseATripleQuotedString() =>
        Generate(TextRequest("\"a\" \"\"\"b\\\nend\""), SnippetLanguage.Python).ShouldContain("payload = \"\"\"\"a\" \\\"\\\"\"b\\\\\nend\\\"\"\"\"");

    [Fact]
    public void Python_EscapesControlCharacters() =>
        Generate(TextRequest("a\tb\u0001\"c\""), SnippetLanguage.Python).ShouldContain("payload = \"a\\tb\\x01\\\"c\\\"\"");

    [Fact]
    public void Python_EncodesNonAsciiBodiesAsUtf8() =>
        Generate(TextRequest("Dünya"), SnippetLanguage.Python).ShouldContain("data=payload.encode(\"utf-8\")");

    // Headers and content types

    [Theory]
    [InlineData(SnippetLanguage.Curl, "--header 'Content-Type: text/csv'")]
    [InlineData(SnippetLanguage.PowerShell, "ContentType = 'text/csv'")]
    [InlineData(SnippetLanguage.CSharp, "content.Headers.ContentType = MediaTypeHeaderValue.Parse(\"text/csv\");")]
    [InlineData(SnippetLanguage.JavaScript, "\"Content-Type\": \"text/csv\",")]
    [InlineData(SnippetLanguage.Python, "\"Content-Type\": \"text/csv\",")]
    public void AnExplicitContentTypeHeaderReplacesTheBodyType(SnippetLanguage language, string expected)
    {
        var code = Generate(TextRequest("a,b", [new("content-type", "text/csv")]), language);

        code.ShouldContain(expected);
        code.ShouldNotContain("text/plain");
    }

    [Theory]
    [InlineData(SnippetLanguage.Curl)]
    [InlineData(SnippetLanguage.CurlWindows)]
    [InlineData(SnippetLanguage.PowerShell)]
    [InlineData(SnippetLanguage.CSharp)]
    [InlineData(SnippetLanguage.JavaScript)]
    [InlineData(SnippetLanguage.Python)]
    public void Multipart_LeavesTheContentTypeToTheTool(SnippetLanguage language) =>
        Generate(MultipartRequest with { Headers = [new("Content-Type", "multipart/form-data")] }, language).ShouldNotContain("multipart/form-data");

    [Fact]
    public void Curl_SendsAnEmptyHeaderWithASemicolon() =>
        Generate(GetRequest with { Headers = [new("X-Empty", string.Empty)] }, SnippetLanguage.Curl).ShouldContain("--header 'X-Empty;'");

    [Fact]
    public void CSharp_PutsContentHeadersOnTheContent()
    {
        var code = Generate(TextRequest("x", [new("Content-Language", "tr"), new("X-Trace", "1")]), SnippetLanguage.CSharp);

        code.ShouldContain("content.Headers.TryAddWithoutValidation(\"Content-Language\", \"tr\");");
        code.ShouldContain("request.Headers.TryAddWithoutValidation(\"X-Trace\", \"1\");");
    }

    [Fact]
    public void RepeatedHeaders_StayRepeatedWhereTheLanguageAllowsIt()
    {
        var request = GetRequest with { Headers = [new("X-Tag", "a"), new("x-tag", "b")] };

        Generate(request, SnippetLanguage.Curl).ShouldContain("--header 'X-Tag: a' \\\n  --header 'x-tag: b'");
        Generate(request, SnippetLanguage.JavaScript).ShouldContain("  headers: [\n    [\"X-Tag\", \"a\"],\n    [\"x-tag\", \"b\"],\n  ],");
        Generate(request, SnippetLanguage.Python).ShouldContain("\"X-Tag\": \"a, b\",");
        Generate(request, SnippetLanguage.PowerShell).ShouldContain("'X-Tag' = 'a, b'");
    }

    [Fact]
    public void RepeatedFormFields_KeepTheirOrder()
    {
        var request = FormRequest with { Body = new FormBody([new("id", "1"), new("id", "2")]) };

        Generate(request, SnippetLanguage.Python).ShouldContain("payload = [\n    (\"id\", \"1\"),\n    (\"id\", \"2\"),\n]");
        Generate(request, SnippetLanguage.PowerShell).ShouldContain("Body = 'id=1&id=2'");
        Generate(request, SnippetLanguage.PowerShell).ShouldContain("ContentType = 'application/x-www-form-urlencoded'");
    }

    [Fact]
    public void Curl_EncodesFormNamesThatDataUrlEncodeWouldSendAsIs() =>
        Generate(FormRequest with { Body = new FormBody([new("a b", "c&d")]) }, SnippetLanguage.Curl).ShouldContain("--data-raw 'a%20b=c%26d'");

    [Fact]
    public void Curl_QuotesFormValuesWithSeparators() =>
        Generate(MultipartRequest with { Body = new MultipartBody([new("meta", "{\"a\":\"x;y\"}", false, "application/json"), new("f", "/tmp/a;b.png", true, null)]) }, SnippetLanguage.Curl)
            .ShouldContain("--form 'meta=\"{\\\"a\\\":\\\"x;y\\\"}\";type=application/json' \\\n  --form 'f=@\"/tmp/a;b.png\"'");

    // Methods and settings

    [Fact]
    public void Curl_UsesHeadForHead() =>
        Generate(GetRequest with { Method = "HEAD" }, SnippetLanguage.Curl).ShouldStartWith("curl --head 'https://");

    [Fact]
    public void Curl_NamesTheMethodWhenCurlWouldPickAnother()
    {
        Generate(TextRequest("x", method: "GET"), SnippetLanguage.Curl).ShouldStartWith("curl --request GET ");
        Generate(GetRequest with { Method = "DELETE" }, SnippetLanguage.Curl).ShouldStartWith("curl --request DELETE ");
        Generate(TextRequest("x"), SnippetLanguage.Curl).ShouldStartWith("curl https://");
    }

    [Fact]
    public void CustomMethods_UseEachLanguagesEscapeHatch()
    {
        var request = GetRequest with { Method = "PURGE" };

        Generate(request, SnippetLanguage.CSharp).ShouldContain("new HttpRequestMessage(new HttpMethod(\"PURGE\"), ");
        Generate(request, SnippetLanguage.PowerShell).ShouldContain("CustomMethod = 'PURGE'");
        Generate(request, SnippetLanguage.JavaScript).ShouldContain("method: \"PURGE\",");
        Generate(request, SnippetLanguage.Python).ShouldContain("requests.request(\"PURGE\", url");
    }

    [Fact]
    public void Settings_AreCarriedOver()
    {
        var request = JsonRequest with { Settings = new HttpRequestSettings(FollowRedirects: false, ValidateCertificates: false, Timeout: TimeSpan.FromSeconds(2.5)) };

        var curl = Generate(request, SnippetLanguage.Curl);
        curl.ShouldNotContain("--location");
        curl.ShouldContain("--insecure \\\n  --max-time 2.5 \\");

        var powerShell = Generate(request, SnippetLanguage.PowerShell);
        powerShell.ShouldStartWith("# Requires PowerShell 7 or later.");
        powerShell.ShouldContain("    MaximumRedirection = 0\n    SkipCertificateCheck = $true\n    TimeoutSec = 3\n");

        var csharp = Generate(request, SnippetLanguage.CSharp);
        csharp.ShouldContain("    AllowAutoRedirect = false,\n");
        csharp.ShouldContain("    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,\n");
        csharp.ShouldContain("using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(2500) };");

        var javaScript = Generate(request, SnippetLanguage.JavaScript);
        javaScript.ShouldContain("  redirect: \"manual\",\n  signal: AbortSignal.timeout(2500),\n");
        javaScript.ShouldContain("NODE_TLS_REJECT_UNAUTHORIZED=0");

        Generate(request, SnippetLanguage.Python).ShouldContain(", allow_redirects=False, verify=False, timeout=2.5)");
    }

    [Fact]
    public void WholeSecondTimeouts_StayReadable()
    {
        var request = GetRequest with { Settings = HttpRequestSettings.Default with { Timeout = TimeSpan.FromSeconds(30) } };

        Generate(request, SnippetLanguage.CSharp).ShouldContain("{ Timeout = TimeSpan.FromSeconds(30) }");
        Generate(request, SnippetLanguage.Curl).ShouldContain("--max-time 30");
        Generate(request, SnippetLanguage.Python).ShouldContain("timeout=30)");
    }

    [Fact]
    public void Generate_WorksOnAPreparedRequest()
    {
        var definition = new HttpRequestDefinition
        {
            Key = "create-user",
            Name = "Create user",
            Method = "post",
            Url = "{{baseUrl}}/users",
            QueryParameters = [new("notify", "yes please")],
            Body = HttpBody.Json("{\"name\": \"{{name}}\"}"),
        };
        var variables = new VariableResolver([new VariableLayer(VariableSource.Environment, [new Variable("baseUrl", "https://api.example.com"), new Variable("name", "Ada")])]);
        var prepared = HttpRequestPreparer.Prepare(definition, new BasicAuth("ada", "s3cret"), variables, Path.GetTempPath()).Request!;

        Generate(prepared, SnippetLanguage.Curl).ShouldBe(
            """
            curl 'https://api.example.com/users?notify=yes%20please' \
              --location \
              --header 'Authorization: Basic YWRhOnMzY3JldA==' \
              --header 'Content-Type: application/json' \
              --data-raw '{"name": "Ada"}'
            """);
    }

    [Fact]
    public void Generate_RejectsUnknownLanguages() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Generate(GetRequest, (SnippetLanguage)99));

    // Golden outputs per language and body mode

    [Fact]
    public void Get_Curl() =>
        CodeSnippetGenerator.Generate(GetRequest, SnippetLanguage.Curl).ShouldBe(
            """"
            curl 'https://api.example.com/users?page=2&sort=name' \
              --location \
              --header 'Accept: application/json'
            """");

    [Fact]
    public void Get_CurlWindows() =>
        CodeSnippetGenerator.Generate(GetRequest, SnippetLanguage.CurlWindows).ShouldBe(
            """"
            curl ^"https://api.example.com/users?page=2^&sort=name^" ^
              --location ^
              --header ^"Accept: application/json^"
            """");

    [Fact]
    public void Get_PowerShell() =>
        CodeSnippetGenerator.Generate(GetRequest, SnippetLanguage.PowerShell).ShouldBe(
            """"
            $headers = [ordered]@{
                'Accept' = 'application/json'
            }
            $params = @{
                Uri = 'https://api.example.com/users?page=2&sort=name'
                Headers = $headers
            }

            Invoke-RestMethod @params
            """");

    [Fact]
    public void Get_CSharp() =>
        CodeSnippetGenerator.Generate(GetRequest, SnippetLanguage.CSharp).ShouldBe(
            """"
            using var client = new HttpClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/users?page=2&sort=name");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await client.SendAsync(request);
            Console.WriteLine((int)response.StatusCode);
            Console.WriteLine(await response.Content.ReadAsStringAsync());
            """");

    [Fact]
    public void Get_JavaScript() =>
        CodeSnippetGenerator.Generate(GetRequest, SnippetLanguage.JavaScript).ShouldBe(
            """"
            const response = await fetch("https://api.example.com/users?page=2&sort=name", {
              headers: {
                "Accept": "application/json",
              },
            });

            console.log(response.status);
            console.log(await response.text());
            """");

    [Fact]
    public void Get_Python() =>
        CodeSnippetGenerator.Generate(GetRequest, SnippetLanguage.Python).ShouldBe(
            """"
            import requests

            url = "https://api.example.com/users?page=2&sort=name"
            headers = {
                "Accept": "application/json",
            }

            response = requests.request("GET", url, headers=headers)

            print(response.status_code)
            print(response.text)
            """");

    [Fact]
    public void Json_Curl() =>
        CodeSnippetGenerator.Generate(JsonRequest, SnippetLanguage.Curl).ShouldBe(
            """"
            curl https://api.example.com/users \
              --location \
              --header 'Authorization: Bearer t0ken' \
              --header 'Content-Type: application/json' \
              --data-raw '{
              "name": "Ada",
              "active": true
            }'
            """");

    [Fact]
    public void Json_CurlWindows() =>
        CodeSnippetGenerator.Generate(JsonRequest, SnippetLanguage.CurlWindows).ShouldBe(
            """"
            curl ^"https://api.example.com/users^" ^
              --location ^
              --header ^"Authorization: Bearer t0ken^" ^
              --header ^"Content-Type: application/json^" ^
              --data-raw ^"^{^

              ^\^"name^\^": ^\^"Ada^\^",^

              ^\^"active^\^": true^

            ^}^"
            """");

    [Fact]
    public void Json_PowerShell() =>
        CodeSnippetGenerator.Generate(JsonRequest, SnippetLanguage.PowerShell).ShouldBe(
            """"
            $headers = [ordered]@{
                'Authorization' = 'Bearer t0ken'
            }
            $body = '{
              "name": "Ada",
              "active": true
            }'
            $params = @{
                Uri = 'https://api.example.com/users'
                Method = 'Post'
                Headers = $headers
                ContentType = 'application/json'
                Body = $body
            }

            Invoke-RestMethod @params
            """");

    [Fact]
    public void Json_CSharp() =>
        CodeSnippetGenerator.Generate(JsonRequest, SnippetLanguage.CSharp).ShouldBe(
            """"
            using System.Net.Http.Headers;

            using var client = new HttpClient();

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/users");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer t0ken");
            var content = new StringContent("""
                {
                  "name": "Ada",
                  "active": true
                }
                """);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            request.Content = content;

            using var response = await client.SendAsync(request);
            Console.WriteLine((int)response.StatusCode);
            Console.WriteLine(await response.Content.ReadAsStringAsync());
            """");

    [Fact]
    public void Json_JavaScript() =>
        CodeSnippetGenerator.Generate(JsonRequest, SnippetLanguage.JavaScript).ShouldBe(
            """"
            const response = await fetch("https://api.example.com/users", {
              method: "POST",
              headers: {
                "Authorization": "Bearer t0ken",
                "Content-Type": "application/json",
              },
              body: `{
              "name": "Ada",
              "active": true
            }`,
            });

            console.log(response.status);
            console.log(await response.text());
            """");

    [Fact]
    public void Json_Python() =>
        CodeSnippetGenerator.Generate(JsonRequest, SnippetLanguage.Python).ShouldBe(
            """"
            import requests

            url = "https://api.example.com/users"
            headers = {
                "Authorization": "Bearer t0ken",
                "Content-Type": "application/json",
            }
            payload = """{
              "name": "Ada",
              "active": true
            }"""

            response = requests.request("POST", url, headers=headers, data=payload)

            print(response.status_code)
            print(response.text)
            """");

    [Fact]
    public void Form_Curl() =>
        CodeSnippetGenerator.Generate(FormRequest, SnippetLanguage.Curl).ShouldBe(
            """"
            curl https://api.example.com/login \
              --location \
              --data-urlencode user=ada \
              --data-urlencode 'note=a b&c'
            """");

    [Fact]
    public void Form_CurlWindows() =>
        CodeSnippetGenerator.Generate(FormRequest, SnippetLanguage.CurlWindows).ShouldBe(
            """"
            curl ^"https://api.example.com/login^" ^
              --location ^
              --data-urlencode ^"user=ada^" ^
              --data-urlencode ^"note=a b^&c^"
            """");

    [Fact]
    public void Form_PowerShell() =>
        CodeSnippetGenerator.Generate(FormRequest, SnippetLanguage.PowerShell).ShouldBe(
            """"
            $form = [ordered]@{
                'user' = 'ada'
                'note' = 'a b&c'
            }
            $params = @{
                Uri = 'https://api.example.com/login'
                Method = 'Post'
                Body = $form
            }

            Invoke-RestMethod @params
            """");

    [Fact]
    public void Form_CSharp() =>
        CodeSnippetGenerator.Generate(FormRequest, SnippetLanguage.CSharp).ShouldBe(
            """"
            using var client = new HttpClient();

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/login");
            var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("user", "ada"),
                new KeyValuePair<string, string>("note", "a b&c"),
            ]);
            request.Content = content;

            using var response = await client.SendAsync(request);
            Console.WriteLine((int)response.StatusCode);
            Console.WriteLine(await response.Content.ReadAsStringAsync());
            """");

    [Fact]
    public void Form_JavaScript() =>
        CodeSnippetGenerator.Generate(FormRequest, SnippetLanguage.JavaScript).ShouldBe(
            """"
            const response = await fetch("https://api.example.com/login", {
              method: "POST",
              body: new URLSearchParams([
                ["user", "ada"],
                ["note", "a b&c"],
              ]),
            });

            console.log(response.status);
            console.log(await response.text());
            """");

    [Fact]
    public void Form_Python() =>
        CodeSnippetGenerator.Generate(FormRequest, SnippetLanguage.Python).ShouldBe(
            """"
            import requests

            url = "https://api.example.com/login"
            payload = {
                "user": "ada",
                "note": "a b&c",
            }

            response = requests.request("POST", url, data=payload)

            print(response.status_code)
            print(response.text)
            """");

    [Fact]
    public void Multipart_Curl() =>
        CodeSnippetGenerator.Generate(MultipartRequest, SnippetLanguage.Curl).ShouldBe(
            """"
            curl https://api.example.com/reports \
              --location \
              --form-string title=Report \
              --form 'file=@"/data/report.pdf";type=application/pdf'
            """");

    [Fact]
    public void Multipart_CurlWindows() =>
        CodeSnippetGenerator.Generate(MultipartRequest, SnippetLanguage.CurlWindows).ShouldBe(
            """"
            curl ^"https://api.example.com/reports^" ^
              --location ^
              --form-string ^"title=Report^" ^
              --form ^"file=^@^\^"/data/report.pdf^\^";type=application/pdf^"
            """");

    [Fact]
    public void Multipart_PowerShell() =>
        CodeSnippetGenerator.Generate(MultipartRequest, SnippetLanguage.PowerShell).ShouldBe(
            """"
            # Requires PowerShell 7 or later.
            $form = [ordered]@{
                'title' = 'Report'
                'file' = (Get-Item -LiteralPath '/data/report.pdf')
            }
            $params = @{
                Uri = 'https://api.example.com/reports'
                Method = 'Post'
                Form = $form
            }

            Invoke-RestMethod @params
            """");

    [Fact]
    public void Multipart_CSharp() =>
        CodeSnippetGenerator.Generate(MultipartRequest, SnippetLanguage.CSharp).ShouldBe(
            """"
            using System.Net.Http.Headers;

            using var client = new HttpClient();

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/reports");
            var content = new MultipartFormDataContent();
            content.Add(new StringContent("Report"), "title");
            var file2 = new StreamContent(File.OpenRead("/data/report.pdf"));
            file2.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
            content.Add(file2, "file", "report.pdf");
            request.Content = content;

            using var response = await client.SendAsync(request);
            Console.WriteLine((int)response.StatusCode);
            Console.WriteLine(await response.Content.ReadAsStringAsync());
            """");

    [Fact]
    public void Multipart_JavaScript() =>
        CodeSnippetGenerator.Generate(MultipartRequest, SnippetLanguage.JavaScript).ShouldBe(
            """"
            import { openAsBlob } from "node:fs";

            const form = new FormData();
            form.append("title", "Report");
            form.append("file", await openAsBlob("/data/report.pdf", { type: "application/pdf" }), "report.pdf");

            const response = await fetch("https://api.example.com/reports", {
              method: "POST",
              body: form,
            });

            console.log(response.status);
            console.log(await response.text());
            """");

    [Fact]
    public void Multipart_Python() =>
        CodeSnippetGenerator.Generate(MultipartRequest, SnippetLanguage.Python).ShouldBe(
            """"
            import requests

            url = "https://api.example.com/reports"
            files = [
                ("title", (None, "Report")),
                ("file", ("report.pdf", open("/data/report.pdf", "rb"), "application/pdf")),
            ]

            response = requests.request("POST", url, files=files)

            print(response.status_code)
            print(response.text)
            """");

    [Fact]
    public void File_Curl() =>
        CodeSnippetGenerator.Generate(FileRequest, SnippetLanguage.Curl).ShouldBe(
            """"
            curl --request PUT https://api.example.com/blobs/1 \
              --location \
              --header 'Content-Type: application/octet-stream' \
              --data-binary @/data/blob.bin
            """");

    [Fact]
    public void File_CurlWindows() =>
        CodeSnippetGenerator.Generate(FileRequest, SnippetLanguage.CurlWindows).ShouldBe(
            """"
            curl --request ^"PUT^" ^"https://api.example.com/blobs/1^" ^
              --location ^
              --header ^"Content-Type: application/octet-stream^" ^
              --data-binary ^"^@/data/blob.bin^"
            """");

    [Fact]
    public void File_PowerShell() =>
        CodeSnippetGenerator.Generate(FileRequest, SnippetLanguage.PowerShell).ShouldBe(
            """"
            $params = @{
                Uri = 'https://api.example.com/blobs/1'
                Method = 'Put'
                ContentType = 'application/octet-stream'
                InFile = '/data/blob.bin'
            }

            Invoke-RestMethod @params
            """");

    [Fact]
    public void File_CSharp() =>
        CodeSnippetGenerator.Generate(FileRequest, SnippetLanguage.CSharp).ShouldBe(
            """"
            using System.Net.Http.Headers;

            using var client = new HttpClient();

            using var request = new HttpRequestMessage(HttpMethod.Put, "https://api.example.com/blobs/1");
            var content = new StreamContent(File.OpenRead("/data/blob.bin"));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            request.Content = content;

            using var response = await client.SendAsync(request);
            Console.WriteLine((int)response.StatusCode);
            Console.WriteLine(await response.Content.ReadAsStringAsync());
            """");

    [Fact]
    public void File_JavaScript() =>
        CodeSnippetGenerator.Generate(FileRequest, SnippetLanguage.JavaScript).ShouldBe(
            """"
            import { openAsBlob } from "node:fs";

            const response = await fetch("https://api.example.com/blobs/1", {
              method: "PUT",
              headers: {
                "Content-Type": "application/octet-stream",
              },
              body: await openAsBlob("/data/blob.bin"),
            });

            console.log(response.status);
            console.log(await response.text());
            """");

    [Fact]
    public void File_Python() =>
        CodeSnippetGenerator.Generate(FileRequest, SnippetLanguage.Python).ShouldBe(
            """"
            import requests

            url = "https://api.example.com/blobs/1"
            headers = {
                "Content-Type": "application/octet-stream",
            }

            with open("/data/blob.bin", "rb") as payload:
                response = requests.request("PUT", url, headers=headers, data=payload)

            print(response.status_code)
            print(response.text)
            """");
}
