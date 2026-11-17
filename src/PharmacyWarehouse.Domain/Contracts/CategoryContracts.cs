namespace PharmacyWarehouse.Domain.Contracts;

// دسته‌ی قلم (بخش ۱۵.۳ spec). ProductCount فقط برای نمایش و تصمیم حذف در صفحه است؛ مرجع حذف سرور است.
public sealed record CategoryItem(Guid Id, string Name, bool IsActive, int ProductCount, int Version);

// تعیین دسته‌ی چند قلم با هم (بخش ۱۵.۳ spec). CategoryId خالی یعنی «بدون دسته». نتیجه جزئی است:
// هر قلم با Version خودش جدا ذخیره می‌شود، مثل تأیید گروهی برداشت.
public sealed class BulkCategoryRequest
{
    public const int MaxItems = 500;

    public Guid? CategoryId { get; set; }
    public List<ReviewTarget> Items { get; set; } = [];
}

public sealed record BulkCategoryResult(IReadOnlyList<Guid> Updated, IReadOnlyList<BulkFailure> Failed);

public sealed class SaveCategoryRequest
{
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int Version { get; set; }
}
