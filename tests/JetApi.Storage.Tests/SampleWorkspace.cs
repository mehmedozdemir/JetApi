using JetApi.Core.Model;

namespace JetApi.Storage.Tests;

internal static class SampleWorkspace
{
    public static Workspace Create() => new()
    {
        Name = "Şirket API'leri",
        Description = "Shared requests",
        GlobalVariables = [new("userAgent", "JetApi")],
        Environments =
        [
            new ApiEnvironment
            {
                Key = "dev",
                Name = "Development",
                Variables = [new("baseUrl", "http://localhost:5000"), new("token", "dev-token", IsSecret: true)],
            },
            new ApiEnvironment { Key = "prod", Name = "Production", Variables = [new("baseUrl", "https://api.example.com", IsEnabled: false)] },
        ],
        Collections =
        [
            new ApiCollection
            {
                Key = "user-service",
                Name = "User Service",
                Auth = new BearerAuth("{{token}}"),
                Variables = [new("version", "v1")],
                Proto = new ProtoSettings(["protos"], ["users/v1/users.proto"]),
                Children =
                [
                    new HttpRequestDefinition
                    {
                        Key = "list-users",
                        Name = "List users",
                        Url = "{{baseUrl}}/users",
                        QueryParameters = [new("page", "1"), new("size", "20", IsEnabled: false, Description: "page size")],
                    },
                    new Folder
                    {
                        Key = "admin",
                        Name = "Admin",
                        Auth = new BasicAuth("admin", "{{adminPassword}}"),
                        Children =
                        [
                            new HttpRequestDefinition
                            {
                                Key = "create-user",
                                Name = "Create user",
                                Method = "POST",
                                Url = "{{baseUrl}}/users",
                                Headers = [new("X-Trace", "{{$guid}}")],
                                Body = HttpBody.Json("{\n  \"name\": \"Ayşe\"\n}"),
                                Settings = new HttpRequestSettings(FollowRedirects: false, Timeout: TimeSpan.FromSeconds(5)),
                            },
                            new HttpRequestDefinition
                            {
                                Key = "upload-avatar",
                                Name = "Upload avatar",
                                Method = "PUT",
                                Url = "{{baseUrl}}/users/1/avatar",
                                Auth = new ApiKeyAuth("X-Api-Key", "{{apiKey}}", ApiKeyLocation.Query),
                                Body = new HttpBody
                                {
                                    Mode = HttpBodyMode.Multipart,
                                    MultipartFields =
                                    [
                                        new("description", "profile"),
                                        new("file", "files/avatar.png", MultipartFieldKind.File, ContentType: "image/png"),
                                    ],
                                },
                            },
                        ],
                    },
                    new GrpcRequestDefinition
                    {
                        Key = "get-user",
                        Name = "Get user",
                        ServerUrl = "http://localhost:5001",
                        Service = "users.v1.UserService",
                        Method = "GetUser",
                        Message = "{\n  \"id\": \"42\"\n}",
                        Metadata = [new("x-tenant", "acme")],
                        Auth = AuthConfig.None,
                        Settings = new GrpcRequestSettings(ValidateCertificates: false, Deadline: TimeSpan.FromSeconds(3)),
                    },
                ],
            },
        ],
    };
}
