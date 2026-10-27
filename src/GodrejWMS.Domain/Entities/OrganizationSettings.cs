namespace GodrejWMS.Domain.Entities;

/// <summary>Vendor-configurable, org-wide settings for this single-tenant deployment - a
/// deliberate singleton row (always Id = 1), edited only from the hidden Platform Admin screen.
/// Null limits mean unlimited - the seeded default, so an existing deployment's behavior never
/// changes until a Platform Admin explicitly sets one.</summary>
public class OrganizationSettings
{
    public int Id { get; set; }

    public int? MaxUsers { get; set; }

    public int? MaxWarehouses { get; set; }
}
