using System.Collections.Concurrent;
using System.Security.Cryptography;
using Dms.Api.Contracts;

namespace Dms.Api;

/// <summary>
/// Remembers who is signed in. Signing in issues a random token; the web UI sends it with every
/// request in the <c>Authorization: Bearer</c> header, and signing out forgets it.
/// </summary>
public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, UserSession> _sessions = new();

    /// <summary>Starts a session and returns its token.</summary>
    public string Start(UserSession session)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = session;
        return token;
    }

    /// <summary>Returns the session for the request's token, or null when not signed in.</summary>
    public UserSession? Find(HttpContext context)
    {
        string? token = TokenOf(context);
        return token is not null && _sessions.TryGetValue(token, out UserSession? session) ? session : null;
    }

    /// <summary>Ends the session for the request's token.</summary>
    public void End(HttpContext context)
    {
        string? token = TokenOf(context);
        if (token is not null)
        {
            _sessions.TryRemove(token, out _);
        }
    }

    private static string? TokenOf(HttpContext context)
    {
        string? header = context.Request.Headers.Authorization;
        const string prefix = "Bearer ";
        return header is not null && header.StartsWith(prefix, StringComparison.Ordinal) ? header[prefix.Length..].Trim() : null;
    }
}
