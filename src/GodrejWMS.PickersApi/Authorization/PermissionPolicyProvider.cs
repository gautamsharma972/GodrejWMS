using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace GodrejWMS.PickersApi.Authorization;

/// <summary>Recognizes policy names of the form "Permission:&lt;key&gt;" (e.g.
/// "Permission:Inward.Create") and builds a one-off policy requiring that permission key on
/// demand, instead of needing a named AddPolicy(...) call per key up front. Every other policy
/// name falls back to the default provider unchanged.
/// Deliberate copy of GodrejWMS.Web.Authorization.PermissionPolicyProvider so this API project
/// never references GodrejWMS.Web (see the Native Android Pickers app plan).</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private const string Prefix = "Permission:";
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var key = policyName[Prefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(key))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}
