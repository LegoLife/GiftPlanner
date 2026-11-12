using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

namespace GiftPlanner.Auth;

public enum SignupCheck { Ok, Bot, TooFast, Expired, RateLimited }

/// <summary>
/// Keyless bot checks for the sign-up form: a hidden honeypot field people never fill in, a signed
/// timestamp that rejects forms submitted faster than a person can type, and a per-IP limit.
/// Also limits password sign-in attempts per IP.
/// </summary>
public sealed class SignupGuard(IDataProtectionProvider protection, TimeProvider clock)
{
    public static readonly TimeSpan MinFillTime = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan MaxFormAge = TimeSpan.FromHours(2);
    public const int SignupsPerHour = 5;
    public const int LoginsPer15Minutes = 10;

    private readonly IDataProtector _protector = protection.CreateProtector("GiftPlanner.SignupForm");
    private readonly SlidingLimiter _signups = new(SignupsPerHour, TimeSpan.FromHours(1), clock);
    private readonly SlidingLimiter _logins = new(LoginsPer15Minutes, TimeSpan.FromMinutes(15), clock);

    /// <summary>Goes in a hidden field when the form is shown.</summary>
    public string IssueFormToken() =>
        _protector.Protect(clock.GetUtcNow().UtcTicks.ToString(CultureInfo.InvariantCulture));

    public SignupCheck Check(string? formToken, string? honeypot, string ip)
    {
        if (!string.IsNullOrEmpty(honeypot)) return SignupCheck.Bot;
        if (!TryReadIssued(formToken, out var issued)) return SignupCheck.Bot;
        var age = clock.GetUtcNow() - issued;
        if (age < MinFillTime) return SignupCheck.TooFast;
        if (age > MaxFormAge) return SignupCheck.Expired;
        return _signups.TryAcquire(ip) ? SignupCheck.Ok : SignupCheck.RateLimited;
    }

    public bool AllowLoginAttempt(string ip) => _logins.TryAcquire(ip);

    private bool TryReadIssued(string? token, out DateTimeOffset issued)
    {
        issued = default;
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            if (!long.TryParse(_protector.Unprotect(token), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return false;
            issued = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }

    /// <summary>At most <c>limit</c> events per key in any <c>window</c>.</summary>
    private sealed class SlidingLimiter(int limit, TimeSpan window, TimeProvider clock)
    {
        private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _hits = new();

        public bool TryAcquire(string key)
        {
            var now = clock.GetUtcNow();
            var q = _hits.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
            lock (q)
            {
                while (q.Count > 0 && now - q.Peek() >= window) q.Dequeue();
                if (q.Count >= limit) return false;
                q.Enqueue(now);
            }
            if (_hits.Count > 10_000) Prune(now); // keep memory bounded under a flood of IPs
            return true;
        }

        private void Prune(DateTimeOffset now)
        {
            foreach (var (key, q) in _hits)
                lock (q)
                    if (q.Count == 0 || now - q.Last() >= window) _hits.TryRemove(key, out _);
        }
    }
}
