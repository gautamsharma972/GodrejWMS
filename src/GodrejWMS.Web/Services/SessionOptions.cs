namespace GodrejWMS.Web.Services;

/// <summary>Bound from the "Session" config section. Governs both the auth cookie's sliding
/// expiration and the client-side idle-activity watcher, so the two stay in lockstep.
/// Named distinctly from <see cref="Microsoft.AspNetCore.Builder.SessionOptions"/> (ASP.NET
/// Core's unrelated session-middleware options type) to avoid an ambiguous reference.</summary>
public sealed class IdleSessionOptions
{
    public int IdleTimeoutMinutes { get; set; } = 20;
}
