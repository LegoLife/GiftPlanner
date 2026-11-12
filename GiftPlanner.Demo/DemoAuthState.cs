using System.Security.Claims;
using GiftPlanner.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace GiftPlanner.Demo;

/// <summary>Everyone using the demo is the parent who owns the lists.</summary>
public sealed class DemoAuthState : AuthenticationStateProvider
{
    private static readonly Task<AuthenticationState> State = Task.FromResult(new AuthenticationState(
        new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "Demo"), new Claim(ClaimTypes.Role, Roles.Admin)], "demo"))));

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => State;
}
