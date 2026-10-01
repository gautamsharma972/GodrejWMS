namespace GodrejWMS.Web.Components.Layout;

/// <summary>One entry the global topbar search can navigate to. <see cref="Roles"/> mirrors that
/// page's own <c>[Authorize(Roles = ...)]</c> attribute so the search never surfaces a page the
/// current user can't open; null means any authenticated user. <see cref="Keywords"/> are extra
/// terms (jargon, synonyms) that should also match this entry beyond its title.</summary>
public sealed record SearchCatalogEntry(string Title, string Route, string Group, string Icon, string[] Keywords, string[]? Roles = null);

/// <summary>The single source of truth for what the global search box can find - kept separate
/// from NavMenu.razor because search also needs to match keywords/roles, not just render a link.
/// Add a new page here as well as to NavMenu when either changes.</summary>
public static class AppSearchCatalog
{
    public static readonly IReadOnlyList<SearchCatalogEntry> Entries =
    [
        new("Dashboard", "", "Operate", "bi-speedometer2", ["home", "overview"]),
        new("Inventory Master", "inventory-master", "Operate", "bi-database-check", ["stock", "current stock", "sku stock", "pkm"]),
        new("Racks & Pallets", "racks", "Operate", "bi-grid-3x3-gap", ["rack", "pallet", "layout"]),
        new("Location Master", "locations", "Operate", "bi-geo-alt", ["location", "bin", "position", "pallet position"]),
        new("Materials", "materials", "Operate", "bi-boxes", ["material", "sku", "product", "material master"]),

        new("Design Types", "design-types", "Master Data", "bi-tags", ["design"]),
        new("Season Mapping", "season-mapping", "Master Data", "bi-calendar-range", ["season", "month"]),
        new("Location Subtypes", "location-subtypes", "Master Data", "bi-tags", ["good", "damage", "expire", "hold"]),
        new("SKU Movement Types", "sku-movement-types", "Master Data", "bi-speedometer2", ["velocity", "fast moving", "slow moving"]),
        new("Zone Types", "zone-types", "Master Data", "bi-bullseye", ["zone", "fast", "reserve", "seasonal", "dispatch"]),
        new("Location Types", "location-types", "Master Data", "bi-diagram-3", ["rack", "floor", "yard"]),
        new("Seasons", "seasons", "Master Data", "bi-cloud-sun", ["rainy", "summer", "winter"]),
        new("Audit Trail", "audit-trail", "Master Data", "bi-clipboard-data", ["log", "activity", "history"], ["Admin", "Supervisor", "Operator"]),

        new("Inward Entry", "inward", "Transactions", "bi-pencil-square", ["grn", "goods receipt", "receive", "receiving"], ["Admin", "Operator", "Supervisor"]),
        new("Inward History", "inward/history", "Transactions", "bi-clock-history", ["grn history"], ["Admin", "Operator", "Supervisor"]),
        new("Pullout Entry", "pullout", "Transactions", "bi-pencil-square", ["outward", "pick", "picking", "dispatch"], ["Admin", "Operator", "Supervisor"]),
        new("Pullout History", "pullout/history", "Transactions", "bi-clock-history", ["outward history"], ["Admin", "Operator", "Supervisor"]),
        new("Move by Location", "inventory-movement/by-location", "Transactions", "bi-geo-alt", ["relocate", "transfer", "movement"], ["Admin", "Supervisor", "Operator"]),
        new("Move by SKU", "inventory-movement/by-sku", "Transactions", "bi-upc-scan", ["relocate", "transfer", "movement"], ["Admin", "Supervisor", "Operator"]),
        new("Movement History", "inventory-movement/history", "Transactions", "bi-clock-history", ["relocation history"], ["Admin", "Supervisor", "Operator"]),

        new("Operational Reports", "reports/operations", "Reports", "bi-table", ["report"], ["Admin", "Supervisor", "Operator"]),
        new("Inward Reports", "reports/inward", "Reports", "bi-box-arrow-in-down", ["grn report", "putaway", "rejection", "turnaround"], ["Admin", "Supervisor", "Operator"]),
        new("Pullout Reports", "reports/pullout", "Reports", "bi-box-arrow-up", ["shortage", "fifo", "picking report"], ["Admin", "Supervisor", "Operator"]),
        new("Inventory Reports", "reports/inventory", "Reports", "bi-bar-chart", ["aging", "valuation", "capacity", "slow moving"], ["Admin", "Supervisor", "Operator"]),
        new("Movement Reports", "reports/movement", "Reports", "bi-arrow-left-right", ["churn", "reasons"], ["Admin", "Supervisor", "Operator"]),
        new("AI Assistant", "reports/ai-assistant", "Reports", "bi-stars", ["ask", "chat", "question", "assistant"], ["Admin", "Supervisor", "Operator"])
    ];

    public static bool IsVisibleTo(SearchCatalogEntry entry, IReadOnlyCollection<string> roles) =>
        entry.Roles is null || entry.Roles.Any(roles.Contains);
}
