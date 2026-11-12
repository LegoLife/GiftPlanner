using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace GiftPlanner.Auth;

/// <summary>Which household the current user is. GiftService scopes every admin query by it.</summary>
public interface IAccountContext
{
    Task<int> GetAccountIdAsync();
}

/// <summary>Reads the account id from the signed-in user's NameIdentifier claim.</summary>
public sealed class ClaimsAccountContext(AuthenticationStateProvider auth) : IAccountContext
{
    public async Task<int> GetAccountIdAsync()
    {
        var user = (await auth.GetAuthenticationStateAsync()).User;
        return int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : throw new UnauthorizedAccessException("Not signed in.");
    }
}
