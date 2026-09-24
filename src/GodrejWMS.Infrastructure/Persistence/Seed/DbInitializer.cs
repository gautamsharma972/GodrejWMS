using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GodrejWMS.Infrastructure.Persistence.Seed;

/// <summary>
/// Applies pending migrations and seeds roles, a default admin account, and reference master
/// data (design types, season/month mapping, put-away consolidation levels, demo materials).
/// Rack/pallet-position/location layout is never auto-seeded — the warehouse layout is entirely
/// operator-defined.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbInitializer));

        var db = provider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        await SeedRolesAsync(provider);
        await SeedAdminUserAsync(provider, logger);
        await SeedSampleUsersAsync(provider, logger);
        await AssignUsersToGcplAsync(provider, db);
        await SeedLocationSubtypesAsync(db);
        await SeedSkuMovementTypesAsync(db);
        await SeedZoneTypesAsync(db);
        await SeedLocationTypesAsync(db);
        await SeedSeasonsAsync(db);
        await SeedMasterDataAsync(db);
        await SeedSeasonMonthMapAsync(db);
        await SeedMovementReasonsAsync(db);
        await EnsureDemoMaterialsAsync(db);
    }

    private static async Task AssignUsersToGcplAsync(IServiceProvider provider, AppDbContext db)
    {
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var users = await userManager.Users.Select(u => u.Id).ToListAsync();
        var assigned = await db.UserWarehouses.IgnoreQueryFilters()
            .Where(a => a.WarehouseId == 1).Select(a => a.UserId).ToListAsync();
        db.UserWarehouses.AddRange(users.Except(assigned).Select(id =>
            new UserWarehouse { UserId = id, WarehouseId = 1 }));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the fixed set of location/stock subtypes in a fixed order so their auto-increment
    /// Ids land at 1=Good, 2=Damage, 3=Expire, 4=Hold - matching the values the retired
    /// LocationSubtype enum used to store in the same database columns.
    /// </summary>
    private static async Task SeedLocationSubtypesAsync(AppDbContext db)
    {
        if (await db.LocationSubtypes.AnyAsync())
        {
            return;
        }

        db.LocationSubtypes.AddRange(
            new LocationSubtype { Code = LocationSubtypeCodes.Good, DisplayName = "Good", Description = "Saleable stock location", SortOrder = 10, IsActive = true },
            new LocationSubtype { Code = LocationSubtypeCodes.Damage, DisplayName = "Damage", Description = "Damaged stock location", SortOrder = 20, IsActive = true },
            new LocationSubtype { Code = LocationSubtypeCodes.Expire, DisplayName = "Expire", Description = "Expired or near-expiry stock location", SortOrder = 30, IsActive = true },
            new LocationSubtype { Code = LocationSubtypeCodes.Hold, DisplayName = "Hold", Description = "Quality hold location", SortOrder = 40, IsActive = true });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the fixed set of SKU movement types in a fixed order so their auto-increment Ids
    /// land at 1=FastMoving, 2=SlowMoving - matching the values the retired SkuMovementType enum
    /// used to store in the same database column.
    /// </summary>
    private static async Task SeedSkuMovementTypesAsync(AppDbContext db)
    {
        if (await db.SkuMovementTypes.AnyAsync())
        {
            return;
        }

        db.SkuMovementTypes.AddRange(
            new SkuMovementType { Code = SkuMovementTypeCodes.FastMoving, DisplayName = "Fast-moving", Description = "High velocity SKU", SortOrder = 10, IsActive = true },
            new SkuMovementType { Code = SkuMovementTypeCodes.SlowMoving, DisplayName = "Slow-moving", Description = "Lower velocity SKU", SortOrder = 20, IsActive = true });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the fixed set of zone types in a fixed order so their auto-increment Ids land at
    /// 1=Fast, 2=Reserve, 3=Seasonal, 4=DispatchNear - matching the values the retired ZoneType
    /// enum used to store in the same database column.
    /// </summary>
    private static async Task SeedZoneTypesAsync(AppDbContext db)
    {
        if (await db.ZoneTypes.AnyAsync())
        {
            return;
        }

        db.ZoneTypes.AddRange(
            new ZoneType { Code = ZoneTypeCodes.Fast, DisplayName = "Fast", Description = "Fast moving pick-friendly zone", SortOrder = 10, IsActive = true },
            new ZoneType { Code = ZoneTypeCodes.Reserve, DisplayName = "Reserve", Description = "Reserve storage zone", SortOrder = 20, IsActive = true },
            new ZoneType { Code = ZoneTypeCodes.Seasonal, DisplayName = "Seasonal", Description = "Current-season priority zone", SortOrder = 30, IsActive = true },
            new ZoneType { Code = ZoneTypeCodes.DispatchNear, DisplayName = "Dispatch-near", Description = "Near-dispatch zone", SortOrder = 40, IsActive = true });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the fixed set of location types in a fixed order so their auto-increment Ids land at
    /// 1=Rack, 2=Pallet, 3=Floor, 4=Yard - matching the values the retired LocationType enum used
    /// to store in the same database columns.
    /// </summary>
    private static async Task SeedLocationTypesAsync(AppDbContext db)
    {
        if (await db.LocationTypes.AnyAsync())
        {
            return;
        }

        db.LocationTypes.AddRange(
            new LocationType { Code = LocationTypeCodes.Rack, DisplayName = "Rack", Description = "Rack storage location", SortOrder = 10, IsActive = true },
            new LocationType { Code = LocationTypeCodes.Pallet, DisplayName = "Pallet", Description = "Standalone pallet storage location", SortOrder = 20, IsActive = true },
            new LocationType { Code = LocationTypeCodes.Floor, DisplayName = "Floor", Description = "Floor storage location", SortOrder = 30, IsActive = true },
            new LocationType { Code = LocationTypeCodes.Yard, DisplayName = "Yard", Description = "Yard storage location", SortOrder = 40, IsActive = true });

        await db.SaveChangesAsync();
    }

    /// <summary>Seeds the fixed set of Inventory Movement reasons - no equivalent master existed
    /// in this app before this feature.</summary>
    private static async Task SeedMovementReasonsAsync(AppDbContext db)
    {
        if (await db.MovementReasons.AnyAsync())
        {
            return;
        }

        db.MovementReasons.AddRange(
            new MovementReason { Code = MovementReasonCodes.RackReorganization, DisplayName = "Rack Reorganization", SortOrder = 10, IsActive = true },
            new MovementReason { Code = MovementReasonCodes.SpaceOptimization, DisplayName = "Space Optimization", SortOrder = 20, IsActive = true },
            new MovementReason { Code = MovementReasonCodes.StockConsolidation, DisplayName = "Stock Consolidation", SortOrder = 30, IsActive = true },
            new MovementReason { Code = MovementReasonCodes.OperationalRequirement, DisplayName = "Operational Requirement", SortOrder = 40, IsActive = true },
            new MovementReason { Code = MovementReasonCodes.SupervisorCorrection, DisplayName = "Supervisor Correction", SortOrder = 50, IsActive = true });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the fixed set of seasons at explicit, never-auto-generated Ids matching the retired
    /// Season enum's values (0=Rainy, 1=Summer, 2=Winter) - Rainy's 0 is why Season.Id is
    /// configured as non-generated (see SeasonConfiguration) rather than auto-increment.
    /// </summary>
    private static async Task SeedSeasonsAsync(AppDbContext db)
    {
        if (await db.Seasons.AnyAsync())
        {
            return;
        }

        db.Seasons.AddRange(
            new Season { Id = SeasonIds.Rainy, Code = SeasonCodes.Rainy, DisplayName = "Rainy", Description = "Rainy season demand", SortOrder = 10, IsActive = true },
            new Season { Id = SeasonIds.Summer, Code = SeasonCodes.Summer, DisplayName = "Summer", Description = "Summer season demand", SortOrder = 20, IsActive = true },
            new Season { Id = SeasonIds.Winter, Code = SeasonCodes.Winter, DisplayName = "Winter", Description = "Winter season demand", SortOrder = 30, IsActive = true });

        await db.SaveChangesAsync();
    }

    private static async Task SeedRolesAsync(IServiceProvider provider)
    {
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task SeedAdminUserAsync(IServiceProvider provider, ILogger logger)
    {
        var config = provider.GetRequiredService<IConfiguration>();
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        var adminEmail = config["DefaultAdmin:Email"] ?? "admin@godrejwms.local";
        var adminPassword = config["DefaultAdmin:Password"] ?? "Godrej@WMS123!";

        var existing = await userManager.FindByEmailAsync(adminEmail);
        if (existing is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FullName = "Warehouse Administrator",
            IsActive = true
        };

        var result = await userManager.CreateAsync(admin, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, RoleNames.Admin);
            logger.LogInformation("Seeded default admin account {Email}. Change the password after first login.", adminEmail);
        }
        else
        {
            logger.LogWarning("Failed to seed default admin account: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>
    /// Seeds one demo login per non-admin role, so the app can be exercised as a Supervisor and
    /// an Operator without hand-creating accounts. Same throwaway password as the default admin -
    /// change it before using these outside a local/demo environment.
    /// </summary>
    private static async Task SeedSampleUsersAsync(IServiceProvider provider, ILogger logger)
    {
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        const string samplePassword = "Godrej@WMS123!";

        var sampleUsers = new[]
        {
            (Email: "supervisor@godrejwms.local", FullName: "Warehouse Supervisor", Role: RoleNames.Supervisor),
            (Email: "operator@godrejwms.local", FullName: "Warehouse Operator", Role: RoleNames.Operator)
        };

        foreach (var sample in sampleUsers)
        {
            if (await userManager.FindByEmailAsync(sample.Email) is not null)
            {
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = sample.Email,
                Email = sample.Email,
                EmailConfirmed = true,
                FullName = sample.FullName,
                IsActive = true
            };

            var result = await userManager.CreateAsync(user, samplePassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, sample.Role);
                logger.LogInformation("Seeded sample {Role} account {Email}.", sample.Role, sample.Email);
            }
            else
            {
                logger.LogWarning("Failed to seed sample {Role} account: {Errors}",
                    sample.Role, string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
    }

    private static async Task EnsureDemoMaterialsAsync(AppDbContext db)
    {
        var existingMaterialNumbers = await db.Materials
            .Select(m => m.MaterialNumber)
            .ToListAsync();
        var existing = existingMaterialNumbers.ToHashSet();

        var samples = new[]
        {
            NewMaterial(40034354, "HIT CIK 125ML MRP75 P90", "XOLDH", "SHAC1", 90, 75, 50, 29.3m, 34, 0.08m, 0.152m, 40, SkuMovementTypeIds.FastMoving, SeasonIds.Rainy),
            NewMaterial(40058047, "HIT CIK 400ML M189 P36 0.18", "XOLDH", "SHAC4", 90, 189, 49.8m, 23.3m, 32.3m, 0.256m, 0.371m, 40, SkuMovementTypeIds.FastMoving, SeasonIds.Rainy),
            NewMaterial(40063045, "HIT FIK 200ML M125P60 CAN .18MM MFT NEW", "XOLDI", "SHAF2", 90, 125, 55.5m, 33.5m, 23.7m, 0.124m, 0.207m, 40, SkuMovementTypeIds.SlowMoving, SeasonIds.Summer),
            NewMaterial(40063500, "HIT FIK 125ML M90 P90 NEW L MFT", "XOLDJ", "SHAF1", 90, 90, 50, 33.5m, 29.3m, 0.077m, 0.147m, 40, SkuMovementTypeIds.SlowMoving, SeasonIds.Winter),
            NewMaterial(50010001, "GODREJ NO.1 SOAP 100G CARTON", "SOAPWRP", "SOAP100", 120, 40, 480, 320, 260, 0.1m, 0.118m, 40, SkuMovementTypeIds.FastMoving, SeasonIds.Summer),
            NewMaterial(50010002, "CINTHOL SOAP 75G VALUE PACK CARTON", "SOAPBOX", "SOAP075", 144, 55, 520, 340, 280, 0.075m, 0.092m, 40, SkuMovementTypeIds.SlowMoving, SeasonIds.Winter),
        };

        var missing = samples.Where(m => !existing.Contains(m.MaterialNumber)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        db.Materials.AddRange(missing);
        await db.SaveChangesAsync();
    }

    private static async Task SeedMasterDataAsync(AppDbContext db)
    {
        var existingCodes = await db.DesignTypes
            .Select(d => d.Code)
            .ToListAsync();
        var existingCodeSet = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sampleDesignTypes = new[]
        {
            NewDesignType("XOLDH", "Legacy aerosol HIT can design - size H"),
            NewDesignType("XOLDI", "Legacy aerosol HIT can design - size I"),
            NewDesignType("XOLDJ", "Legacy aerosol HIT can design - size J"),
            NewDesignType("HITCIK", "Godrej HIT CIK cans"),
            NewDesignType("HITFIK", "Godrej HIT FIK cans"),
            NewDesignType("SOAPWRP", "Godrej soap wrapped carton"),
            NewDesignType("SOAPBOX", "Godrej soap boxed SKU"),
            NewDesignType("AEROSOL", "Aerosol consumer product carton"),
            NewDesignType("SEASONAL", "Seasonal demand SKU packaging")
        };

        var missing = sampleDesignTypes
            .Where(d => !existingCodeSet.Contains(d.Code))
            .ToList();

        if (missing.Count > 0)
        {
            db.DesignTypes.AddRange(missing);
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedSeasonMonthMapAsync(AppDbContext db)
    {
        var existing = await db.SeasonMonthMaps
            .ToDictionaryAsync(s => s.Month);

        var defaults = new Dictionary<int, int>
        {
            [1] = SeasonIds.Winter,
            [2] = SeasonIds.Winter,
            [3] = SeasonIds.Summer,
            [4] = SeasonIds.Summer,
            [5] = SeasonIds.Summer,
            [6] = SeasonIds.Summer,
            [7] = SeasonIds.Rainy,
            [8] = SeasonIds.Rainy,
            [9] = SeasonIds.Rainy,
            [10] = SeasonIds.Rainy,
            [11] = SeasonIds.Winter,
            [12] = SeasonIds.Winter
        };

        foreach (var (month, seasonId) in defaults)
        {
            if (!existing.TryGetValue(month, out var row))
            {
                db.SeasonMonthMaps.Add(new SeasonMonthMap
                {
                    Month = month,
                    SeasonId = seasonId,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        if (existing.Count == 12 && existing.Values.All(row => row.SeasonId == SeasonIds.Rainy))
        {
            foreach (var (month, seasonId) in defaults)
            {
                existing[month].SeasonId = seasonId;
                existing[month].UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await db.SaveChangesAsync();
    }

    private static DesignType NewDesignType(string code, string description) => new()
    {
        Code = code,
        Description = description,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static Material NewMaterial(
        long materialNumber, string description, string designType, string characteristicValue,
        int packSize, decimal mrpPrice, decimal lengthMm, decimal widthMm, decimal heightMm,
        decimal netWeightKg, decimal grossWeightKg, int palletCapacityBoxes, int movementTypeId, int seasonId)
    {
        var material = new Material
        {
            MaterialNumber = materialNumber,
            Description = description,
            DesignType = designType,
            CharacteristicValue = characteristicValue,
            PackSize = packSize,
            MrpPrice = mrpPrice,
            LengthMm = lengthMm,
            WidthMm = widthMm,
            HeightMm = heightMm,
            NetWeightKg = netWeightKg,
            GrossWeightKg = grossWeightKg,
            PalletCapacityBoxes = palletCapacityBoxes,
            MovementTypeId = movementTypeId,
            SeasonId = seasonId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        material.RecalculateDerivedFields();
        return material;
    }
}


