using System.Globalization;
using JetApi.Core.Model;
using JetApi.Http;

namespace JetApi.Import.Tests;

public sealed class CurlImporterTests
{
    private static HttpRequestDefinition Import(string command, out IReadOnlyList<ImportIssue> issues)
    {
        var result = CurlImporter.Import(command);
        result.Error.ShouldBeNull();
        issues = result.Issues;
        return result.Value.ShouldNotBeNull();
    }

    private static HttpRequestDefinition Import(string command)
    {
        var request = Import(command, out var issues);
        issues.ShouldBeEmpty();
        return request;
    }

    private static KeyValueEntry Entry(string key, string value) => new(key, value);

    [Theory]
    [InlineData("curl https://x.test", true)]
    [InlineData("  CURL.EXE -s https://x.test", true)]
    [InlineData("/usr/bin/curl https://x.test", true)]
    [InlineData("C:\\tools\\curl.exe https://x.test", true)]
    [InlineData("curl\n  https://x.test", true)]
    [InlineData("curly https://x.test", false)]
    [InlineData("wget https://x.test", false)]
    [InlineData("https://x.test", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeCurl_RecognizesTheProgramName(string? text, bool expected) => CurlImporter.LooksLikeCurl(text).ShouldBe(expected);

    [Fact]
    public void Import_RejectsOtherText() =>
        CurlImporter.Import("wget https://x.test").Error.ShouldBe("This is not a cURL command. Paste a command that starts with curl.");

    [Fact]
    public void Import_RejectsAnUnclosedQuote() =>
        CurlImporter.Import("curl 'https://x.test").Error.ShouldBe("The command has a quote that is never closed. Copy the whole command and try again.");

    [Fact]
    public void Import_NeedsAUrl() => CurlImporter.Import("curl -X POST -d a=1").Error.ShouldBe("The cURL command has no URL.");

    // Browsers

    [Fact]
    public void ChromeBash_GetWithHeaders()
    {
        var request = Import(
            """
            curl 'https://api.github.com/repos/dotnet/runtime/issues?state=open&per_page=5' \
              -H 'accept: application/json' \
              -H 'accept-language: en-US,en;q=0.9,tr;q=0.8' \
              -H 'authorization: Bearer ghp_abc123' \
              -H 'sec-ch-ua: "Chromium";v="130", "Google Chrome";v="130"' \
              -H 'user-agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64)'
            """);

        request.Name.ShouldBe("GET /repos/dotnet/runtime/issues");
        request.Key.ShouldBe("get-repos-dotnet-runtime-issues");
        request.Method.ShouldBe("GET");
        request.Url.ShouldBe("https://api.github.com/repos/dotnet/runtime/issues");
        request.QueryParameters.ShouldBe([Entry("state", "open"), Entry("per_page", "5")]);
        request.Headers.ShouldBe(
        [
            Entry("accept", "application/json"),
            Entry("accept-language", "en-US,en;q=0.9,tr;q=0.8"),
            Entry("sec-ch-ua", "\"Chromium\";v=\"130\", \"Google Chrome\";v=\"130\""),
            Entry("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"),
        ]);
        request.Auth.ShouldBe(new BearerAuth("ghp_abc123"));
        request.Body.ShouldBe(HttpBody.Empty);

        // curl does not follow redirects unless told to.
        request.Settings.ShouldBe(new HttpRequestSettings(FollowRedirects: false));
    }

    [Fact]
    public void ChromeBash_AnsiCQuotedJson()
    {
        var request = Import(
            """
            curl 'https://example.com/api/comments' \
              -H 'content-type: application/json' \
              --data-raw $'{"text":"it\'s fine\\nreally","tab":"a\tb","emoji":"\u00fc \U0001F680","hex":"\x41"}' \
              --compressed
            """);

        request.Method.ShouldBe("POST");
        request.Headers.ShouldBeEmpty();
        request.Body.ShouldBe(HttpBody.Json("{\"text\":\"it's fine\\nreally\",\"tab\":\"a\tb\",\"emoji\":\"ü 🚀\",\"hex\":\"A\"}"));
    }

    [Fact]
    public void ChromeCmd_CaretEscapes()
    {
        // Produced with the escaping of Chrome's "Copy as cURL (cmd)".
        var request = Import(
            """
            curl ^"https://shop.example.com/api/comments?draft=1^&lang=tr^" ^
              -H ^"accept: */*^" ^
              -H ^"content-type: application/json^" ^
              -H ^"sec-ch-ua: ^\^"Chromium^\^";v=^\^"130^\^", ^\^"Not?A_Brand^\^";v=^\^"99^\^"^" ^
              --data-raw ^"^{^\^"text^\^":^\^"50^% off ^& ^<b^>more^</b^> for ^%^USERNAME^%^\^",^\^"note^\^":^\^"line1^

            line2^\^",^\^"quote^\^":^\^"say ^\^\^\^"hi^\^\^\^"^\^"^}^"
            """);

        request.Method.ShouldBe("POST");
        request.Url.ShouldBe("https://shop.example.com/api/comments");
        request.QueryParameters.ShouldBe([Entry("draft", "1"), Entry("lang", "tr")]);
        request.Headers.ShouldBe([Entry("accept", "*/*"), Entry("sec-ch-ua", "\"Chromium\";v=\"130\", \"Not?A_Brand\";v=\"99\"")]);
        request.Body.ShouldBe(HttpBody.Json("{\"text\":\"50% off & <b>more</b> for %USERNAME%\",\"note\":\"line1\nline2\",\"quote\":\"say \\\"hi\\\"\"}"));
    }

    [Fact]
    public void ChromeCmd_WindowsLineEndings() =>
        Import("curl ^\"https://x.test/a^\" ^\r\n  -H ^\"x-a: 1^\" ^\r\n  --data-raw ^\"one^\r\n\r\ntwo^\"").Body.Content.ShouldBe("one\ntwo");

    [Fact]
    public void FirefoxBash_FormWithCharset()
    {
        var request = Import(
            "curl 'https://www.example.com/search' --compressed -X POST -H 'User-Agent: Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0' "
            + "-H 'Accept: */*' -H 'Content-Type: application/x-www-form-urlencoded; charset=UTF-8' -H 'Origin: https://www.example.com' "
            + "-H 'Connection: keep-alive' --data-raw 'q=hello+world&lang=tr&filter=a%26b'");

        request.Method.ShouldBe("POST");
        request.Body.ShouldBeForm([Entry("q", "hello world"), Entry("lang", "tr"), Entry("filter", "a&b")]);

        // Not the type JetApi sends by default, so the header stays and wins.
        request.Headers.ShouldContain(Entry("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8"));
        request.Headers.Count.ShouldBe(5);
    }

    // Other shells and tools

    [Fact]
    public void PowerShell_BacktickContinuationsAndVariables()
    {
        var request = Import(
            """
            curl.exe -X POST `
              -H "Content-Type: application/json" `
              -H "Authorization: Bearer $env:API_TOKEN" `
              -d '{"name":"Ada","quote":"it''s"}' `
              "https://api.example.com/users?tenant=$Tenant"
            """, out var issues);

        request.Method.ShouldBe("POST");
        request.Url.ShouldBe("https://api.example.com/users");
        request.QueryParameters.ShouldBe([Entry("tenant", "{{Tenant}}")]);
        request.Auth.ShouldBe(new BearerAuth("{{API_TOKEN}}"));
        request.Body.ShouldBe(HttpBody.Json("{\"name\":\"Ada\",\"quote\":\"it's\"}"));
        issues.ShouldHaveSingleItem().Message.ShouldBe("Shell variables became JetApi variables: API_TOKEN, Tenant. Define them in an environment.");
    }

    [Fact]
    public void PowerShell_EscapesInDoubleQuotes() =>
        Import("& curl.exe -H \"X-Note: say `\"hi`\" for `$5\" https://x.test").Headers.ShouldBe([Entry("X-Note", "say \"hi\" for $5")]);

    [Fact]
    public void Postman_ExportWithPrettyJson()
    {
        var request = Import(
            """
            curl --location 'https://api.example.com/v1/orders?status=open' \
            --header 'Content-Type: application/json' \
            --header 'Authorization: Basic YWRtaW46c2VjcmV0' \
            --data '{
                "item": "book",
                "qty": 2
            }'
            """);

        request.Name.ShouldBe("POST /v1/orders");
        request.Settings.FollowRedirects.ShouldBeTrue();
        request.Auth.ShouldBe(new BasicAuth("admin", "secret"));
        request.Headers.ShouldBeEmpty();
        request.Body.ShouldBe(HttpBody.Json("{\n    \"item\": \"book\",\n    \"qty\": 2\n}"));
    }

    [Fact]
    public void Postman_OlderExportWithRequestAndDataRaw()
    {
        var request = Import("curl --location --request PUT '{{baseUrl}}/notes/{{noteId}}' --header 'Content-Type: text/plain' --data-raw 'hello'");

        request.Name.ShouldBe("PUT /notes/{{noteId}}");
        request.Key.ShouldBe("put-notes-noteid");
        request.Url.ShouldBe("{{baseUrl}}/notes/{{noteId}}");
        request.Body.ShouldBe(HttpBody.Text("hello"));
        request.Headers.ShouldBe([Entry("Content-Type", "text/plain")]);
    }

    [Fact]
    public void Documentation_CombinedShortFlags()
    {
        var request = Import("curl -sSLk -u admin:p@ss:word -X DELETE \"https://api.example.com/items/42\" -m 7.5");

        request.Method.ShouldBe("DELETE");
        request.Auth.ShouldBe(new BasicAuth("admin", "p@ss:word"));
        request.Settings.ShouldBe(new HttpRequestSettings(FollowRedirects: true, ValidateCertificates: false, Timeout: TimeSpan.FromSeconds(7.5)));
    }

    [Fact]
    public void ShortOptions_TakeAttachedValues()
    {
        var request = Import("curl -XPATCH -HContent-Type:application/json -d'{\"a\":1}' localhost:8080/api");

        request.Method.ShouldBe("PATCH");
        request.Url.ShouldBe("localhost:8080/api");
        request.Name.ShouldBe("PATCH /api");
        request.Body.ShouldBe(HttpBody.Json("{\"a\":1}"));
    }

    // Bodies

    [Fact]
    public void Data_WithoutContentType_IsAFormLikeCurlSendsIt()
    {
        var request = Import("curl https://x.test/people -d 'name=Ada%20Lovelace&age=36' -d city=London");

        request.Method.ShouldBe("POST");
        request.Body.ShouldBeForm([Entry("name", "Ada Lovelace"), Entry("age", "36"), Entry("city", "London")]);
        request.Headers.ShouldBeEmpty();
    }

    [Fact]
    public void Data_WithAFormContentType_DropsTheImpliedHeader() =>
        Import("curl https://x.test -H 'Content-Type: application/x-www-form-urlencoded' -d 'a=1'").Headers.ShouldBeEmpty();

    [Fact]
    public void Data_ThatIsNotAForm_KeepsCurlsContentType()
    {
        var request = Import("curl https://x.test -d 'just some text'");

        request.Body.ShouldBe(HttpBody.Text("just some text"));
        request.Headers.ShouldBe([Entry("Content-Type", "application/x-www-form-urlencoded")]);
    }

    [Fact]
    public void Data_ThatIsJsonWithoutContentType_BecomesJson()
    {
        var request = Import("curl https://x.test -d '{\"a\": \"b=c\"}'", out var issues);

        request.Body.ShouldBe(HttpBody.Json("{\"a\": \"b=c\"}"));
        issues.ShouldHaveSingleItem().Severity.ShouldBe(ImportIssueSeverity.Info);
    }

    [Theory]
    [InlineData("application/xml", HttpBodyMode.Xml, 0)]
    [InlineData("text/xml; charset=utf-8", HttpBodyMode.Xml, 1)]
    [InlineData("application/vnd.api+json", HttpBodyMode.Json, 1)]
    [InlineData("application/json", HttpBodyMode.Json, 0)]
    [InlineData("text/csv", HttpBodyMode.Text, 1)]
    public void Data_BodyModeFollowsTheContentType(string contentType, HttpBodyMode mode, int headersKept)
    {
        var request = Import($"curl https://x.test -H 'Content-Type: {contentType}' --data-binary '<a>1</a>'");

        request.Body.Mode.ShouldBe(mode);
        request.Body.Content.ShouldBe("<a>1</a>");
        request.Headers.Count.ShouldBe(headersKept);
    }

    [Fact]
    public void DataUrlEncode_EncodesTheValue() =>
        Import("curl https://x.test --data-urlencode 'comment=a+b & c=d' --data-urlencode 'city=İzmir'").Body
            .ShouldBeForm([Entry("comment", "a+b & c=d"), Entry("city", "İzmir")]);

    [Fact]
    public void DataUrlEncode_WithoutAName_SendsTheEncodedTextAsIs()
    {
        var request = Import("curl https://x.test --data-urlencode '=a b&c'");

        request.Body.ShouldBe(HttpBody.Text("a%20b%26c"));
        request.Headers.ShouldBe([Entry("Content-Type", "application/x-www-form-urlencoded")]);
    }

    [Fact]
    public void Get_MovesDataToTheQuery()
    {
        var request = Import("curl -G https://x.test/search --data-urlencode 'q=hello world' -d limit=10");

        request.Method.ShouldBe("GET");
        request.Url.ShouldBe("https://x.test/search");
        request.QueryParameters.ShouldBe([Entry("q", "hello world"), Entry("limit", "10")]);
        request.Body.ShouldBe(HttpBody.Empty);
    }

    [Fact]
    public void UrlQuery_AddsEncodedParameters() =>
        Import("curl 'https://x.test/a?x=1' --url-query 'name=Ada L'").QueryParameters.ShouldBe([Entry("x", "1"), Entry("name", "Ada L")]);

    [Fact]
    public void Json_SetsJsonHeaders()
    {
        var request = Import("curl --json '{\"a\":1}' https://x.test/items");

        request.Method.ShouldBe("POST");
        request.Body.ShouldBe(HttpBody.Json("{\"a\":1}"));
        request.Headers.ShouldBe([Entry("Accept", "application/json")]);
    }

    [Fact]
    public void Json_KeepsAnExplicitAccept() =>
        Import("curl --json '{}' -H 'Accept: application/problem+json' https://x.test").Headers.ShouldBe([Entry("Accept", "application/problem+json")]);

    [Fact]
    public void Form_TextAndFileParts()
    {
        var request = Import(
            "curl https://x.test/upload -F 'name=Ada' -F 'avatar=@/home/ada/me.png;type=image/png' -F 'doc=@\"my;file.pdf\";filename=x.pdf' "
            + "--form-string 'raw=@not-a-file;type=x' -F 'meta=\"{\\\"a\\\":1}\";type=application/json' -H 'Content-Type: multipart/form-data'",
            out var issues);

        request.Method.ShouldBe("POST");
        request.Headers.ShouldBeEmpty();
        request.Body.Mode.ShouldBe(HttpBodyMode.Multipart);
        request.Body.MultipartFields.ShouldBe(
        [
            new MultipartField("name", "Ada"),
            new MultipartField("avatar", "/home/ada/me.png", MultipartFieldKind.File, ContentType: "image/png"),
            new MultipartField("doc", "my;file.pdf", MultipartFieldKind.File),
            new MultipartField("raw", "@not-a-file;type=x"),
            new MultipartField("meta", "{\"a\":1}", ContentType: "application/json"),
        ]);
        issues.Select(i => i.Message).ShouldBe(
        [
            "Uses the file \"/home/ada/me.png\"; make sure it exists on this machine. Relative paths are resolved from the workspace folder.",
            "Uses the file \"my;file.pdf\"; make sure it exists on this machine. Relative paths are resolved from the workspace folder.",
        ]);
    }

    [Fact]
    public void Form_ValueFromAFile_BecomesAFileUpload()
    {
        var request = Import("curl https://x.test -F 'notes=<notes.txt'", out var issues);

        request.Body.MultipartFields.ShouldBe([new MultipartField("notes", "notes.txt", MultipartFieldKind.File)]);
        issues.Count.ShouldBe(2);
    }

    [Fact]
    public void DataFromAFile_BecomesAFileBody()
    {
        var request = Import("curl https://x.test/blobs -H 'Content-Type: application/octet-stream' --data-binary @payload.bin", out var issues);

        request.Method.ShouldBe("POST");
        request.Body.ShouldBe(new HttpBody { Mode = HttpBodyMode.File, FilePath = "payload.bin" });
        request.Headers.ShouldBeEmpty();
        issues.ShouldHaveSingleItem().Severity.ShouldBe(ImportIssueSeverity.Info);
    }

    [Fact]
    public void UploadFile_IsAPutWithAFileBody()
    {
        var request = Import("curl -T ./report.csv -H 'Content-Type: text/csv' https://x.test/reports/1", out _);

        request.Method.ShouldBe("PUT");
        request.Body.ShouldBe(new HttpBody { Mode = HttpBodyMode.File, FilePath = "./report.csv" });
        request.Headers.ShouldBe([Entry("Content-Type", "text/csv")]);
    }

    [Theory]
    [InlineData("curl https://x.test -d @-", "-")]
    [InlineData("curl https://x.test -d a=1 -d @extra.txt", "extra.txt")]
    [InlineData("curl https://x.test -H @headers.txt", "headers.txt")]
    [InlineData("curl https://x.test -b cookies.txt", "cookies.txt")]
    [InlineData("curl https://x.test --data-urlencode name@file.txt", "file.txt")]
    public void FilesThatCannotBeRead_AreReported(string command, string file)
    {
        Import(command, out var issues);

        issues.ShouldHaveSingleItem().Message.ShouldBe($"\"{file}\" is read from a file or standard input, which an import cannot do, so it was left out.");
    }

    [Fact]
    public void FilesThatCannotBeRead_LeaveTheRestOfTheData() =>
        Import("curl https://x.test -d a=1 -d @extra.txt", out _).Body.ShouldBeForm([Entry("a", "1")]);

    [Fact]
    public void DataAndForm_KeepsTheForm()
    {
        var request = Import("curl https://x.test -d a=1 -F b=2", out var issues);

        request.Body.MultipartFields.ShouldBe([new MultipartField("b", "2")]);
        issues.ShouldHaveSingleItem().Message.ShouldBe("The command has both form fields (-F) and data (-d); only the form fields were imported.");
    }

    // Headers, auth, settings

    [Fact]
    public void ShortcutOptions_BecomeHeaders()
    {
        var request = Import("curl https://x.test -A 'JetApi/1.0' -e 'https://ref.test;auto' -b 'a=1; b=2' -b 'c=3' -r 0-99 -H 'X-Empty;' -H 'Host:'");

        request.Headers.ShouldBe(
        [
            Entry("User-Agent", "JetApi/1.0"),
            Entry("Referer", "https://ref.test"),
            Entry("Range", "bytes=0-99"),
            Entry("X-Empty", string.Empty),
            Entry("Cookie", "a=1; b=2; c=3"),
        ]);
    }

    [Fact]
    public void OAuth2Bearer_BecomesBearerAuth() => Import("curl --oauth2-bearer abc.def https://x.test").Auth.ShouldBe(new BearerAuth("abc.def"));

    [Fact]
    public void NoCredentials_InheritAuth() => Import("curl https://x.test").Auth.ShouldBe(AuthConfig.Inherit);

    [Fact]
    public void AuthorizationHeaders_ThatAreNotBearerOrBasic_StayHeaders()
    {
        var request = Import("curl https://x.test -H 'Authorization: Digest username=\"a\"'");

        request.Auth.ShouldBe(AuthConfig.Inherit);
        request.Headers.ShouldBe([Entry("Authorization", "Digest username=\"a\"")]);
    }

    [Fact]
    public void UserWithoutPassword_IsReported()
    {
        var request = Import("curl -u ada https://x.test", out var issues);

        request.Auth.ShouldBe(new BasicAuth("ada", string.Empty));
        issues.ShouldHaveSingleItem().Message.ShouldBe("No password was given for user \"ada\" (curl would ask for it). Enter it on the Auth tab.");
    }

    [Fact]
    public void OtherAuthSchemes_AreNotImported()
    {
        var request = Import("curl --digest -u ada:secret https://x.test", out var issues);

        request.Auth.ShouldBe(AuthConfig.None);
        issues.ShouldHaveSingleItem().Message.ShouldBe("--digest authentication is not supported yet, so the credentials were not imported.");
    }

    [Fact]
    public void Head_UsesHead() => Import("curl -I https://x.test").Method.ShouldBe("HEAD");

    [Fact]
    public void Options_AreReportedOrIgnoredByKind()
    {
        var request = Import(
            "curl --proxy http://proxy:8080 --frobnicate -Wq --connect-timeout 3 -o out.json --http2 -m soon https://x.test/a https://x.test/b",
            out var issues);

        // Values of known options are not mistaken for the URL.
        request.Url.ShouldBe("https://x.test/a");
        issues.Select(i => i.Message).ShouldBe(
        [
            "Option --proxy has no equivalent in JetApi yet and was ignored.",
            "Option --frobnicate is not known and was ignored.",
            "Option -W is not known and was ignored.",
            "The timeout \"soon\" is not a number of seconds and was ignored.",
            "Only the first URL was imported; https://x.test/b was ignored.",
        ]);
    }

    [Fact]
    public void AMissingValue_IsReported()
    {
        Import("curl https://x.test -H", out var issues);

        issues.ShouldHaveSingleItem().Message.ShouldBe("Option -H has no value and was ignored.");
    }

    // URLs and shell syntax

    [Theory]
    [InlineData("https://x.test/a?q=a+b", "https://x.test/a?q=a+b")]
    [InlineData("https://x.test/a?debug", "https://x.test/a?debug")]
    [InlineData("https://x.test/a?bad=%FF", "https://x.test/a?bad=%FF")]
    public void Queries_ThatCannotBeSplitLosslessly_StayInTheUrl(string url, string expected)
    {
        var request = Import("curl '" + url + "'");

        request.Url.ShouldBe(expected);
        request.QueryParameters.ShouldBeEmpty();
    }

    [Fact]
    public void Queries_AreDecodedAndKeepPlaceholders()
    {
        var request = Import("curl '{{baseUrl}}/users/{{id}}?name=%C3%9Cmit%20Y&token={{token}}&empty=#top'");

        request.Url.ShouldBe("{{baseUrl}}/users/{{id}}#top");
        request.QueryParameters.ShouldBe([Entry("name", "Ümit Y"), Entry("token", "{{token}}"), Entry("empty", string.Empty)]);
        request.Name.ShouldBe("GET /users/{{id}}");
    }

    [Fact]
    public void BashVariables_BecomePlaceholders()
    {
        var request = Import("curl -H \"Authorization: Bearer $TOKEN\" \"${BASE_URL}/me\" -H 'X-Literal: $NOT'", out var issues);

        request.Url.ShouldBe("{{BASE_URL}}/me");
        request.Auth.ShouldBe(new BearerAuth("{{TOKEN}}"));
        request.Headers.ShouldBe([Entry("X-Literal", "$NOT")]);
        issues.ShouldHaveSingleItem().Message.ShouldContain("TOKEN, BASE_URL");
    }

    [Fact]
    public void Pipes_AndRedirections_EndTheCommand() =>
        Import("curl -s https://x.test/a | jq '.items' > out.json").Url.ShouldBe("https://x.test/a");

    [Fact]
    public void Bash_BackslashesAndDoubleQuotes() =>
        Import("curl \"https://x.test/a\" -H \"X-A: \\\"q\\\" \\$HOME \\\\ \\n\" -H X-B:\\ spaced\\'s").Headers
            .ShouldBe([Entry("X-A", "\"q\" $HOME \\ \\n"), Entry("X-B", "spaced's")]);

    [Fact]
    public void Messages_FollowTheUiLanguage()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");

            CurlImporter.Import("curl -X POST").Error.ShouldBe("cURL komutunda URL yok.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    // Round trip with the snippet generator

    public static TheoryData<string, SnippetLanguage> RoundTrips()
    {
        var data = new TheoryData<string, SnippetLanguage>();
        foreach (var name in new[] { "get", "json", "text", "xml", "form", "multipart", "file", "head", "custom" })
        {
            data.Add(name, SnippetLanguage.Curl);
            data.Add(name, SnippetLanguage.CurlWindows);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RoundTrips))]
    public void ExportedCurl_ImportsAsTheSameRequest(string name, SnippetLanguage language)
    {
        var tricky = "it's \"quoted\" ^ & | < > % %PATH% $HOME `x` \\ \\\" ‘ü’ 🚀";
        var original = new HttpRequestDefinition
        {
            Key = "x",
            Name = "x",
            Method = name switch { "get" => "GET", "head" => "HEAD", "custom" => "PURGE", "file" => "PUT", _ => "POST" },
            Url = "https://api.example.com/v1/items",
            QueryParameters = [Entry("q", tricky), Entry("page", "2")],
            Headers = [Entry("X-Note", "a 'b' \"c\" ^d^ %e%"), Entry("Accept", "application/json")],
            Body = name switch
            {
                "json" => HttpBody.Json("{\n  \"text\": \"" + tricky.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\",\n  \"n\": 1\n}"),
                "text" => HttpBody.Text("line 1\nline 2 " + tricky),
                "xml" => HttpBody.Xml("<a b=\"1\">&amp; " + tricky + "</a>"),
                "form" => HttpBody.Form([Entry("user name", tricky), Entry("plus", "a+b=c&d"), Entry("empty", string.Empty)]),
                "multipart" => new HttpBody
                {
                    Mode = HttpBodyMode.Multipart,
                    MultipartFields =
                    [
                        new MultipartField("title", "My \"report\"; final"),
                        new MultipartField("meta", "{\"a\":\"x;y\"}", ContentType: "application/json"),
                        new MultipartField("file", "{{path}}", MultipartFieldKind.File, ContentType: "text/plain"),
                    ],
                },
                "file" => new HttpBody { Mode = HttpBodyMode.File, FilePath = "{{path}}" },
                _ => HttpBody.Empty,
            },
            Settings = new HttpRequestSettings(FollowRedirects: name != "head", ValidateCertificates: name != "xml", Timeout: name == "json" ? TimeSpan.FromSeconds(1.5) : null),
            Auth = new BasicAuth("ada", "s3cr:t ü"),
        };
        var directory = Directory.CreateTempSubdirectory("jetapi-curl-");
        try
        {
            var path = Path.Combine(directory.FullName, "my file;v1.txt");
            File.WriteAllText(path, "hello");
            var variables = new JetApi.Core.Variables.VariableResolver(
                [new JetApi.Core.Variables.VariableLayer(JetApi.Core.Variables.VariableSource.Environment, [new Variable("path", path)])]);
            var prepared = HttpRequestPreparer.Prepare(original, original.Auth, variables, directory.FullName).Request.ShouldNotBeNull();

            var imported = Import(CodeSnippetGenerator.Generate(prepared, language), out var issues);

            imported.Method.ShouldBe(original.Method);
            imported.Url.ShouldBe(original.Url);
            imported.QueryParameters.ShouldBe(original.QueryParameters);
            imported.Headers.ShouldBe(original.Headers);
            imported.Auth.ShouldBe(original.Auth);
            imported.Settings.ShouldBe(original.Settings);
            var expectedBody = original.Body.Mode switch
            {
                HttpBodyMode.File => original.Body with { FilePath = path },
                HttpBodyMode.Multipart => original.Body with { MultipartFields = [.. original.Body.MultipartFields.Select(f => f.Kind == MultipartFieldKind.File ? f with { Value = path } : f)] },
                _ => original.Body,
            };
            imported.Body.Mode.ShouldBe(expectedBody.Mode);
            imported.Body.Content.ShouldBe(expectedBody.Content);
            imported.Body.FormFields.ShouldBe(expectedBody.FormFields);
            imported.Body.MultipartFields.ShouldBe(expectedBody.MultipartFields);
            imported.Body.FilePath.ShouldBe(expectedBody.FilePath);
            issues.ShouldAllBe(issue => !issue.IsWarning);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}

internal static class HttpBodyAssertions
{
    // Records compare lists by reference, so form bodies are compared field by field.
    public static void ShouldBeForm(this HttpBody body, IReadOnlyList<KeyValueEntry> fields)
    {
        body.Mode.ShouldBe(HttpBodyMode.FormUrlEncoded);
        body.FormFields.ShouldBe(fields);
    }
}
