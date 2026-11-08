using JetApi.Core.Model;

namespace JetApi.Core.Tests.Model;

public sealed class NodeKeyTests
{
    [Theory]
    [InlineData("Get User", "get-user")]
    [InlineData("Get User (v2)", "get-user-v2")]
    [InlineData("  --Create   order!! ", "create-order")]
    [InlineData("Kullanıcı Güncelle", "kullanici-guncelle")]
    [InlineData("ŞİRKET ÇALIŞANLARI", "sirket-calisanlari")]
    [InlineData("Müşteri Öğesi", "musteri-ogesi")]
    [InlineData("!!!", "item")]
    [InlineData("", "item")]
    public void FromName_ProducesKebabCaseAscii(string name, string expected) =>
        NodeKey.FromName(name).ShouldBe(expected);

    [Fact]
    public void FromName_TruncatesLongNames()
    {
        var key = NodeKey.FromName(new string('a', 100));

        key.Length.ShouldBe(NodeKey.MaxLength);
        NodeKey.IsValid(key).ShouldBeTrue();
    }

    [Fact]
    public void CreateUnique_AppendsSuffix_WhenKeyIsTaken()
    {
        NodeKey.CreateUnique("Get User", ["get-user", "get-user-2"]).ShouldBe("get-user-3");
        NodeKey.CreateUnique("Get User", ["other"]).ShouldBe("get-user");
    }

    [Theory]
    [InlineData("get-user", true)]
    [InlineData("v2", true)]
    [InlineData("Get-User", false)]
    [InlineData("-get", false)]
    [InlineData("get-", false)]
    [InlineData("get user", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_ChecksFormat(string? key, bool expected) => NodeKey.IsValid(key).ShouldBe(expected);
}
