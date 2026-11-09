using JetApi.Core.Model;
using JetApi.Testing;

namespace JetApi.Runner;

/// <summary>A request in run order.</summary>
/// <param name="CollectionKey">Its collection.</param>
/// <param name="Path">Its path in the collection.</param>
/// <param name="Request">The request.</param>
public sealed record RunRequest(string CollectionKey, IReadOnlyList<string> Path, RequestNode Request);

/// <summary>How a run goes.</summary>
/// <param name="Iterations">How many times the requests run; ignored when <paramref name="DataRows"/> is given (one per row).</param>
/// <param name="Delay">Pause between requests.</param>
/// <param name="StopOnFailure">Stop at the first request that fails (no response or a failed check).</param>
/// <param name="DataRows">Rows of a data file: their columns are variables, one iteration per row.</param>
/// <param name="PersistExtractions">
/// Store extractions that say environment, collection or global there; otherwise every extracted value lives only for the run.
/// </param>
public sealed record RunOptions(
    int Iterations = 1,
    TimeSpan Delay = default,
    bool StopOnFailure = false,
    IReadOnlyList<IReadOnlyDictionary<string, string>>? DataRows = null,
    bool PersistExtractions = false);

/// <summary>What sending one request gave.</summary>
/// <param name="Response">What checks look at; <see cref="ResponseSnapshot.None"/> without a response.</param>
/// <param name="StatusText">"200 OK", "UNAVAILABLE (14)", or empty.</param>
/// <param name="Error">Why there is no response (preparation, connection, timeout); <c>null</c> when there is one.</param>
public sealed record SendResult(ResponseSnapshot Response, string StatusText, string? Error);

/// <summary>Sends requests for a run; the app and the CLI each provide one.</summary>
public interface IRequestSender
{
    /// <summary>Sends a request with <paramref name="runVariables"/> over all other variables.</summary>
    Task<SendResult> SendAsync(RunRequest request, IReadOnlyDictionary<string, string> runVariables, CancellationToken cancellationToken);

    /// <summary>Fills <c>{{variables}}</c> in a check's or extraction's text the same way.</summary>
    string Resolve(RunRequest request, string text, IReadOnlyDictionary<string, string> runVariables);

    /// <summary>Stores an extracted value in the environment, collection or globals; returns why it could not, or <c>null</c>.</summary>
    Task<string?> StoreAsync(RunRequest request, Extraction extraction, string value, CancellationToken cancellationToken);
}

/// <summary>The outcome of one request in a run.</summary>
/// <param name="Iteration">1-based iteration.</param>
/// <param name="Request">The request.</param>
/// <param name="Send">What sending it gave.</param>
/// <param name="Checks">Results of its enabled checks.</param>
/// <param name="Extractions">Results of its enabled extractions.</param>
/// <param name="Duration">How long the request took.</param>
public sealed record RequestRunResult(int Iteration, RunRequest Request, SendResult Send, IReadOnlyList<CheckResult> Checks, IReadOnlyList<ExtractionResult> Extractions, TimeSpan Duration)
{
    /// <summary>A response came and every check passed.</summary>
    public bool Passed => Send.Error is null && Checks.All(c => c.Passed);
}

/// <summary>The outcome of a run.</summary>
/// <param name="Results">Every request that ran, in order.</param>
/// <param name="IterationCount">Iterations planned.</param>
/// <param name="StoppedEarly">Stopped at a failure (stop on failure).</param>
/// <param name="Cancelled">Cancelled by the user.</param>
/// <param name="Duration">Wall-clock time.</param>
public sealed record RunSummary(IReadOnlyList<RequestRunResult> Results, int IterationCount, bool StoppedEarly, bool Cancelled, TimeSpan Duration)
{
    /// <summary>Checks that passed.</summary>
    public int PassedChecks => Results.Sum(r => r.Checks.Count(c => c.Passed));

    /// <summary>Checks that failed.</summary>
    public int FailedChecks => Results.Sum(r => r.Checks.Count(c => !c.Passed));

    /// <summary>Requests without a response.</summary>
    public int FailedRequests => Results.Count(r => r.Send.Error is not null);

    /// <summary>Nothing failed and the run was not cut short.</summary>
    public bool Succeeded => !Cancelled && !StoppedEarly && FailedChecks == 0 && FailedRequests == 0;
}

/// <summary>
/// Runs requests in order over iterations or data rows, with their checks and extractions (M3). It continues on the
/// caller's context, so the app's callbacks arrive on its UI thread; the CLI has no such context.
/// </summary>
public static class CollectionRunner
{
    /// <summary>Runs <paramref name="requests"/>.</summary>
    /// <param name="requests">The requests in run order.</param>
    /// <param name="options">Iterations, data rows, delay, stop on failure.</param>
    /// <param name="sender">Sends the requests.</param>
    /// <param name="onIteration">Called when an iteration starts (1-based, with its data row).</param>
    /// <param name="onResult">Called after each request.</param>
    /// <param name="cancellationToken">Stops the run; what ran so far is returned.</param>
    public static async Task<RunSummary> RunAsync(
        IReadOnlyList<RunRequest> requests,
        RunOptions options,
        IRequestSender sender,
        Action<int, IReadOnlyDictionary<string, string>?>? onIteration = null,
        Action<RequestRunResult>? onResult = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sender);
        var started = System.Diagnostics.Stopwatch.StartNew();
        var iterations = options.DataRows is { Count: > 0 } rows ? rows.Count : Math.Max(1, options.Iterations);
        var results = new List<RequestRunResult>();
        var extracted = new Dictionary<string, string>(StringComparer.Ordinal);
        var first = true;
        try
        {
            for (var iteration = 1; iteration <= iterations; iteration++)
            {
                var row = options.DataRows is { Count: > 0 } data ? data[iteration - 1] : null;
                onIteration?.Invoke(iteration, row);
                foreach (var request in requests)
                {
                    if (!first && options.Delay > TimeSpan.Zero)
                    {
                        await Task.Delay(options.Delay, cancellationToken).ConfigureAwait(true);
                    }

                    first = false;
                    cancellationToken.ThrowIfCancellationRequested();

                    // The data row first; values extracted earlier in the run win.
                    var variables = new Dictionary<string, string>(row ?? new Dictionary<string, string>(), StringComparer.Ordinal);
                    foreach (var (name, value) in extracted)
                    {
                        variables[name] = value;
                    }

                    var result = await RunOneAsync(iteration, request, variables, extracted, options, sender, cancellationToken).ConfigureAwait(true);
                    results.Add(result);
                    onResult?.Invoke(result);
                    if (options.StopOnFailure && !result.Passed)
                    {
                        return new RunSummary(results, iterations, StoppedEarly: true, Cancelled: false, started.Elapsed);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunSummary(results, iterations, StoppedEarly: false, Cancelled: true, started.Elapsed);
        }

        return new RunSummary(results, iterations, StoppedEarly: false, Cancelled: false, started.Elapsed);
    }

    private static async Task<RequestRunResult> RunOneAsync(
        int iteration,
        RunRequest request,
        Dictionary<string, string> variables,
        Dictionary<string, string> extracted,
        RunOptions options,
        IRequestSender sender,
        CancellationToken cancellationToken)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var send = await sender.SendAsync(request, variables, cancellationToken).ConfigureAwait(true);
        string Resolve(string text) => sender.Resolve(request, text, variables);
        var checks = ResponseChecker.Run(send.Response, request.Request.Checks, Resolve);
        var extractions = new List<ExtractionResult>();
        foreach (var result in ResponseExtractor.Run(send.Response, request.Request.Extractions, Resolve))
        {
            if (!result.IsSuccess)
            {
                extractions.Add(result);
                continue;
            }

            var error = options.PersistExtractions && result.Extraction.To != ExtractionTarget.Runtime
                ? await sender.StoreAsync(request, result.Extraction, result.Value!, cancellationToken).ConfigureAwait(true)
                : null;
            if (error is null)
            {
                extracted[result.Extraction.Variable] = result.Value!;
                variables[result.Extraction.Variable] = result.Value!;
            }

            extractions.Add(error is null ? result : result with { Value = null, Error = error });
        }

        return new RequestRunResult(iteration, request, send, checks, extractions, watch.Elapsed);
    }
}
