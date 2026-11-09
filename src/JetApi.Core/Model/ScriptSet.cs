namespace JetApi.Core.Model;

/// <summary>
/// Postman pre-request and test scripts kept from an import, verbatim (the <c>scripts</c> section of a file). JetApi does
/// not run them yet; the parts it understood became checks and extractions (ADR-0010).
/// </summary>
/// <param name="PreRequest">JavaScript that ran before the request in Postman.</param>
/// <param name="Test">JavaScript that ran after the response in Postman.</param>
public sealed record ScriptSet(string PreRequest = "", string Test = "")
{
    /// <summary>No scripts.</summary>
    public static readonly ScriptSet None = new();

    /// <summary>Neither script has any code.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(PreRequest) && string.IsNullOrWhiteSpace(Test);
}
