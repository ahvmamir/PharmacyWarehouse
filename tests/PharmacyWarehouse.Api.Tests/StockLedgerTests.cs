using Microsoft.Data.Sqlite;

namespace PharmacyWarehouse.Api.Tests;

public class StockLedgerTests : ApiTestBase
{
    [Fact]
    public async Task StockMovements_CannotBeUpdatedOrDeleted()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-01", null);
        var batch = await App.AddBatchRawAsync(product, "TEST-BATCH-01", null, onHand: 5);
        var movements = await App.MovementsAsync(batch);
        Assert.Single(movements);

        var update = await Assert.ThrowsAnyAsync<Exception>(() => App.ExecuteRawAsync("UPDATE StockMovements SET Quantity = 9"));
        var delete = await Assert.ThrowsAnyAsync<Exception>(() => App.ExecuteRawAsync("DELETE FROM StockMovements"));

        Assert.Contains("append-only", FindSqlite(update).Message);
        Assert.Contains("append-only", FindSqlite(delete).Message);
    }

    [Fact]
    public async Task Batch_OnHandBelowZero_IsRejectedByCheck()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-01", null);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => App.ExecuteRawAsync(
            "INSERT INTO Batches (Id, ProductId, BatchNumber, BatchKey, ExpiryPrecision, ExpiryCalendar, OnHand, CreatedAtUtc, Version) " +
            $"VALUES ('{Guid.NewGuid()}', '{product.ToString().ToUpperInvariant()}', 'TEST-BATCH-01', 'test-batch-01', 'Day', 'Gregorian', -1, '2026-01-01', 1)"));

        Assert.Contains("CK_Batches_OnHand", FindSqlite(ex).Message);
    }

    [Fact]
    public async Task Triggers_ExistAfterMigration()
    {
        Assert.Equal(1, await App.CountSqliteMasterAsync("trigger", "StockMovements_NoUpdate"));
        Assert.Equal(1, await App.CountSqliteMasterAsync("trigger", "StockMovements_NoDelete"));
    }

    [Fact]
    public async Task Ledger_IsConsistentForRawHelpers()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-01", null);
        await App.AddBatchRawAsync(product, "TEST-BATCH-01", null, onHand: 7);
        await App.AddBatchRawAsync(product, "TEST-BATCH-02", null);

        await App.AssertLedgerConsistentAsync();
    }

    private static SqliteException FindSqlite(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is SqliteException s) return s;
            if (e.InnerException is null) break;
        }

        throw new InvalidOperationException("No SqliteException found.", ex);
    }
}
