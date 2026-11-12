using GiftPlanner.Auth;
using GiftPlanner.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace GiftPlanner.Tests;

public sealed class AccountServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestDbFactory _factory;
    private readonly AccountService _svc;

    public AccountServiceTests()
    {
        _connection.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
        _svc = new AccountService(_factory);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Register_then_sign_in_with_any_email_casing()
    {
        var r = await _svc.RegisterAsync("Pat", "Pat@Example.com", "correct horse");
        Assert.Null(r.Error);
        Assert.Equal(r.Account!.Id, (await _svc.VerifyPasswordAsync(" pat@example.COM ", "correct horse"))?.Id);
        Assert.Null(await _svc.VerifyPasswordAsync("pat@example.com", "wrong password"));
        Assert.Null(await _svc.VerifyPasswordAsync("nobody@example.com", "correct horse"));
    }

    [Theory]
    [InlineData("", "a@b.co", "longenough", "name")]
    [InlineData("Pat", "not-an-email", "longenough", "valid email")]
    [InlineData("Pat", "a@localhost", "longenough", "valid email")]
    [InlineData("Pat", "a@b.co", "short", "at least 8")]
    public async Task Register_rejects_bad_input(string name, string email, string password, string expected)
    {
        var r = await _svc.RegisterAsync(name, email, password);
        Assert.Null(r.Account);
        Assert.Contains(expected, r.Error);
    }

    [Fact]
    public async Task Register_rejects_an_email_already_in_use()
    {
        await _svc.RegisterAsync("Pat", "pat@example.com", "correct horse");
        var r = await _svc.RegisterAsync("Imposter", "PAT@example.com", "other password");
        Assert.Null(r.Account);
        Assert.Contains("already", r.Error);
    }

    [Fact]
    public async Task Google_creates_an_account_once_then_finds_it_again()
    {
        var a = await _svc.SignInWithGoogleAsync("sub-1", "kim@gmail.com", "Kim", emailVerified: true);
        var b = await _svc.SignInWithGoogleAsync("sub-1", "kim@gmail.com", "Kim", emailVerified: true);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal("Kim", a.DisplayName);
        Assert.True(a.EmailVerified);
    }

    [Fact]
    public async Task Google_links_to_a_password_account_and_drops_its_unverified_password()
    {
        // Someone signs up with Kim's address before Kim does; Kim then uses Google.
        var squatter = (await _svc.RegisterAsync("Squatter", "kim@gmail.com", "squatter pw")).Account!;
        var kim = await _svc.SignInWithGoogleAsync("sub-kim", "kim@gmail.com", "Kim", emailVerified: true);

        Assert.Equal(squatter.Id, kim.Id);
        Assert.Null(await _svc.VerifyPasswordAsync("kim@gmail.com", "squatter pw"));
    }

    [Fact]
    public async Task Google_keeps_the_password_of_an_already_verified_account()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Accounts.Add(new Account { Id = AccountService.OwnerAccountId, DisplayName = "Owner" });
            await db.SaveChangesAsync();
        }
        await _svc.ClaimOwnerAccountAsync("me@gmail.com", AccountService.HashPassword("old admin pw"));
        var viaGoogle = await _svc.SignInWithGoogleAsync("sub-me", "me@gmail.com", "Me", emailVerified: true);

        Assert.Equal(AccountService.OwnerAccountId, viaGoogle.Id);
        Assert.Equal(AccountService.OwnerAccountId, (await _svc.VerifyPasswordAsync("me@gmail.com", "old admin pw"))?.Id);
    }

    [Fact]
    public async Task Google_never_links_by_an_unverified_email()
    {
        var existing = (await _svc.RegisterAsync("Pat", "pat@example.com", "correct horse")).Account!;
        var other = await _svc.SignInWithGoogleAsync("sub-x", "pat@example.com", "X", emailVerified: false);
        Assert.NotEqual(existing.Id, other.Id);
        Assert.Null(other.Email);
    }

    [Fact]
    public void Return_urls_stay_on_this_site()
    {
        Assert.Equal("/setup", SignInOptions.SafeReturnUrl("/setup"));
        Assert.Equal("/", SignInOptions.SafeReturnUrl("//evil.com"));
        Assert.Equal("/", SignInOptions.SafeReturnUrl("/\\evil.com"));
        Assert.Equal("/", SignInOptions.SafeReturnUrl("https://evil.com"));
        Assert.Equal("/", SignInOptions.SafeReturnUrl(null));
    }
}

public sealed class SignupGuardTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
    private readonly SignupGuard _guard;

    public SignupGuardTests() => _guard = new SignupGuard(new EphemeralDataProtectionProvider(), _clock);

    private string FilledForm(TimeSpan typingTime)
    {
        var token = _guard.IssueFormToken();
        _clock.Advance(typingTime);
        return token;
    }

    [Fact]
    public void A_person_who_takes_a_few_seconds_gets_through() =>
        Assert.Equal(SignupCheck.Ok, _guard.Check(FilledForm(TimeSpan.FromSeconds(20)), "", "1.1.1.1"));

    [Fact]
    public void Filling_the_honeypot_is_a_bot() =>
        Assert.Equal(SignupCheck.Bot, _guard.Check(FilledForm(TimeSpan.FromSeconds(20)), "http://spam", "1.1.1.1"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("forged-token")]
    public void A_missing_or_forged_form_token_is_a_bot(string? token) =>
        Assert.Equal(SignupCheck.Bot, _guard.Check(token, null, "1.1.1.1"));

    [Fact]
    public void Submitting_instantly_is_too_fast() =>
        Assert.Equal(SignupCheck.TooFast, _guard.Check(FilledForm(TimeSpan.FromMilliseconds(300)), null, "1.1.1.1"));

    [Fact]
    public void An_old_form_has_expired() =>
        Assert.Equal(SignupCheck.Expired, _guard.Check(FilledForm(TimeSpan.FromHours(3)), null, "1.1.1.1"));

    [Fact]
    public void Each_ip_gets_a_few_sign_ups_an_hour()
    {
        for (var i = 0; i < SignupGuard.SignupsPerHour; i++)
            Assert.Equal(SignupCheck.Ok, _guard.Check(FilledForm(TimeSpan.FromSeconds(10)), null, "1.1.1.1"));

        Assert.Equal(SignupCheck.RateLimited, _guard.Check(FilledForm(TimeSpan.FromSeconds(10)), null, "1.1.1.1"));
        Assert.Equal(SignupCheck.Ok, _guard.Check(FilledForm(TimeSpan.FromSeconds(10)), null, "2.2.2.2"));

        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(SignupCheck.Ok, _guard.Check(FilledForm(TimeSpan.FromSeconds(10)), null, "1.1.1.1"));
    }

    [Fact]
    public void Password_sign_in_attempts_are_limited_per_ip()
    {
        for (var i = 0; i < SignupGuard.LoginsPer15Minutes; i++) Assert.True(_guard.AllowLoginAttempt("1.1.1.1"));
        Assert.False(_guard.AllowLoginAttempt("1.1.1.1"));
        _clock.Advance(TimeSpan.FromMinutes(15));
        Assert.True(_guard.AllowLoginAttempt("1.1.1.1"));
    }
}
