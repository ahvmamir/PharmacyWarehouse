using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

// شمارنده‌ی کارتابل: هر صف فقط برای دارنده‌ی مجوز همان صف شمرده می‌شود.
public class InboxTests : ApiTestBase
{
    private HttpClient Staff { get; set; } = null!;
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddStaffAsync("staff-i");
        await App.AddKeeperAsync("keeper-i");
        Staff = await LoggedInAsync("staff-i", TestApp.StaffPassword);
        Keeper = await LoggedInAsync("keeper-i", TestApp.StaffPassword);
    }

    private static async Task<InboxCounts> CountsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<InboxCounts>(ApiRoutes.Inbox))!;

    [Fact]
    public async Task Keeper_SeesPendingWithdrawalsAndReceipts()
    {
        var before = await CountsAsync(Keeper);
        Assert.Equal(0, before.PendingWithdrawals);
        Assert.Equal(0, before.PendingReceipts);

        var receipt = await Staff.PostAsJsonAsync(ApiRoutes.ReceiptsQuick, new QuickReceiptRequest
        {
            Lines = [new QuickReceiptLineRequest { NewName = "TEST-DRUG-I1", BatchNumber = "TEST-BATCH-I1", Quantity = 3 }]
        });
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        var line = Assert.Single((await receipt.Content.ReadFromJsonAsync<ReceiptItem>())!.Lines);

        var allocation = (await Staff.GetFromJsonAsync<AllocationResponse>(ApiRoutes.ProductAllocation(line.ProductId, 1)))!;
        var withdrawal = await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, new WithdrawalRequest
        {
            ProductId = line.ProductId,
            Lines = allocation.Lines.Select(l => new WithdrawalLineRequest { BatchId = l.BatchId, Quantity = l.Quantity, BatchVersion = l.Version }).ToList()
        });
        Assert.Equal(HttpStatusCode.OK, withdrawal.StatusCode);

        var after = await CountsAsync(Keeper);
        Assert.Equal(1, after.PendingWithdrawals);
        Assert.Equal(1, after.PendingReceipts);
    }

    [Fact]
    public async Task Staff_WithoutReviewPermissions_GetsNoCounts()
    {
        var counts = await CountsAsync(Staff);

        Assert.Null(counts.PendingWithdrawals);
        Assert.Null(counts.PendingReceipts);
    }

    [Fact]
    public async Task Anonymous_IsRejected()
    {
        using var anonymous = App.NewClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(ApiRoutes.Inbox)).StatusCode);
    }
}
