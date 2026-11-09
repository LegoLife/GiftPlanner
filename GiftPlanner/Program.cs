using GiftPlanner.Auth;
using GiftPlanner.Components;
using GiftPlanner.Data;
using GiftPlanner.Gifts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

// `dotnet GiftPlanner.dll hash-password [password]` prints a hash for GiftPlanner__PasswordHash.
if (args is ["hash-password", .. var rest])
{
    var pw = rest.Length > 0 ? rest[0] : Console.ReadLine();
    if (string.IsNullOrEmpty(pw)) { Console.Error.WriteLine("Password can't be empty."); return 1; }
    Console.WriteLine(PasswordVerifier.Hash(pw));
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

var passwordHash = builder.Configuration["GiftPlanner:PasswordHash"];
if (string.IsNullOrWhiteSpace(passwordHash))
    throw new InvalidOperationException(
        "GiftPlanner:PasswordHash is not set. Generate one with `dotnet run -- hash-password <pw>` " +
        "(or `docker run --rm -i giftplanner hash-password`) and set GiftPlanner__PasswordHash.");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<GiftService>();
builder.Services.AddHttpClient<ProductLookup>(ProductLookup.Configure)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        MaxAutomaticRedirections = 5,
    });

builder.Services.AddSingleton(new PasswordVerifier(passwordHash));

// Keys persist with the data so sign-ins survive container restarts.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")))
    .SetApplicationName("GiftPlanner");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
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
    });

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
