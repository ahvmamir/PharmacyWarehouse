using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.Operations;

namespace PharmacyWarehouse.Api.Tests;

public class StockVerifierTests : ApiTestBase
{
    [Fact]
    public async Task ConsistentLedger_HasNoMismatch_IncludingBatchWithoutMovements()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-V1", null);
        await App.AddBatchRawAsync(product, "TEST-LOT-V1", null, onHand: 5);
        await App.AddBatchRawAsync(product, "TEST-LOT-V2", null);

        using var scope = App.CreateScope();
        var (count, mismatches) = await StockVerifier.VerifyAsync(scope.ServiceProvider.GetRequiredService<WarehouseDbContext>());

        Assert.Equal(2, count);
        Assert.Empty(mismatches);
    }

    [Fact]
    public async Task Mismatch_IsReported_AndNothingIsChanged()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-V1", null);
        var batchId = await App.AddBatchRawAsync(product, "TEST-LOT-V1", null, onHand: 5);
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        (await db.Batches.FirstAsync(b => b.Id == batchId)).OnHand = 99; // خرابی عمدی؛ فقط در تست
        await db.SaveChangesAsync();

        var (_, mismatches) = await StockVerifier.VerifyAsync(db);

        var mismatch = Assert.Single(mismatches);
        Assert.Equal(batchId, mismatch.BatchId);
        Assert.Equal((99, 5), (mismatch.OnHand, mismatch.MovementSum));
        Assert.Equal(99, await db.Batches.AsNoTracking().Where(b => b.Id == batchId).Select(b => b.OnHand).FirstAsync());
    }
}
