using JetApi.Core.Model;

namespace JetApi.Core.Tests.Model;

public sealed class CollectionTreeTests
{
    private static readonly ApiCollection Sample = new()
    {
        Key = "users",
        Name = "Users",
        Children =
        [
            new HttpRequestDefinition { Key = "list", Name = "List" },
            new Folder
            {
                Key = "admin",
                Name = "Admin",
                Auth = new BasicAuth("a", "b"),
                Children =
                [
                    new HttpRequestDefinition { Key = "create", Name = "Create" },
                    new Folder { Key = "deep", Name = "Deep", Children = [new GrpcRequestDefinition { Key = "get", Name = "Get" }] },
                ],
            },
        ],
    };

    [Fact]
    public void Find_ReturnsNodesAtAnyDepth()
    {
        CollectionTree.Find(Sample, ["list"])!.Name.ShouldBe("List");
        CollectionTree.Find(Sample, ["admin", "deep", "get"])!.ShouldBeOfType<GrpcRequestDefinition>();
        CollectionTree.Find(Sample, ["admin", "missing"]).ShouldBeNull();
        CollectionTree.Find(Sample, ["list", "x"]).ShouldBeNull();
    }

    [Fact]
    public void Ancestors_AreClosestFirst() =>
        CollectionTree.Ancestors(Sample, ["admin", "deep", "get"]).Select(f => f.Key).ShouldBe(["deep", "admin"]);

    [Fact]
    public void Replace_KeepsPosition_AndDoesNotMutateOriginal()
    {
        var updated = CollectionTree.Replace(Sample, ["admin", "create"], new HttpRequestDefinition { Key = "create", Name = "Renamed" });

        ((Folder)updated.Children[1]).Children.Select(c => c.Name).ShouldBe(["Renamed", "Deep"]);
        CollectionTree.Find(Sample, ["admin", "create"])!.Name.ShouldBe("Create");
        ((Folder)updated.Children[1]).Auth.ShouldBe(new BasicAuth("a", "b"));
    }

    [Fact]
    public void Add_InsertsAtIndex_AndRejectsDuplicateKeysIgnoringCase()
    {
        var updated = CollectionTree.Add(Sample, ["admin"], new HttpRequestDefinition { Key = "new", Name = "New" }, 0);

        CollectionTree.ChildrenOf(updated, ["admin"]).Select(c => c.Key).ShouldBe(["new", "create", "deep"]);
        Should.Throw<ArgumentException>(() => CollectionTree.Add(Sample, [], new Folder { Key = "LIST", Name = "x" }));
        Should.Throw<ArgumentException>(() => CollectionTree.Add(Sample, ["list"], new Folder { Key = "x", Name = "x" }));
    }

    [Fact]
    public void Remove_And_Move()
    {
        CollectionTree.Remove(Sample, ["admin", "deep"]).Children[1].ShouldBeOfType<Folder>().Children.ShouldHaveSingleItem();

        var moved = CollectionTree.Move(Sample, ["admin", "deep", "get"], []);
        moved.Children.Select(c => c.Key).ShouldBe(["list", "admin", "get"]);
        Should.Throw<ArgumentException>(() => CollectionTree.Move(Sample, ["admin"], ["admin", "deep"]));
    }

    [Fact]
    public void EnumerateRequests_ReturnsPathsInDisplayOrder() =>
        CollectionTree.EnumerateRequests(Sample).Select(r => string.Join('/', r.Path)).ShouldBe(["list", "admin/create", "admin/deep/get"]);

    [Fact]
    public void WorkspaceExtensions_ReplaceOrAppend()
    {
        var workspace = new Workspace { Name = "w", Collections = [Sample] };

        workspace.WithCollection(Sample with { Name = "Changed" }).Collections.ShouldHaveSingleItem().Name.ShouldBe("Changed");
        workspace.WithCollection(new ApiCollection { Key = "other", Name = "Other" }).Collections.Count.ShouldBe(2);
        workspace.WithEnvironment(new ApiEnvironment { Key = "dev", Name = "Dev" }).FindEnvironment("dev").ShouldNotBeNull();
        workspace.FindEnvironment(null).ShouldBeNull();
        workspace.FindCollection("users").ShouldBeSameAs(Sample);
    }
}
