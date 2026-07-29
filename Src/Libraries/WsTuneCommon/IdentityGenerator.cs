namespace WsTuneCommon;

/// <summary>
/// Resolves the per-instance routing identity used as the SignalR group / Origin address.
///
/// A client's identity is only a return address, so it must be unique per running instance —
/// otherwise multiple copies of a shared client app join the same group and cross-talk.
/// A server's identity is the destination clients target, so it must stay stable; we honor a
/// provided value and only auto-generate as a fallback.
/// </summary>
public static class IdentityGenerator
{
    public static string NewId(string? prefix = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        prefix = prefix?.Trim();
        return string.IsNullOrEmpty(prefix) ? suffix : $"{prefix}-{suffix}";
    }

    /// <summary>
    /// Clients always get a unique identity. A configured value, if any, is kept only as a
    /// human-readable prefix so logs stay recognizable; uniqueness is still guaranteed.
    /// </summary>
    public static string ResolveClient(string? configured)
        => NewId(string.IsNullOrWhiteSpace(configured) ? "client" : configured);

    /// <summary>
    /// Servers prefer a configured identity (clients route to it) and fall back to an
    /// auto-generated one when none is provided.
    /// </summary>
    public static string ResolveServer(string? configured)
    {
        var normalized = configured?.Trim();
        if (normalized is null || normalized.Length == 0)
        {
            return NewId("server");
        }

        return normalized;
    }
}
