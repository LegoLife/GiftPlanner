namespace GiftPlanner.Auth;

/// <summary>What the sign-in pages offer.</summary>
public sealed record SignInOptions(bool GoogleEnabled)
{
    /// <summary>Holds Google's answer just long enough to find or create the account.</summary>
    public const string ExternalScheme = "External";

    /// <summary>Only same-site paths, so a crafted link can't bounce someone to another site after sign-in.</summary>
    public static string SafeReturnUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\')) ? url : "/";
}
