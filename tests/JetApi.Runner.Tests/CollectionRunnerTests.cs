using JetApi.Core.Model;
using JetApi.Testing;

namespace JetApi.Runner.Tests;

public sealed class CollectionRunnerTests
{
    // Answers with a JSON body echoing the request name and the run variables it saw.
    private sealed class FakeSender : IRequestSender
    {
        public List<(string Name, Dictionary<string, string> Variables)> Sent { get; } = [];

        public List<(string Variable, string Value)> Stored { get; } = [];

        public Func<RunRequest, SendResult?>? Override { get; set; }

        public Task<SendResult> SendAsync(RunRequest request, IReadOnlyDictionary<string, string> runVariables, CancellationToken cancellationToken)
        {
            Sent.Add((request.Request.Name, new Dictionary<string, string>(runVariables)));
            if (Override?.Invoke(request) is { } custom)
            {
                return Task.FromResult(custom);
            }

            var body = $$"""{ "name": "{{request.Request.Name}}", "email": "{{runVariables.GetValueOrDefault("email", "")}}", "token": "t-{{Sent.Count}}" }""";
            return Task.FromResult(new SendResult(new ResponseSnapshot(200, [], body, TimeSpan.FromMilliseconds(5)), "200 OK", null));
        }

        public string Resolve(RunRequest request, string text, IReadOnlyDictionary<string, string> runVariables) =>
            runVariables.Aggregate(text, (t, v) => t.Replace("{{" + v.Key + "}}", v.Value, StringComparison.Ordinal));

        public Task<string?> StoreAsync(RunRequest request, Extraction extraction, string value, CancellationToken cancellationToken)
        {
            Stored.Add((extraction.Variable, value));
            return Task.FromResult<string?>(null);
        }
    }

    private static RunRequest Request(string name, ResponseCheck[]? checks = null, Extraction[]? extractions = null) =>
        new("users", [name.ToLowerInvariant()], new HttpRequestDefinition { Key = name.ToLowerInvariant(), Name = name, Checks = checks ?? [], Extractions = extractions ?? [] });

    [Fact]
    public async Task Iterations_RunEveryRequestInOrder()
    {
        var sender = new FakeSender();
        var iterations = new List<int>();

        var summary = await CollectionRunner.RunAsync([Request("List"), Request("Create")], new RunOptions(Iterations: 2), sender, (i, _) => iterations.Add(i),
            cancellationToken: TestContext.Current.CancellationToken);

        sender.Sent.Select(s => s.Name).ShouldBe(["List", "Create", "List", "Create"]);
        iterations.ShouldBe([1, 2]);
        summary.Results.Select(r => r.Iteration).ShouldBe([1, 1, 2, 2]);
        summary.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task DataRows_DriveTheIterations_AndAreVariables()
    {
        var sender = new FakeSender();
        var create = Request("Create", [new ResponseCheck(CheckSource.Json, CheckOperator.Equals, "$.email", "{{email}}")]);

        var summary = await CollectionRunner.RunAsync([create], new RunOptions(Iterations: 9, DataRows: [Row("email", "a@b.c"), Row("email", "d@e.f")]), sender,
            cancellationToken: TestContext.Current.CancellationToken);

        summary.IterationCount.ShouldBe(2);
        sender.Sent.Select(s => s.Variables["email"]).ShouldBe(["a@b.c", "d@e.f"]);
        summary.PassedChecks.ShouldBe(2);
    }

    [Fact]
    public async Task ExtractedValues_ReachLaterRequests_AndOverrideTheDataRow()
    {
        var sender = new FakeSender();
        var login = Request("Login", extractions: [new Extraction(ExtractionSource.Json, "$.token", "token"), new Extraction(ExtractionSource.Json, "$.name", "email", ExtractionTarget.Environment)]);

        await CollectionRunner.RunAsync([login, Request("Me")], new RunOptions(DataRows: [Row("email", "row@x.y")]), sender, cancellationToken: TestContext.Current.CancellationToken);

        sender.Sent[1].Variables["token"].ShouldBe("t-1");
        sender.Sent[1].Variables["email"].ShouldBe("Login");
        sender.Stored.ShouldBeEmpty();
    }

    [Fact]
    public async Task PersistedExtractions_AreStored_RuntimeOnesAreNot()
    {
        var sender = new FakeSender();
        var login = Request("Login", extractions: [new Extraction(ExtractionSource.Json, "$.token", "token"), new Extraction(ExtractionSource.Status, string.Empty, "status", ExtractionTarget.Environment)]);

        await CollectionRunner.RunAsync([login], new RunOptions(PersistExtractions: true), sender, cancellationToken: TestContext.Current.CancellationToken);

        sender.Stored.ShouldBe([("status", "200")]);
    }

    [Fact]
    public async Task StopOnFailure_StopsAtAFailedCheck_OrAMissingResponse()
    {
        var sender = new FakeSender();
        var failing = Request("Create", [new ResponseCheck(CheckSource.Status, CheckOperator.Equals, Expected: "201")]);

        var stopped = await CollectionRunner.RunAsync([Request("List"), failing, Request("Delete")], new RunOptions(Iterations: 3, StopOnFailure: true), sender,
            cancellationToken: TestContext.Current.CancellationToken);
        stopped.StoppedEarly.ShouldBeTrue();
        stopped.Results.Count.ShouldBe(2);
        stopped.Succeeded.ShouldBeFalse();

        sender.Override = r => r.Request.Name == "List" ? new SendResult(ResponseSnapshot.None, string.Empty, "Connection refused.") : null;
        var unreachable = await CollectionRunner.RunAsync([Request("List"), Request("Delete")], new RunOptions(StopOnFailure: true), sender,
            cancellationToken: TestContext.Current.CancellationToken);
        unreachable.Results.ShouldHaveSingleItem().Passed.ShouldBeFalse();
        unreachable.FailedRequests.ShouldBe(1);

        var keepsGoing = await CollectionRunner.RunAsync([Request("List"), Request("Delete")], new RunOptions(), sender, cancellationToken: TestContext.Current.CancellationToken);
        keepsGoing.Results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Cancelling_ReturnsWhatRan()
    {
        var sender = new FakeSender();
        using var cancellation = new CancellationTokenSource();

        var summary = await CollectionRunner.RunAsync([Request("List"), Request("Create")], new RunOptions(Iterations: 5, Delay: TimeSpan.FromSeconds(30)), sender,
            onResult: _ => cancellation.Cancel(), cancellationToken: cancellation.Token);

        summary.Cancelled.ShouldBeTrue();
        summary.Results.ShouldHaveSingleItem();
    }

    private static Dictionary<string, string> Row(string name, string value) => new() { [name] = value };
}
