#:project ../../src/JetApi.Storage/JetApi.Storage.csproj

// Writes a workspace of the size NFR-1 is measured with: 5 collections x 10 folders x 10 requests = 500 requests,
// plus two environments. Usage: dotnet run build/perf/generate-workspace.cs -- <directory> [requests]
using JetApi.Core.Model;
using JetApi.Storage;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: generate-workspace.cs <directory> [requests=500]");
    return 2;
}

var root = Path.GetFullPath(args[0]);
var total = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 500;
const int CollectionCount = 5;
var perFolder = 10;
var foldersPerCollection = Math.Max(1, total / CollectionCount / perFolder);

if (Directory.Exists(root))
{
    Directory.Delete(root, recursive: true);
}

string[] methods = ["GET", "POST", "PUT", "PATCH", "DELETE"];
var collections = new List<ApiCollection>();
var written = 0;
for (var c = 0; c < CollectionCount; c++)
{
    var folders = new List<CollectionNode>();
    for (var f = 0; f < foldersPerCollection && written < total; f++)
    {
        var requests = new List<CollectionNode>();
        for (var r = 0; r < perFolder && written < total; r++, written++)
        {
            var method = methods[written % methods.Length];
            requests.Add(new HttpRequestDefinition
            {
                Key = $"request-{r:00}",
                Name = $"{method} resource {r}",
                Method = method,
                Url = $"{{{{baseUrl}}}}/service{c}/area{f}/items/{r}?page=1",
                Headers = [new("Accept", "application/json"), new("X-Trace-Id", "{{$guid}}")],
                Body = method is "POST" or "PUT" or "PATCH"
                    ? HttpBody.Json($"{{\n  \"id\": {written},\n  \"name\": \"item {written}\",\n  \"tags\": [\"a\", \"b\"]\n}}")
                    : HttpBody.Empty,
            });
        }

        folders.Add(new Folder { Key = $"area-{f:00}", Name = $"Area {f}", Children = requests });
    }

    collections.Add(new ApiCollection
    {
        Key = $"service-{c}",
        Name = $"Service {c}",
        Children = folders,
        Auth = new BearerAuth("{{token}}"),
    });
}

var workspace = new Workspace
{
    Name = "Performance",
    Collections = collections,
    Environments =
    [
        new ApiEnvironment { Key = "dev", Name = "Development", Variables = [new("baseUrl", "http://localhost:5000"), new("token", "dev-token", IsSecret: true)] },
        new ApiEnvironment { Key = "prod", Name = "Production", Variables = [new("baseUrl", "https://api.example.com"), new("token", string.Empty, IsSecret: true)] },
    ],
};

await WorkspaceStore.SaveAsync(root, workspace);
Console.WriteLine($"Wrote {written} requests to {root}");
return 0;
