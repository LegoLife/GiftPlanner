using System.Net.Mail;
using System.Security.Claims;
using GiftPlanner.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GiftPlanner.Auth;

public sealed record RegisterResult(Account? Account, string? Error);

/// <summary>Creates accounts and checks sign-ins (email + password, or Google).</summary>
public sealed class AccountService(IDbContextFactory<AppDbContext> dbFactory)
{
    public const int MinPasswordLength = 8;
    /// <summary>The migration gives everything that existed before accounts to this one.</summary>
    public const int OwnerAccountId = 1;

    private static readonly PasswordHasher<Account> Hasher = new();

    /// <summary>For the `hash-password` command.</summary>
    public static string HashPassword(string password) => Hasher.HashPassword(new Account(), password);

    public static string? Normalize(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return false;
        return MailAddress.TryCreate(email.Trim(), out var a) && a.Address == email.Trim() && a.Host.Contains('.');
    }

    public async Task<RegisterResult> RegisterAsync(string? name, string? email, string? password)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 80) return new(null, "Please enter your name (up to 80 characters).");
        if (!IsValidEmail(email)) return new(null, "Please enter a valid email address.");
        if (password is null || password.Length < MinPasswordLength)
            return new(null, $"Please use a password of at least {MinPasswordLength} characters.");

        var normalized = Normalize(email)!;
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Accounts.AnyAsync(a => a.NormalizedEmail == normalized))
            return new(null, "There's already an account with that email. Sign in instead.");

        var account = new Account { DisplayName = name, Email = email!.Trim(), NormalizedEmail = normalized };
        account.PasswordHash = Hasher.HashPassword(account, password);
        db.Accounts.Add(account);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return new(null, "There's already an account with that email. Sign in instead."); }
        return new(account, null);
    }

    /// <summary>The account, or null if the email/password don't match.</summary>
    public async Task<Account?> VerifyPasswordAsync(string? email, string? password)
    {
        var normalized = Normalize(email);
        if (normalized is null || string.IsNullOrEmpty(password)) return null;
        await using var db = await dbFactory.CreateDbContextAsync();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.NormalizedEmail == normalized);
        if (account?.PasswordHash is null)
        {
            Hasher.HashPassword(new Account(), password); // same work either way, so timing doesn't reveal which emails exist
            return null;
        }
        var result = Hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = Hasher.HashPassword(account, password);
            await db.SaveChangesAsync();
        }
        return account;
    }

    /// <summary>
    /// Finds the account for a Google sign-in, linking it to an existing account with the same email,
    /// or creating a new one. Linking only happens for emails Google has verified. If the matching account's
    /// email was never verified, its password is removed: whoever set it may not own that inbox.
    /// </summary>
    public async Task<Account> SignInWithGoogleAsync(string subject, string? email, string? name, bool emailVerified)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.GoogleSubject == subject);
        if (account is not null) return account;

        var normalized = emailVerified ? Normalize(email) : null;
        account = normalized is null ? null : await db.Accounts.FirstOrDefaultAsync(a => a.NormalizedEmail == normalized);
        if (account is not null)
        {
            account.GoogleSubject = subject;
            if (!account.EmailVerified) account.PasswordHash = null;
            account.EmailVerified = true;
        }
        else
        {
            account = new Account
            {
                DisplayName = string.IsNullOrWhiteSpace(name) ? (email?.Split('@')[0] ?? "Me") : name.Trim(),
                Email = normalized is null ? null : email!.Trim(),
                NormalizedEmail = normalized,
                GoogleSubject = subject,
                EmailVerified = normalized is not null,
            };
            db.Accounts.Add(account);
        }
        await db.SaveChangesAsync();
        return account;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Accounts.AnyAsync(a => a.Id == id);
    }

    /// <summary>
    /// Gives the pre-accounts owner (account 1, made by the migration) the email and password from
    /// configuration, so the old login keeps working as email + the same password.
    /// </summary>
    public async Task ClaimOwnerAccountAsync(string? ownerEmail, string? legacyPasswordHash)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var owner = await db.Accounts.FirstOrDefaultAsync(a => a.Id == OwnerAccountId);
        if (owner is null) return;
        var normalized = Normalize(ownerEmail);
        if (owner.NormalizedEmail is null && normalized is not null && IsValidEmail(ownerEmail)
            && !await db.Accounts.AnyAsync(a => a.NormalizedEmail == normalized))
        {
            owner.Email = ownerEmail!.Trim();
            owner.NormalizedEmail = normalized;
            owner.EmailVerified = true; // set by whoever runs the server
        }
        if (owner.PasswordHash is null && !string.IsNullOrWhiteSpace(legacyPasswordHash))
            owner.PasswordHash = legacyPasswordHash; // PasswordHasher output is the same format
        await db.SaveChangesAsync();
    }

    public static ClaimsPrincipal Principal(Account account) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, account.DisplayName),
            new Claim(ClaimTypes.Role, Roles.Admin),
        ], CookieAuthenticationDefaults.AuthenticationScheme));
}
