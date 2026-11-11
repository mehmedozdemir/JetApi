using JetApi.Grpc.Codec;

namespace JetApi.Grpc.Tests;

/// <summary>Field suggestions in the message editor. The caret is marked with <c>|</c>.</summary>
public sealed class ProtoJsonCompletionTests
{
    private static readonly Schema.ProtoSchema Schema = TestSchema.Load();

    private static ProtoJsonCompletion? Find(string marked)
    {
        var caret = marked.IndexOf('|', StringComparison.Ordinal);
        return ProtoJsonCompletion.Find(Schema.Everything(), marked.Remove(caret, 1), caret);
    }

    private static string[] Names(string marked) => [.. Find(marked).ShouldNotBeNull().Fields.Select(f => f.JsonName)];

    // Applies the suggestion for `field` and marks the caret (|) or the selected placeholder ([...]).
    private static string Apply(string marked, string field)
    {
        var caret = marked.IndexOf('|', StringComparison.Ordinal);
        var text = marked.Remove(caret, 1);
        var completion = ProtoJsonCompletion.Find(Schema.Everything(), text, caret).ShouldNotBeNull();
        var edit = completion.Insert(completion.Fields.Single(f => f.JsonName == field));
        var result = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        return edit.SelectionLength == 0
            ? result.Insert(edit.SelectionStart, "|")
            : result.Insert(edit.SelectionStart + edit.SelectionLength, "]").Insert(edit.SelectionStart, "[");
    }

    [Fact]
    public void EmptyObject_OffersEveryField()
    {
        var completion = Find("{ | }").ShouldNotBeNull();
        completion.Type.FullName.ShouldBe("jetapi.test.v1.Everything");
        completion.Fields.Count.ShouldBe(Schema.Everything().Fields.Count);
        completion.IsTyped.ShouldBeFalse();
        completion.Prefix.ShouldBe(string.Empty);
    }

    [Fact]
    public void TypedName_FiltersStartsWithFirst()
    {
        var completion = Find("{\n  \"ad|\n}").ShouldNotBeNull();
        completion.IsTyped.ShouldBeTrue();
        completion.Prefix.ShouldBe("ad");
        completion.Fields.Select(f => f.JsonName).ShouldBe(["address", "addresses", "addressById"]);
    }

    [Fact]
    public void NameMatches_ProtoNameToo() => Names("{ \"address_b| }").ShouldBe(["addressById"]);

    [Fact]
    public void ContainsMatches_ComeAfterPrefixMatches() => Names("{ \"na| }").ShouldBe(["nameWrapper", "dynamic"]);

    [Fact]
    public void BareWord_IsANameBeingTyped()
    {
        var completion = Find("{ fla| }").ShouldNotBeNull();
        completion.IsTyped.ShouldBeTrue();
        completion.Fields.Select(f => f.JsonName).ShouldBe(["flag", "flags"]);
    }

    [Fact]
    public void PresentFields_AreLeftOut_BeforeAndAfterTheCaret()
    {
        var names = Names("{ \"d\": 1, \"i32\": 2, | \"text\": \"x\", \"address\": { \"city\": \"a\" } }");
        names.ShouldNotContain("d");
        names.ShouldNotContain("i32");
        names.ShouldNotContain("text");
        names.ShouldNotContain("address");
        names.ShouldContain("f");
    }

    [Fact]
    public void ProtoNamesCountAsPresent() => Names("{ \"created_at\": \"\", | }").ShouldNotContain("createdAt");

    [Fact]
    public void SetOneof_HidesItsOtherMembers()
    {
        var names = Names("{ \"email\": \"a@b.c\", | }");
        names.ShouldNotContain("email");
        names.ShouldNotContain("phone");
    }

    [Fact]
    public void NestedMessage_OffersItsFields() => Names("{ \"address\": { \"city\": \"x\", | } }").ShouldBe(["street"]);

    [Fact]
    public void RepeatedMessageElements_OfferTheElementFields() =>
        Names("{ \"addresses\": [ { \"city\": \"a\" }, { | } ] }").ShouldBe(["street", "city"]);

    [Fact]
    public void MapValues_OfferTheValueFields() =>
        Names("{ \"addressById\": { \"5\": { | } } }").ShouldBe(["street", "city"]);

    [Fact]
    public void RecursiveMessage_IsFollowed() =>
        Names("{ \"tree\": { \"children\": [ { \"children\": [ { | } ] } ] } }").ShouldBe(["name", "children"]);

    [Theory]
    [InlineData("{ \"d\": |1 }")]
    [InlineData("{ \"text\": \"ab| }")]
    [InlineData("{ \"d\"| }")]
    [InlineData("{ \"d\" | : 1 }")]
    [InlineData("{ \"counts\": { | } }")]
    [InlineData("{ \"attributes\": { | } }")]
    [InlineData("{ \"details\": { | } }")]
    [InlineData("{ \"numbers\": [ | ] }")]
    [InlineData("{ \"addresses\": { | } }")]
    [InlineData("| { }")]
    [InlineData("{ } { | }")]
    [InlineData("")]
    public void NotAPropertyName_OffersNothing(string marked) =>
        (string.IsNullOrEmpty(marked) ? ProtoJsonCompletion.Find(Schema.Everything(), string.Empty, 0) : Find(marked)).ShouldBeNull();

    [Fact]
    public void UnquotedVariable_IsAValue() => Names("{ \"d\": {{rate}}, | }").ShouldNotContain("d");

    [Theory]
    [InlineData("text", "\"text\": \"|\"")]
    [InlineData("i32", "\"i32\": [0]")]
    [InlineData("i64", "\"i64\": \"[0]\"")]
    [InlineData("flag", "\"flag\": [false]")]
    [InlineData("color", "\"color\": \"[COLOR_UNSPECIFIED]\"")]
    [InlineData("address", "\"address\": {|}")]
    [InlineData("addresses", "\"addresses\": [|]")]
    [InlineData("counts", "\"counts\": {|}")]
    [InlineData("createdAt", "\"createdAt\": \"[1970-01-01T00:00:00Z]\"")]
    [InlineData("timeout", "\"timeout\": \"[0s]\"")]
    [InlineData("bigWrapper", "\"bigWrapper\": \"[0]\"")]
    [InlineData("nameWrapper", "\"nameWrapper\": \"|\"")]
    [InlineData("dynamic", "\"dynamic\": [null]")]
    [InlineData("list", "\"list\": [|]")]
    [InlineData("attributes", "\"attributes\": {|}")]
    public void Insert_WritesAPlaceholderOfTheType(string field, string expected) =>
        Apply("{\n  |\n}", field).ShouldBe("{\n  " + expected + "\n}");

    [Fact]
    public void Insert_ReplacesTheTypedName() =>
        Apply("{\n  \"ad|\n}", "address").ShouldBe("{\n  \"address\": {|}\n}");

    [Fact]
    public void Insert_ReplacesAClosedNameAndTheRestOfIt() =>
        Apply("{ \"ad|dr\" }", "addresses").ShouldBe("{ \"addresses\": [|] }");

    [Fact]
    public void Insert_RenamesAnExistingKeyOnly() =>
        Apply("{ \"tex|\": \"kept\" }", "text").ShouldBe("{ \"text\"|: \"kept\" }");

    [Fact]
    public void Insert_AddsTheCommaBeforeTheNextProperty() =>
        Apply("{\n  |\n  \"d\": 1\n}", "flag").ShouldBe("{\n  \"flag\": [false],\n  \"d\": 1\n}");

    [Fact]
    public void Insert_AddsAMissingCommaAfterThePreviousValue() =>
        Apply("{\n  \"d\": 1\n  \"fl|\n}", "flag").ShouldBe("{\n  \"d\": 1,\n  \"flag\": [false]\n}");

    [Fact]
    public void Insert_NoCommaBeforeTheClosingBrace() =>
        Apply("{ \"d\": 1, | }", "flag").ShouldBe("{ \"d\": 1, \"flag\": [false] }");
}
