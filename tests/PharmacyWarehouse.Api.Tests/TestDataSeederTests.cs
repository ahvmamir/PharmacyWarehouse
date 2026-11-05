using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.Operations;
using PharmacyWarehouse.Api.Stock;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Tests;

public class TestDataSeederTests : ApiTestBase
{
    private async Task<TestDataSeeder.SeedResult?> SeedAsync()
    {
        using var scope = App.CreateScope();
        return await TestDataSeeder.SeedAsync(
            scope.ServiceProvider.GetRequiredService<WarehouseDbContext>(),
            scope.ServiceProvider.GetRequiredService<StockLedger>(),
            DateTime.UtcNow);
    }

    [Fact]
    public async Task Seeds100ObviouslyFakeProducts_ThroughLedgerAndAudit()
    {
        var result = await SeedAsync();

        Assert.NotNull(result);
        await App.AssertLedgerConsistentAsync();

        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        var products = await db.Products.AsNoTracking().ToListAsync();
        var batches = await db.Batches.AsNoTracking().ToListAsync();

        Assert.Equal(TestDataSeeder.ProductCount, products.Count);
        Assert.All(products, p => Assert.StartsWith(TestDataSeeder.NamePrefix, p.Name));
        Assert.All(batches, b => Assert.StartsWith("TEST-", b.BatchNumber));
        Assert.Equal(result.Batches, batches.Count);
        Assert.Contains(await db.AuditEntries.AsNoTracking().Select(a => a.EntityType).Distinct().ToListAsync(), t => t == nameof(Product));
    }

    [Fact]
    public async Task DataIsVaried_SoEveryDashboardBucketFills()
    {
        await SeedAsync();

        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        var batches = await db.Batches.AsNoTracking().ToListAsync();
        var products = await db.Products.AsNoTracking().ToListAsync();
        var today = LocalDay.Today(DateTime.UtcNow);

        Assert.Contains(batches, b => b.ExpiryDate < today);
        Assert.Contains(batches, b => b.ExpiryDate >= today && b.ExpiryDate <= today.AddMonths(DashboardRules.DefaultNearMonths));
        Assert.Contains(batches, b => b.ExpiryDate > today.AddMonths(DashboardRules.DefaultNearMonths) && b.ExpiryDate <= today.AddMonths(DashboardRules.DefaultFarMonths));
        Assert.Contains(batches, b => b.ExpiryDate > today.AddMonths(DashboardRules.DefaultFarMonths));
        Assert.Contains(batches, b => b.ExpiryDate is null);
        Assert.Contains(products, p => p.MinStock is null);
        Assert.Contains(products, p => p.MinStock is not null);
        Assert.True(products.Select(p => p.LocationId).Distinct().Count() > 8);
    }

    [Fact]
    public async Task SecondRun_ChangesNothing()
    {
        await SeedAsync();

        Assert.Null(await SeedAsync());

        using var scope = App.CreateScope();
        Assert.Equal(TestDataSeeder.ProductCount, await scope.ServiceProvider.GetRequiredService<WarehouseDbContext>().Products.CountAsync());
    }
}
