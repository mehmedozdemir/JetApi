using System.Net;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using JetApi.Desktop.Localization;
using JetApi.Http;

namespace JetApi.Desktop.ViewModels;

/// <summary>Visual category of a status, mapped to colors in the view.</summary>
public enum StatusTone
{
    /// <summary>Neutral.</summary>
    Neutral,

    /// <summary>2xx / OK.</summary>
    Success,

    /// <summary>3xx.</summary>
    Info,

    /// <summary>4xx, timeouts, warnings.</summary>
    Warning,

    /// <summary>5xx, network failures, errors.</summary>
    Danger,
}

/// <summary>Response view mode.</summary>
public enum BodyView
{
    /// <summary>Formatted JSON/XML.</summary>
    Pretty,

    /// <summary>As received.</summary>
    Raw,

    /// <summary>Hex dump.</summary>
    Hex,
}

/// <summary>A row of the timing breakdown.</summary>
/// <param name="LabelKey">String key of the phase.</param>
/// <param name="Value">Formatted duration.</param>
/// <param name="Offset">Start of the bar, 0..1 of the total.</param>
/// <param name="Width">Length of the bar, 0..1 of the total.</param>
/// <param name="IsTotal">The total row.</param>
public sealed record TimingRow(string LabelKey, string Value, double Offset, double Width, bool IsTotal)
{
    /// <summary>Localized phase name.</summary>
    public string Label => Loc.Get(LabelKey);

    /// <summary>Phase name used to pick the bar color (Dns, Connect, FirstByte, Download, Total).</summary>
    public string Phase => LabelKey["Res_".Length..];
}

/// <summary>A header line.</summary>
/// <param name="Name">Header name.</param>
/// <param name="Value">Header value.</param>
/// <param name="IsAutomatic">Added by JetApi rather than typed by the user.</param>
public sealed record HeaderLine(string Name, string Value, bool IsAutomatic = false);

/// <summary>The response panel of an HTTP request tab.</summary>
public sealed partial class HttpResponseViewModel : ViewModelBase
{
    private const string Mask = "••••••••";

    private HttpExecutionResult? _result;
    private string _bodyText = string.Empty;
    private IReadOnlyList<string> _preparationErrors = [];
    private IReadOnlyList<KeyValuePair<string, string>> _userHeaders = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayedBody), nameof(IsPretty), nameof(IsRaw), nameof(IsHex))]
    private BodyView _view = BodyView.Pretty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SentText))]
    private bool _revealSecrets;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isSending;

    /// <summary>Nothing sent yet.</summary>
    public bool IsEmpty => _result is null && _preparationErrors.Count == 0 && !IsSending;

    /// <summary>A response or failure is shown.</summary>
    public bool HasResult => _result is not null;

    /// <summary>A response with a status code was received.</summary>
    public bool HasResponse => _result?.Outcome == HttpExecutionOutcome.Completed;

    /// <summary>The request failed, timed out or was cancelled.</summary>
    public bool HasFailure => _result is not null && _result.Outcome != HttpExecutionOutcome.Completed;

    /// <summary>The request was not sent because preparation failed.</summary>
    public bool HasPreparationErrors => _preparationErrors.Count > 0;

    /// <summary>Why the request was not sent.</summary>
    public IReadOnlyList<string> PreparationErrors => _preparationErrors;

    /// <summary>The raw result.</summary>
    public HttpExecutionResult? Result => _result;

    /// <summary>"201 Created", "No response", ...</summary>
    public string StatusText => _result switch
    {
        null => HasPreparationErrors ? Loc.Get("Res_NotSent") : string.Empty,
        { Outcome: HttpExecutionOutcome.Completed, StatusCode: { } code } => $"{(int)code} {_result.ReasonPhrase ?? code.ToString()}".Trim(),
        { Outcome: HttpExecutionOutcome.TimedOut } => Loc.Get("Res_TimedOut"),
        { Outcome: HttpExecutionOutcome.Cancelled } => Loc.Get("Res_Cancelled"),
        _ => Loc.Get("Res_Failed"),
    };

    /// <summary>Color category of the status.</summary>
    public StatusTone Tone => _result switch
    {
        null => HasPreparationErrors ? StatusTone.Danger : StatusTone.Neutral,
        { Outcome: HttpExecutionOutcome.Completed, StatusCode: { } code } => (int)code switch
        {
            < 300 => StatusTone.Success,
            < 400 => StatusTone.Info,
            < 500 => StatusTone.Warning,
            _ => StatusTone.Danger,
        },
        { Outcome: HttpExecutionOutcome.TimedOut or HttpExecutionOutcome.Cancelled } => StatusTone.Warning,
        _ => StatusTone.Danger,
    };

    /// <summary>Total duration.</summary>
    public string DurationText => _result?.Timings is { } timings ? Formatting.Duration(timings.Total) : string.Empty;

    /// <summary>Body size.</summary>
    public string SizeText => HasResponse ? Formatting.Size(_result!.Body.Length) : string.Empty;

    /// <summary>HTTP/1.1, HTTP/2.</summary>
    public string ProtocolText => _result?.HttpVersion is { } version ? $"HTTP/{(version.Minor == 0 && version.Major >= 2 ? version.Major.ToString(Loc.Instance.Culture) : version.ToString(2))}" : string.Empty;

    /// <summary>Remote address of a new connection.</summary>
    public string RemoteText => _result?.RemoteEndPoint ?? string.Empty;

    /// <summary>Response content type.</summary>
    public string ContentType => _result?.ContentType ?? string.Empty;

    /// <summary>Error message for failures, in the UI language.</summary>
    public string ErrorMessage => _result switch
    {
        null => string.Empty,
        { ErrorKind: HttpErrorKind.DnsFailure } => Loc.Get("Err_Dns"),
        { ErrorKind: HttpErrorKind.ConnectionRefused } => Loc.Get("Err_Refused"),
        { ErrorKind: HttpErrorKind.TimedOut } => Loc.Format("Err_Timeout", DurationText),
        { ErrorKind: HttpErrorKind.Cancelled } => Loc.Get("Err_Cancelled"),
        { ErrorKind: HttpErrorKind.TlsFailure } => Loc.Format("Err_Tls", _result.ErrorDetail ?? _result.ErrorMessage),
        _ => Loc.Format("Err_Network", _result.ErrorDetail ?? _result.ErrorMessage),
    };

    /// <summary>The body was cut off.</summary>
    public bool IsTruncated => _result?.IsBodyTruncated ?? false;

    /// <summary>Truncation warning.</summary>
    public string TruncatedText => Loc.Format("Res_Truncated", Formatting.Size(_result?.Body.Length ?? 0));

    /// <summary>Response has an empty body.</summary>
    public bool HasNoBody => HasResponse && _result!.Body.IsEmpty;

    /// <summary>Syntax for the body editor: json, xml or text.</summary>
    public string BodyLanguage => Formatting.LooksLikeJson(ContentType, _bodyText) ? "json" : Formatting.LooksLikeXml(ContentType, _bodyText) ? "xml" : "text";

    /// <summary>The body in the selected view.</summary>
    public string DisplayedBody => View switch
    {
        BodyView.Raw => _bodyText,
        BodyView.Hex => _result is null ? string.Empty : Formatting.HexDump(_result.Body.Span),
        _ => Formatting.Pretty(ContentType, _bodyText),
    };

    /// <summary>Pretty view selected.</summary>
    public bool IsPretty
    {
        get => View == BodyView.Pretty;
        set
        {
            if (value)
            {
                View = BodyView.Pretty;
            }
        }
    }

    /// <summary>Raw view selected.</summary>
    public bool IsRaw
    {
        get => View == BodyView.Raw;
        set
        {
            if (value)
            {
                View = BodyView.Raw;
            }
        }
    }

    /// <summary>Hex view selected.</summary>
    public bool IsHex
    {
        get => View == BodyView.Hex;
        set
        {
            if (value)
            {
                View = BodyView.Hex;
            }
        }
    }

    /// <summary>Response headers.</summary>
    public IReadOnlyList<HeaderLine> Headers => _result is null ? [] : [.. _result.Headers.Select(h => new HeaderLine(h.Key, h.Value))];

    /// <summary>Header count badge.</summary>
    public string HeadersBadge => HasResponse ? _result!.Headers.Count.ToString(Loc.Instance.Culture) : string.Empty;

    /// <summary>Timing breakdown.</summary>
    public IReadOnlyList<TimingRow> Timings
    {
        get
        {
            if (_result?.Timings is not { } t || t.Total <= TimeSpan.Zero)
            {
                return [];
            }

            var total = t.Total.TotalMilliseconds;
            var dns = t.Dns?.TotalMilliseconds ?? 0;
            var connect = t.Connect?.TotalMilliseconds ?? 0;
            var firstByte = Math.Max(0, t.TimeToFirstByte.TotalMilliseconds - dns - connect);
            var rows = new List<TimingRow>();
            if (t.Dns is not null)
            {
                rows.Add(new("Res_Dns", Formatting.Duration(t.Dns.Value), 0, dns / total, false));
            }

            if (t.Connect is not null)
            {
                rows.Add(new("Res_Connect", Formatting.Duration(t.Connect.Value), dns / total, connect / total, false));
            }

            if (HasResponse)
            {
                rows.Add(new("Res_FirstByte", Formatting.Duration(TimeSpan.FromMilliseconds(firstByte)), (dns + connect) / total, firstByte / total, false));
                rows.Add(new("Res_Download", Formatting.Duration(t.Download), t.TimeToFirstByte.TotalMilliseconds / total, t.Download.TotalMilliseconds / total, false));
            }

            rows.Add(new("Res_Total", Formatting.Duration(t.Total), 0, 1, true));
            return rows;
        }
    }

    /// <summary>Explains whether the connection was new or reused.</summary>
    public string TimingNote => _result?.Timings is { } t && HasResponse ? Loc.Get(t.IsConnectionReused ? "Res_TimingReused" : "Res_TimingNew") : string.Empty;

    /// <summary>Headers as sent, with automatic ones marked.</summary>
    public IReadOnlyList<HeaderLine> SentHeaders =>
        _result is null
            ? []
            : [.. _result.Sent.Headers.Select(h => new HeaderLine(h.Key, MaskIfSecret(h.Key, h.Value), !_userHeaders.Any(u => u.Key.Equals(h.Key, StringComparison.OrdinalIgnoreCase))))];

    /// <summary>The request as sent, as text.</summary>
    public string SentText
    {
        get
        {
            if (_result is null)
            {
                return string.Empty;
            }

            var sent = _result.Sent;
            var builder = new StringBuilder();
            builder.Append(sent.Method).Append(' ').Append(sent.Url.AbsoluteUri).Append('\n');
            var width = sent.Headers.Count == 0 ? 0 : sent.Headers.Max(h => h.Key.Length + h.Value.Length) + 4;
            foreach (var header in SentHeaders)
            {
                var line = $"{header.Name}: {header.Value}";
                builder.Append(line);
                if (header.IsAutomatic)
                {
                    builder.Append(' ', Math.Max(1, width - line.Length)).Append("# ").Append(Loc.Get("Res_Automatic"));
                }

                builder.Append('\n');
            }

            return builder.ToString();
        }
    }

    /// <summary>Shows a sent request's result.</summary>
    public void SetResult(HttpExecutionResult result, IReadOnlyList<KeyValuePair<string, string>> userHeaders)
    {
        ArgumentNullException.ThrowIfNull(result);
        _result = result;
        _preparationErrors = [];
        _userHeaders = userHeaders;
        _bodyText = result.Outcome == HttpExecutionOutcome.Completed ? Formatting.DecodeText(result.Body.Span, result.ContentType) : string.Empty;
        View = BodyView.Pretty;
        IsSending = false;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Shows why the request was not sent.</summary>
    public void SetPreparationErrors(IReadOnlyList<string> errors)
    {
        _result = null;
        _preparationErrors = errors;
        _bodyText = string.Empty;
        IsSending = false;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>The body text for copying or saving.</summary>
    public string BodyForCopy => DisplayedBody;

    /// <summary>The raw body bytes for saving.</summary>
    public ReadOnlyMemory<byte> RawBody => _result?.Body ?? ReadOnlyMemory<byte>.Empty;

    private string MaskIfSecret(string name, string value) =>
        !RevealSecrets && (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase))
            ? (value.IndexOf(' ', StringComparison.Ordinal) is > 0 and var space ? value[..space] + " " + Mask : Mask)
            : value;
}
