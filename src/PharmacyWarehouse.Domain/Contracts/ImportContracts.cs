using PharmacyWarehouse.Domain.Import;

namespace PharmacyWarehouse.Domain.Contracts;

public sealed class ImportRequest
{
    public List<ImportRowInput> Rows { get; set; } = [];
    // ثبت جزئی (commit): شماره‌ی ردیف‌های انتخاب‌شده در Rows (از صفر)؛ null و ValidOnly=false یعنی همه، و هر خطا ۴۰۹ می‌دهد.
    public List<int>? SelectedIndexes { get; set; }
    // فقط ردیف‌های معتبر (Ok/Warning) ثبت شوند و ردیف‌های دارای خطا بمانند.
    public bool ValidOnly { get; set; }
}

// Rows: همان ردیف‌هایی که کاربر می‌بیند و اصلاح می‌کند. IgnoredRows: ردیف‌های بی‌نام (قالب خالی) که اصلاً نمایش داده نمی‌شوند.
public sealed record ImportPreview(
    IReadOnlyList<ImportRowInput> Rows, IReadOnlyList<ImportRowStatus> Statuses, ImportSummary Summary, int IgnoredRows, bool CanCommit);

public sealed record ImportResult(int ProductsCreated, int ProductsExisting, int BatchesCreated, int LocationsCreated, int BarcodesCreated);
