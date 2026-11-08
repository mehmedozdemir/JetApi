using JetApi.Core.Model;
using JetApi.Core.Variables;
using Microsoft.Extensions.Time.Testing;

namespace JetApi.Core.Tests.Variables;

public sealed class VariableResolverTests
{
    private static VariableResolver CreateResolver(params (VariableSource Source, Variable[] Variables)[] layers) =>
        new(layers.Select(layer => new VariableLayer(layer.Source, layer.Variables)));

    [Fact]
    public void Resolve_SubstitutesVariables()
    {
        var resolver = CreateResolver((VariableSource.Environment, [new("baseUrl", "https://api.test"), new("id", "42")]));

        var result = resolver.Resolve("{{baseUrl}}/users/{{ id }}");

        result.Value.ShouldBe("https://api.test/users/42");
        result.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void Resolve_HigherPrecedenceLayerWins_RegardlessOfOrder()
    {
        var resolver = CreateResolver(
            (VariableSource.Runtime, [new("host", "runtime")]),
            (VariableSource.Global, [new("host", "global")]),
            (VariableSource.Environment, [new("host", "env")]),
            (VariableSource.Collection, [new("host", "collection")]));

        resolver.Resolve("{{host}}").Value.ShouldBe("runtime");
    }

    [Fact]
    public void Resolve_IgnoresDisabledVariables()
    {
        var resolver = CreateResolver(
            (VariableSource.Global, [new("host", "global")]),
            (VariableSource.Environment, [new("host", "env", IsEnabled: false)]));

        resolver.Resolve("{{host}}").Value.ShouldBe("global");
    }

    [Fact]
    public void Resolve_IsCaseSensitive()
    {
        var resolver = CreateResolver((VariableSource.Global, [new("Token", "abc")]));

        var result = resolver.Resolve("{{token}}");

        result.Value.ShouldBe("{{token}}");
        result.Issues.ShouldBe([new VariableIssue("token", VariableIssueKind.Undefined)]);
    }

    [Fact]
    public void Resolve_ResolvesNestedVariables_AcrossLayers()
    {
        var resolver = CreateResolver(
            (VariableSource.Global, [new("scheme", "https")]),
            (VariableSource.Environment, [new("host", "api.test"), new("baseUrl", "{{scheme}}://{{host}}")]));

        resolver.Resolve("{{baseUrl}}/x").Value.ShouldBe("https://api.test/x");
    }

    [Fact]
    public void Resolve_KeepsUndefinedPlaceholders_AndReportsEachOnce()
    {
        var result = VariableResolver.Empty.Resolve("{{a}}-{{b}}-{{a}}");

        result.Value.ShouldBe("{{a}}-{{b}}-{{a}}");
        result.Issues.Select(issue => issue.Name).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void Resolve_DetectsCycles_InsteadOfLooping()
    {
        var resolver = CreateResolver((VariableSource.Environment, [new("a", "x{{b}}"), new("b", "y{{a}}")]));

        var result = resolver.Resolve("{{a}}");

        result.Value.ShouldBe("xy{{a}}");
        result.Issues.ShouldBe([new VariableIssue("a", VariableIssueKind.Cycle)]);
    }

    [Fact]
    public void Resolve_SelfReference_IsCycle()
    {
        var resolver = CreateResolver((VariableSource.Environment, [new("a", "{{a}}")]));

        resolver.Resolve("{{a}}").Issues.Single().Kind.ShouldBe(VariableIssueKind.Cycle);
    }

    [Theory]
    [InlineData("{{ not a name }}")]
    [InlineData("{{}}")]
    [InlineData("{{unterminated")]
    [InlineData("{\"json\": {\"nested\": 1}}")]
    public void Resolve_LeavesNonPlaceholderBracesUntouched(string text)
    {
        var result = VariableResolver.Empty.Resolve(text);

        result.Value.ShouldBe(text);
        result.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void Resolve_FindsPlaceholderAfterInvalidOne()
    {
        var resolver = CreateResolver((VariableSource.Global, [new("x", "1")]));

        resolver.Resolve("{{ bad name }} {{x}}").Value.ShouldBe("{{ bad name }} 1");
    }

    [Fact]
    public void Resolve_NullOrEmpty_ReturnsEmpty()
    {
        VariableResolver.Empty.Resolve(null).Value.ShouldBe(string.Empty);
        VariableResolver.Empty.Resolve(string.Empty).Value.ShouldBe(string.Empty);
    }

    [Fact]
    public void Resolve_DynamicVariables_UseInjectedClock()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero));
        var resolver = new VariableResolver([], new DynamicVariables(clock, new Random(1)));

        resolver.Resolve("{{$timestamp}}|{{$isoTimestamp}}").Value.ShouldBe("1767323045|2026-01-02T03:04:05.678Z");
        Guid.TryParse(resolver.Resolve("{{$guid}}").Value, out _).ShouldBeTrue();
        int.Parse(resolver.Resolve("{{$randomInt}}").Value, System.Globalization.CultureInfo.InvariantCulture).ShouldBeInRange(0, 999);
    }

    [Fact]
    public void Resolve_UnknownDynamicVariable_IsUndefined()
    {
        VariableResolver.Empty.Resolve("{{$nope}}").Issues.Single().ShouldBe(new VariableIssue("$nope", VariableIssueKind.Undefined));
    }

    [Fact]
    public void Lookup_ReportsSourceAndSecretFlag()
    {
        var resolver = CreateResolver(
            (VariableSource.Collection, [new("host", "h")]),
            (VariableSource.Environment, [new("token", "{{host}}-t", IsSecret: true)]));

        resolver.Lookup("token").ShouldBe(new VariableLookup("h-t", VariableSource.Environment, true));
        resolver.Lookup("missing").ShouldBeNull();
    }

    [Fact]
    public void FindReferences_ReturnsDistinctNamesInOrder() =>
        VariableResolver.FindReferences("{{b}} {{a}} {{b}} {{ bad name }}").ShouldBe(["b", "a"]);

    [Fact]
    public void FindPlaceholders_ReturnsPositionsIncludingRepeats()
    {
        var placeholders = VariableResolver.FindPlaceholders("x{{a}}-{{ b }}{{a}}");

        placeholders.ShouldBe(
        [
            new VariablePlaceholder(1, 5, "a"),
            new VariablePlaceholder(7, 7, "b"),
            new VariablePlaceholder(14, 5, "a"),
        ]);
        placeholders[1].End.ShouldBe(14);
    }

    [Fact]
    public void FindPlaceholders_SkipsInvalidAndUnclosed()
    {
        VariableResolver.FindPlaceholders("{{ bad name }} {{ok}} {{open").ShouldBe([new VariablePlaceholder(15, 6, "ok")]);
        VariableResolver.FindPlaceholders("{{{x}}").ShouldBe([new VariablePlaceholder(1, 5, "x")]);
        VariableResolver.FindPlaceholders(null).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void Placeholder_ContainsCoversBraces(int index, bool expected) =>
        new VariablePlaceholder(1, 5, "a").Contains(index).ShouldBe(expected);

    [Fact]
    public void Factory_BuildsLayersFromWorkspace()
    {
        var workspace = new Workspace { Name = "w", GlobalVariables = [new("a", "global"), new("b", "global")] };
        var collection = new ApiCollection { Key = "c", Name = "c", Variables = [new("b", "collection"), new("c", "collection")] };
        var environment = new ApiEnvironment { Key = "e", Name = "e", Variables = [new("c", "env")] };

        var resolver = VariableResolverFactory.Create(workspace, collection, environment, [new("d", "runtime")]);

        resolver.Resolve("{{a}} {{b}} {{c}} {{d}}").Value.ShouldBe("global collection env runtime");
    }
}
