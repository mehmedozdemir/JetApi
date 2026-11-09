namespace JetApi.Cli.Tests;

public sealed class CliParserTests
{
    [Fact]
    public void Run_ReadsEveryOption()
    {
        var result = CliParser.Parse([
            "run", "./api", "users/admin", "-e", "Development", "--data=rows.csv", "-n", "3", "--delay", "250", "--bail",
            "--timeout", "5000", "--var", "token=abc=1", "--var", "base=http://x", "--junit", "out/junit.xml", "--html", "out/report.html",
            "--persist", "--no-color", "--lang", "tr",
        ]);

        result.Error.ShouldBeNull();
        result.Command.ShouldBe(CliCommand.Run);
        var options = result.Options;
        options.Workspace.ShouldBe("./api");
        options.Target.ShouldBe("users/admin");
        options.Environment.ShouldBe("Development");
        options.DataFile.ShouldBe("rows.csv");
        options.Iterations.ShouldBe(3);
        options.DelayMs.ShouldBe(250);
        options.Bail.ShouldBeTrue();
        options.TimeoutMs.ShouldBe(5000);
        options.Variables.ShouldBe([new("token", "abc=1"), new("base", "http://x")]);
        options.JUnitPath.ShouldBe("out/junit.xml");
        options.HtmlPath.ShouldBe("out/report.html");
        options.Persist.ShouldBeTrue();
        options.NoColor.ShouldBeTrue();
        options.Language.ShouldBe("tr");
    }

    [Fact]
    public void Run_HasDefaults()
    {
        var options = CliParser.Parse(["run", "."]).Options;

        options.Target.ShouldBeNull();
        options.Iterations.ShouldBe(1);
        options.TimeoutMs.ShouldBe(30_000);
        options.Bail.ShouldBeFalse();
    }

    [Theory]
    [InlineData(new string[0], "Help")]
    [InlineData(new[] { "--help" }, "Help")]
    [InlineData(new[] { "run", ".", "-h" }, "Help")]
    [InlineData(new[] { "--version" }, "Version")]
    public void HelpAndVersion(string[] args, string command)
    {
        CliParser.Parse(args).Command.ToString().ShouldBe(command);
    }

    [Theory]
    [InlineData(new[] { "test" }, "Unknown command 'test'. Use 'jetapi run <workspace>'.")]
    [InlineData(new[] { "run" }, "Give the workspace folder: jetapi run <workspace>.")]
    [InlineData(new[] { "run", ".", "a", "b" }, "Unexpected argument 'b'.")]
    [InlineData(new[] { "run", ".", "--fast" }, "Unknown option '--fast'.")]
    [InlineData(new[] { "run", ".", "--env" }, "'--env' needs a value.")]
    [InlineData(new[] { "run", ".", "-n", "0" }, "'-n' needs a whole number of at least 1, not '0'.")]
    [InlineData(new[] { "run", ".", "--delay", "-5" }, "'--delay' needs a whole number of at least 0, not '-5'.")]
    [InlineData(new[] { "run", ".", "--delay=x" }, "'--delay' needs a whole number of at least 0, not 'x'.")]
    [InlineData(new[] { "run", ".", "--var", "a b=2" }, "'--var' needs name=value with a valid variable name, not 'a b=2'.")]
    [InlineData(new[] { "run", ".", "--lang", "de" }, "'--lang' is 'en' or 'tr', not 'de'.")]
    public void Errors_AreClear(string[] args, string error)
    {
        CliParser.Parse(args).Error.ShouldBe(error);
    }
}
