using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

// دسته‌ی قلم (بخش ۱۵.۳ spec): یکتایی روی کلید یکدست‌شده، حذف فقط بدون قلم، دسته‌ی غیرفعال برای قلم تازه رد می‌شود.
public class CategoryTests : ApiTestBase
{
    private HttpClient Admin { get; set; } = null!;
    private HttpClient Staff { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddAdminAsync("admin-c");
        await App.AddStaffAsync("staff-c");
        Admin = await LoggedInAsync("admin-c", TestApp.AdminPassword);
        Staff = await LoggedInAsync("staff-c", TestApp.StaffPassword);
    }

    private async Task<CategoryItem> CreateAsync(string name)
    {
        var response = await Admin.PostAsJsonAsync(ApiRoutes.Categories, new SaveCategoryRequest { Name = name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryItem>())!;
    }

    private async Task<ProductDetail> ProductAsync(string name, Guid? categoryId)
    {
        var request = ProductTests.Req(name);
        request.CategoryId = categoryId;
        var response = await Admin.PostAsJsonAsync(ApiRoutes.Products, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDetail>())!;
    }

    [Fact]
    public async Task Create_NormalizesName_AndRejectsDuplicateKey()
    {
        var created = await CreateAsync("  TEST-CAT  آنتی‌بیوتیک ");
        Assert.Equal("TEST-CAT آنتی‌بیوتیک", created.Name);

        var duplicate = await Admin.PostAsJsonAsync(ApiRoutes.Categories, new SaveCategoryRequest { Name = "test-cat آنتی‌بیوتیک" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var empty = await Admin.PostAsJsonAsync(ApiRoutes.Categories, new SaveCategoryRequest { Name = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Product_ShowsCategory_AndListFiltersByIt()
    {
        var category = await CreateAsync("TEST-CAT-A");
        var inCategory = await ProductAsync("TEST-DRUG-C1", category.Id);
        await ProductAsync("TEST-DRUG-C2", null);

        Assert.Equal(category.Id, inCategory.CategoryId);
        Assert.Equal("TEST-CAT-A", inCategory.Summary.CategoryName);

        var filtered = (await Admin.GetFromJsonAsync<List<ProductListItem>>($"{ApiRoutes.Products}?categoryId={category.Id}"))!;
        Assert.Equal("TEST-DRUG-C1", Assert.Single(filtered).Name);

        var list = (await Admin.GetFromJsonAsync<List<CategoryItem>>(ApiRoutes.Categories))!;
        Assert.Equal(1, Assert.Single(list).ProductCount);
    }

    [Fact]
    public async Task Delete_WithProducts_Conflicts_ButDeactivateWorks()
    {
        var category = await CreateAsync("TEST-CAT-B");
        await ProductAsync("TEST-DRUG-C3", category.Id);

        var dependencies = (await Admin.GetFromJsonAsync<DependencyList>($"{ApiRoutes.Categories}/{category.Id}/dependencies"))!;
        Assert.False(dependencies.CanDelete);
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.DeleteAsync($"{ApiRoutes.Categories}/{category.Id}")).StatusCode);

        var deactivated = await Admin.PutAsJsonAsync($"{ApiRoutes.Categories}/{category.Id}",
            new SaveCategoryRequest { Name = category.Name, IsActive = false, Version = category.Version });
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutProducts_Succeeds()
    {
        var category = await CreateAsync("TEST-CAT-C");

        Assert.Equal(HttpStatusCode.OK, (await Admin.DeleteAsync($"{ApiRoutes.Categories}/{category.Id}")).StatusCode);
        Assert.Empty((await Admin.GetFromJsonAsync<List<CategoryItem>>(ApiRoutes.Categories))!);
    }

    [Fact]
    public async Task InactiveCategory_RejectedForNewProduct_ButKeptOnExisting()
    {
        var category = await CreateAsync("TEST-CAT-D");
        var product = await ProductAsync("TEST-DRUG-C4", category.Id);
        (await Admin.PutAsJsonAsync($"{ApiRoutes.Categories}/{category.Id}",
            new SaveCategoryRequest { Name = category.Name, IsActive = false, Version = category.Version })).EnsureSuccessStatusCode();

        var request = ProductTests.Req("TEST-DRUG-C5");
        request.CategoryId = category.Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync(ApiRoutes.Products, request)).StatusCode);

        // ویرایش قلمی که همین دسته را دارد رد نمی‌شود.
        var edit = ProductTests.Req("TEST-DRUG-C4", minStock: 3);
        edit.CategoryId = category.Id;
        edit.Version = product.Summary.Version;
        Assert.Equal(HttpStatusCode.OK, (await Admin.PutAsJsonAsync($"{ApiRoutes.Products}/{product.Summary.Id}", edit)).StatusCode);
    }

    [Fact]
    public async Task VersionMismatch_Conflicts()
    {
        var category = await CreateAsync("TEST-CAT-E");

        var stale = await Admin.PutAsJsonAsync($"{ApiRoutes.Categories}/{category.Id}",
            new SaveCategoryRequest { Name = "TEST-CAT-E2", Version = category.Version + 5 });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Staff_CanRead_ButNotChange()
    {
        Assert.Equal(HttpStatusCode.OK, (await Staff.GetAsync(ApiRoutes.Categories)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Staff.PostAsJsonAsync(ApiRoutes.Categories, new SaveCategoryRequest { Name = "TEST-CAT-F" })).StatusCode);
    }

    [Fact]
    public async Task BulkAssign_SetsCategory_AndReportsStaleVersions()
    {
        var category = await CreateAsync("TEST-CAT-H");
        var first = await ProductAsync("TEST-DRUG-C7", null);
        var second = await ProductAsync("TEST-DRUG-C8", null);

        var response = await Admin.PostAsJsonAsync(ApiRoutes.ProductsBulkCategory, new BulkCategoryRequest
        {
            CategoryId = category.Id,
            Items =
            [
                new ReviewTarget { Id = first.Summary.Id, Version = first.Summary.Version },
                new ReviewTarget { Id = second.Summary.Id, Version = second.Summary.Version + 5 }
            ]
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BulkCategoryResult>())!;
        Assert.Equal([first.Summary.Id], result.Updated);
        Assert.Equal(second.Summary.Id, Assert.Single(result.Failed).Id);

        var list = (await Admin.GetFromJsonAsync<List<ProductListItem>>($"{ApiRoutes.Products}?categoryId={category.Id}"))!;
        Assert.Equal("TEST-DRUG-C7", Assert.Single(list).Name);
    }

    [Fact]
    public async Task BulkAssign_NeedsProductsManage()
    {
        var product = await ProductAsync("TEST-DRUG-C9", null);

        var response = await Staff.PostAsJsonAsync(ApiRoutes.ProductsBulkCategory, new BulkCategoryRequest
        {
            Items = [new ReviewTarget { Id = product.Summary.Id, Version = product.Summary.Version }]
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CategoryChange_IsAudited_WithName()
    {
        var category = await CreateAsync("TEST-CAT-G");
        var product = await ProductAsync("TEST-DRUG-C6", null);

        var edit = ProductTests.Req("TEST-DRUG-C6");
        edit.CategoryId = category.Id;
        edit.Version = product.Summary.Version;
        (await Admin.PutAsJsonAsync($"{ApiRoutes.Products}/{product.Summary.Id}", edit)).EnsureSuccessStatusCode();

        var rows = await App.AuditRowsAsync(product.Summary.Id);
        var change = Assert.Single(rows, r => r.Field == "CategoryId");
        Assert.Equal($"{category.Id}|TEST-CAT-G", change.NewValue);
    }
}
