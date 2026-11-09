namespace JetApi.Runner.Tests;

public sealed class DataFileTests
{
    [Fact]
    public void Csv_WithQuotesSeparatorsAndLineBreaks()
    {
        var result = DataFile.ParseCsv("﻿name,email,note\r\nAyşe,ayse@example.com,\"Hello, world\"\r\n\"Ali \"\"K\"\"\",ali@example.com,\"two\nlines\"\r\n\r\n");

        result.Error.ShouldBeNull();
        result.Columns.ShouldBe(["name", "email", "note"]);
        result.Rows.Count.ShouldBe(2);
        result.Rows[0]["note"].ShouldBe("Hello, world");
        result.Rows[1]["name"].ShouldBe("Ali \"K\"");
        result.Rows[1]["note"].ShouldBe("two\nlines");
    }

    [Fact]
    public void Csv_FromTurkishExcel_UsesSemicolons_AndFillsMissingValues()
    {
        var result = DataFile.ParseCsv("ad;e-posta;tutar\nAyşe;ayse@example.com;1,50\nAli\n");

        result.Columns.ShouldBe(["ad", "e-posta", "tutar"]);
        result.Rows[0]["tutar"].ShouldBe("1,50");
        result.Rows[1]["e-posta"].ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData("name\n\"open,1\n", "A quote opened on line 2 of the CSV file is never closed.")]
    [InlineData("name,,x\n1,2,3\n", "The first line of the CSV file must name every column.")]
    [InlineData("name,email\n", "The data file has no rows.")]
    [InlineData("", "The data file has no rows.")]
    public void Csv_Problems_AreExplained(string text, string error) => DataFile.ParseCsv(text).Error.ShouldBe(error);

    [Fact]
    public void Json_ArrayOfObjects_StringsAsTheyAre_OtherValuesAsJson()
    {
        var result = DataFile.ParseJson("""[{"email": "a@b.c", "age": 30, "tags": ["x"]}, {"email": "d@e.f", "vip": true}]""");

        result.Columns.ShouldBe(["email", "age", "tags", "vip"]);
        result.Rows[0]["email"].ShouldBe("a@b.c");
        result.Rows[0]["age"].ShouldBe("30");
        result.Rows[0]["tags"].ShouldBe("[\"x\"]");
        result.Rows[1]["vip"].ShouldBe("true");
    }

    [Theory]
    [InlineData("{\"email\": \"a\"}", "A JSON data file must be an array of objects, e.g. [{\"email\": \"a@b.c\"}].")]
    [InlineData("[1, 2]", "Item 1 of the JSON data file is not an object.")]
    [InlineData("[]", "The data file has no rows.")]
    [InlineData("[{\"a\": }]", "The JSON data file is not valid (line 1, column 8).")]
    public void Json_Problems_AreExplained(string text, string error) => DataFile.ParseJson(text).Error.ShouldBe(error);

    [Fact]
    public async Task Load_PicksTheFormatFromTheExtension_AndReportsMissingFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "jetapi-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "users.csv"), "email\na@b.c\n", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder, "users.json"), "[{\"email\": \"d@e.f\"}]", TestContext.Current.CancellationToken);

            (await DataFile.LoadAsync(Path.Combine(folder, "users.csv"), TestContext.Current.CancellationToken)).Rows[0]["email"].ShouldBe("a@b.c");
            (await DataFile.LoadAsync(Path.Combine(folder, "users.json"), TestContext.Current.CancellationToken)).Rows[0]["email"].ShouldBe("d@e.f");
            (await DataFile.LoadAsync(Path.Combine(folder, "missing.csv"), TestContext.Current.CancellationToken)).Error.ShouldStartWith("The data file could not be read: ");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
