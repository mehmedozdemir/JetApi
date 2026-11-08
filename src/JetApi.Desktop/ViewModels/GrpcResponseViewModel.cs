using CommunityToolkit.Mvvm.ComponentModel;
using JetApi.Desktop.Localization;
using JetApi.Grpc;

namespace JetApi.Desktop.ViewModels;

/// <summary>The response panel of a gRPC request tab.</summary>
public sealed partial class GrpcResponseViewModel : ViewModelBase
{
    private GrpcCallResult? _result;
    private IReadOnlyList<string> _preparationErrors = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isSending;

    /// <summary>Nothing called yet.</summary>
    public bool IsEmpty => _result is null && _preparationErrors.Count == 0 && !IsSending;

    /// <summary>A status was received.</summary>
    public bool HasResult => _result is not null;

    /// <summary>Status is OK and a message was decoded.</summary>
    public bool HasMessage => _result is { IsOk: true } && !string.IsNullOrEmpty(_result.ResponseJson);

    /// <summary>A non-OK status (or a decoding problem) to explain.</summary>
    public bool HasStatusDetail => _result is not null && !string.IsNullOrEmpty(_result.StatusDetail);

    /// <summary>The call was not made.</summary>
    public bool HasPreparationErrors => _preparationErrors.Count > 0;

    /// <summary>Why the call was not made.</summary>
    public IReadOnlyList<string> PreparationErrors => _preparationErrors;

    /// <summary>"0 · OK", "5 · NotFound".</summary>
    public string StatusText => _result is null
        ? (HasPreparationErrors ? Loc.Get("Res_NotSent") : string.Empty)
        : $"{_result.StatusCode} · {_result.StatusName}";

    /// <summary>Color category.</summary>
    public StatusTone Tone => _result switch
    {
        null => HasPreparationErrors ? StatusTone.Danger : StatusTone.Neutral,
        { IsOk: true } => StatusTone.Success,
        { Outcome: GrpcCallOutcome.Cancelled } => StatusTone.Warning,
        { StatusName: "DeadlineExceeded" or "Cancelled" or "InvalidArgument" or "NotFound" or "AlreadyExists" or "PermissionDenied" or "Unauthenticated" or "FailedPrecondition" or "OutOfRange" } => StatusTone.Warning,
        _ => StatusTone.Danger,
    };

    /// <summary>Call duration.</summary>
    public string DurationText => _result is null ? string.Empty : Formatting.Duration(_result.Duration);

    /// <summary>Status detail sent by the server.</summary>
    public string StatusDetail => _result?.StatusDetail ?? string.Empty;

    /// <summary>The response message as JSON.</summary>
    public string ResponseJson => _result?.ResponseJson ?? string.Empty;

    /// <summary>Size of the encoded response message.</summary>
    public string SizeText => _result is { IsOk: true } ? Formatting.Size(_result.ResponseMessage.Length) : string.Empty;

    /// <summary>Response headers.</summary>
    public IReadOnlyList<HeaderLine> Headers => _result is null ? [] : [.. _result.ResponseHeaders.Select(h => new HeaderLine(h.Key, h.Value))];

    /// <summary>Trailers.</summary>
    public IReadOnlyList<HeaderLine> Trailers => _result is null ? [] : [.. _result.Trailers.Select(h => new HeaderLine(h.Key, h.Value))];

    /// <summary>Sent metadata; authorization values are masked.</summary>
    public IReadOnlyList<HeaderLine> SentMetadata => _result is null
        ? []
        : [.. _result.SentMetadata.Select(h => new HeaderLine(h.Key, h.Key == "authorization" && h.Value.IndexOf(' ', StringComparison.Ordinal) is > 0 and var i ? h.Value[..i] + " ••••••••" : h.Value))];

    /// <summary>Header count badge.</summary>
    public string HeadersBadge => _result is null ? string.Empty : _result.ResponseHeaders.Count.ToString(Loc.Instance.Culture);

    /// <summary>Trailer count badge.</summary>
    public string TrailersBadge => _result is null ? string.Empty : _result.Trailers.Count.ToString(Loc.Instance.Culture);

    /// <summary>Shows a call result.</summary>
    public void SetResult(GrpcCallResult result)
    {
        _result = result;
        _preparationErrors = [];
        IsSending = false;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Shows why the call was not made.</summary>
    public void SetPreparationErrors(IReadOnlyList<string> errors)
    {
        _result = null;
        _preparationErrors = errors;
        IsSending = false;
        OnPropertyChanged(string.Empty);
    }
}
