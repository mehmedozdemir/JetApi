namespace JetApi.Core.Model;

/// <summary>A named variable referenced as <c>{{Name}}</c>.</summary>
/// <param name="Name">Variable name; case-sensitive.</param>
/// <param name="Value">Value; may itself reference other variables.</param>
/// <param name="IsEnabled">Disabled variables are ignored during resolution.</param>
/// <param name="IsSecret">Secret values are masked in the UI and logs.</param>
public sealed record Variable(string Name, string Value, bool IsEnabled = true, bool IsSecret = false);
