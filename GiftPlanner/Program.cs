using GiftPlanner.Auth;
using GiftPlanner.Components;
using GiftPlanner.Data;
using GiftPlanner.Gifts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

// `dotnet GiftPlanner.dll hash-password [password]` prints a hash (e.g. for GiftPlanner__PasswordHash, the owner account's password).
if (args is ["hash-password", .. var rest])
{
    var pw = rest.Length > 0 ? rest[0] : Console.ReadLine();
    if (string.IsNullOrEmpty(pw)) { Console.Error.WriteLine("Password can't be empty."); return 1; }
    Console.WriteLine(AccountService.HashPassword(pw));
    return 0;
}

// `dotnet GiftPlanner.dll lookup <url>` prints what the product lookup finds (for debugging stores).
if (args is ["lookup", var lookupUrl])
{
    var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
    ProductLookup.Configure(http);
    var info = await new ProductLookup(http, LoggerFactory.Create(b => b.AddSimpleConsole()).CreateLogger<ProductLookup>()).LookupAsync(lookupUrl);
    Console.WriteLine(info?.ToString() ?? "(page couldn't be fetched)");
    return 0;
}

// The container has no locale, so .NET would format prices with the generic "¤" sign. Always use US dollars.
var usCulture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
usCulture.NumberFormat.CurrencySymbol = "$";
usCulture.NumberFormat.CurrencyPositivePattern = 0; // $1.00
usCulture.NumberFormat.CurrencyNegativePattern = 1; // -$1.00
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = usCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = usCulture;
System.Globalization.CultureInfo.CurrentCulture = usCulture;

var builder = WebApplication.CreateBuilder(args);

var dataDir = builder.Configuration["GiftPlanner:DataDirectory"] ?? builder.Environment.ContentRootPath;
var dbPath = Path.Combine(dataDir, "giftplanner.db");

// Before accounts existed there was one password; the migration gives that data to account 1,
// and these settings give account 1 an email + that same password so the old login keeps working.
var ownerEmail = builder.Configuration["GiftPlanner:OwnerEmail"];
var legacyPasswordHash = builder.Configuration["GiftPlanner:PasswordHash"];

// Google sign-in is on only when a client id/secret are configured.
var googleClientId = builder.Configuration["GiftPlanner:Google:ClientId"];
var googleClientSecret = builder.Configuration["GiftPlanner:Google:ClientSecret"];
var googleEnabled = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<IAccountContext, ClaimsAccountContext>();
builder.Services.AddScoped<GiftService>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SignupGuard>();
builder.Services.AddSingleton(new SignInOptions(googleEnabled));
builder.Services.AddHostedService(sp => new NightlyBackup(dbPath, Path.Combine(dataDir, "backups"),
    sp.GetRequiredService<ILogger<NightlyBackup>>(), TimeProvider.System));
builder.Services.AddHttpClient<ProductLookup>(ProductLookup.Configure)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        MaxAutomaticRedirections = 5,
    });

// Keys persist with the data so sign-ins survive container restarts.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")))
    .SetApplicationName("GiftPlanner");

var auth = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/";
        o.Cookie.Name = "giftplanner.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTPS behind a proxy, plain HTTP on the LAN
        o.ExpireTimeSpan = TimeSpan.FromDays(60);
        o.SlidingExpiration = true;
        // Drop sign-ins from before accounts existed, and for accounts that no longer exist.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var ok = int.TryParse(ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
                     && await ctx.HttpContext.RequestServices.GetRequiredService<AccountService>().ExistsAsync(id);
            if (!ok)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

if (googleEnabled)
{
    auth.AddCookie(SignInOptions.ExternalScheme, o =>
        {
            o.Cookie.Name = "giftplanner.external";
            o.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        })
        .AddGoogle(o =>
        {
            o.SignInScheme = SignInOptions.ExternalScheme;
            o.ClientId = googleClientId!;
            o.ClientSecret = googleClientSecret!;
            o.ClaimActions.MapJsonKey("email_verified", "email_verified");
        });
}

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Honour X-Forwarded-* from a reverse proxy (e.g. Nginx Proxy Manager) when exposed to the internet.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

// Create or upgrade the database on startup.
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

await app.Services.GetRequiredService<AccountService>().ClaimOwnerAccountAsync(ownerEmail, legacyPasswordHash);

if (googleEnabled)
{
    app.MapGet("/auth/google", (string? returnUrl) =>
        Results.Challenge(new AuthenticationProperties
        {
            RedirectUri = "/auth/google/done?returnUrl=" + Uri.EscapeDataString(SignInOptions.SafeReturnUrl(returnUrl)),
        }, [GoogleDefaults.AuthenticationScheme]));

    app.MapGet("/auth/google/done", async (HttpContext ctx, AccountService accounts, string? returnUrl) =>
    {
        var external = await ctx.AuthenticateAsync(SignInOptions.ExternalScheme);
        await ctx.SignOutAsync(SignInOptions.ExternalScheme);
        var p = external.Principal;
        var subject = p?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!external.Succeeded || string.IsNullOrEmpty(subject)) return Results.Redirect("/login?error=google");

        var verified = string.Equals(p!.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        var account = await accounts.SignInWithGoogleAsync(subject,
            p.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value, p.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value, verified);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AccountService.Principal(account),
            new AuthenticationProperties { IsPersistent = true });
        return Results.Redirect(SignInOptions.SafeReturnUrl(returnUrl));
    });
}

app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
return 0;
