namespace PharmacyWarehouse.Domain.Contracts;

public sealed class RoleItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";

    // نقش ثابت Admin: نه ویرایش می‌شود و نه حذف.
    public bool IsLocked { get; init; }
    public string[] Permissions { get; init; } = [];
    public int UserCount { get; init; }
}

public sealed class SaveRoleRequest
{
    public string Name { get; set; } = "";
    public string[] Permissions { get; set; } = [];
}

public sealed class UpdateUserRequest
{
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public string[] Grants { get; set; } = [];
    public string[] Denies { get; set; } = [];
}

// نتیجه‌ی پرسش «چه چیزی جلوی حذف را گرفته؟»
public sealed class DependencyList
{
    public bool CanDelete => Items.Count == 0;
    public IReadOnlyList<DependencyItem> Items { get; init; } = [];

    // ممکن است بیشتر از Items باشد؛ فهرست کوتاه می‌شود.
    public int Total { get; init; }
}

// SubjectId: رکوردی که وابستگی به آن است و صفحه‌ی جزئیات دارد (برش ۱: قلم)، تا پنجره‌ی حذف به آن پیوند بدهد.
public sealed record DependencyItem(string Kind, string Label, Guid? SubjectId = null);
