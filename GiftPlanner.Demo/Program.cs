using GiftPlanner.Auth;
using GiftPlanner.Components;
using GiftPlanner.Data;
using GiftPlanner.Demo;
using GiftPlanner.Gifts;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
// SQLite runs inside the page; the file lives in the browser's in-memory file system.
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=giftplanner.db"));
builder.Services.AddScoped<IAccountContext, ClaimsAccountContext>();
builder.Services.AddScoped<GiftService>();
builder.Services.AddScoped(sp => new ProductLookup(new HttpClient(),
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<ProductLookup>()));

// Every visitor is the signed-in parent.
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, DemoAuthState>();

var host = builder.Build();
await DemoData.SeedAsync(host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>());
await host.RunAsync();
