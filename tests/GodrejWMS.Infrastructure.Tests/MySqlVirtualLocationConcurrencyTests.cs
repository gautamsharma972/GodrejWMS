using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Runs only when GODREJWMS_TEST_MYSQL holds a connection string (without a database name) to a MySQL 8 server whose user may
/// create databases, e.g. "Server=localhost;Port=3306;User=root;Password=...;SslMode=None;". The EF in-memory provider used by the other
/// tests has no transactions, row locks or true concurrency, so this is the only place SKIP LOCKED and the row-locked sequence are proven.</summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public const string Variable = "GODREJWMS_TEST_MYSQL";

    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Set {Variable} to a MySQL connection string (no database name) to run the MySQL integration tests.";
        }
    }
}

public class MySqlVirtualLocationConcurrencyTests
{
    private static string Server => Environment.GetEnvironmentVariable(MySqlFactAttribute.Variable)!.TrimEnd(';') + ";";

    private static AppDbContext Open(string database) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseMySql($"{Server}Database={database};", new MySqlServerVersion(new Version(8, 0, 36)))
                .Options,
            new IntegrationUser(), new SvcClock());

    [MySqlFact]
    public async Task ConcurrentUploads_NeverShareAVirtualLocation_AndNeverProduceTheSameCode()
    {
        var database = $"godrejwms_it_{Guid.NewGuid():N}"[..30];
        await using (var setup = Open(database))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
            setup.LocationTypes.Add(new LocationType { Id = LocationTypeIds.Floor, Code = "FLOOR", DisplayName = "Floor" });
            setup.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "GOOD", DisplayName = "Good" });
            setup.ZoneTypes.Add(new ZoneType { Id = ZoneTypeIds.Overflow, Code = "Overflow", DisplayName = "Overflow" });
            await setup.SaveChangesAsync();

            // Four FREE virtual locations already exist, so the concurrent uploads race for reuse as well as for creation.
            var virtuals = new VirtualLocationService(setup, new SvcClock());
            var spare = new List<PalletPosition>();
            for (var i = 0; i < 4; i++)
            {
                spare.Add(await virtuals.AcquireAsync(1, null));
            }

            foreach (var location in spare)
            {
                await virtuals.ReleaseIfEmptyAsync(location);
            }

            await setup.SaveChangesAsync();
        }

        try
        {
            using var barrier = new Barrier(3);
            async Task<List<string>> UploadAsync()
            {
                await using var db = Open(database);
                var uow = new EfUnitOfWork(db);
                var virtuals = new VirtualLocationService(db, new SvcClock());
                barrier.SignalAndWait();
                return await uow.ExecuteInTransactionAsync(async ct =>
                {
                    var codes = new List<string>();
                    for (var i = 0; i < 4; i++)
                    {
                        codes.Add((await virtuals.AcquireAsync(1, null, ct)).LocationCode);
                    }

                    return codes;
                });
            }

            var tasks = Enumerable.Range(0, 3).Select(_ => Task.Run(UploadAsync)).ToArray();
            var results = (await Task.WhenAll(tasks)).SelectMany(codes => codes).ToList();

            Assert.Equal(12, results.Count);
            Assert.Equal(12, results.Distinct().Count()); // no location was handed to two uploads
            await using var check = Open(database);
            Assert.Equal(12, await check.PalletPositions.CountAsync(p => p.IsVirtual && p.VirtualStatus == VirtualLocationStatus.InUse));
        }
        finally
        {
            await using var cleanup = Open(database);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }
}

public class MySqlVirtualLocationCreationConcurrencyTests
{
    [MySqlFact]
    public async Task ConcurrentUploadsCreatingTheFirstVirtualLocations_GetDistinctCodes()
    {
        var server = Environment.GetEnvironmentVariable(MySqlFactAttribute.Variable)!.TrimEnd(';') + ";";
        var database = $"godrejwms_it_{Guid.NewGuid():N}"[..30];
        AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql($"{server}Database={database};", new MySqlServerVersion(new Version(8, 0, 36))).Options, new IntegrationUser(), new SvcClock());

        await using (var setup = Open())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
            setup.LocationTypes.Add(new LocationType { Id = LocationTypeIds.Floor, Code = "FLOOR", DisplayName = "Floor" });
            setup.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "GOOD", DisplayName = "Good" });
            setup.ZoneTypes.Add(new ZoneType { Id = ZoneTypeIds.Overflow, Code = "Overflow", DisplayName = "Overflow" });
            await setup.SaveChangesAsync();
            // What the migration / CreateWarehouse prepare for every warehouse: the hidden rack and the number counter.
            setup.Racks.Add(new Rack { WarehouseId = 1, Code = "OV1", Columns = 0, Levels = 1, IsSystem = true });
            setup.VirtualLocationSequences.Add(new VirtualLocationSequence { WarehouseId = 1 });
            await setup.SaveChangesAsync();
        }

        try
        {
            using var barrier = new Barrier(4);
            async Task<List<string>> UploadAsync()
            {
                await using var db = Open();
                var uow = new EfUnitOfWork(db);
                var virtuals = new VirtualLocationService(db, new SvcClock());
                barrier.SignalAndWait();
                return await uow.ExecuteInTransactionAsync(async ct =>
                {
                    var codes = new List<string>();
                    for (var i = 0; i < 3; i++)
                    {
                        codes.Add((await virtuals.AcquireAsync(1, null, ct)).LocationCode);
                    }

                    return codes;
                });
            }

            var results = (await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(UploadAsync)))).SelectMany(c => c).ToList();

            Assert.Equal(12, results.Distinct().Count());
            Assert.Equal(Enumerable.Range(1, 12).Select(n => $"VL-GDR1-{n:D6}").OrderBy(c => c), results.OrderBy(c => c)); // gap-free numbering
        }
        finally
        {
            await using var cleanup = Open();
            await cleanup.Database.EnsureDeletedAsync();
        }
    }
}

file sealed class IntegrationUser : ICurrentUserService
{
    public string? UserId => "it-user";
    public string? UserName => "it@godrejwms.local";
    public bool IsInRole(string role) => true;
    public IReadOnlyList<string> Roles => ["Superadmin"];
}
