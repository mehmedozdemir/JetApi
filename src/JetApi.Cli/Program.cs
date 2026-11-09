using System.Text;
using JetApi.Cli;

Console.OutputEncoding = Encoding.UTF8;
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // The first Ctrl+C stops the run and still writes the reports; a second one ends the process.
    e.Cancel = !stop.IsCancellationRequested;
    stop.Cancel();
};

// Colors only on a terminal, and never when NO_COLOR is set (https://no-color.org).
var color = !Console.IsOutputRedirected && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
return await CliApp.RunAsync(args, Console.Out, Console.Error, color, stop.Token).ConfigureAwait(false);
