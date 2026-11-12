namespace GiftPlanner.Auth;

public static class Roles
{
    /// <summary>Every signed-in account owns (and manages) its own household's lists. Givers use share links and never sign in.</summary>
    public const string Admin = "admin";
}
