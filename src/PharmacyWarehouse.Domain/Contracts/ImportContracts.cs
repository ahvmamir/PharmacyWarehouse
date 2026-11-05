using PharmacyWarehouse.Domain.Import;

namespace PharmacyWarehouse.Domain.Contracts;

public sealed class ImportRequest
{
    public List<ImportRowInput> Rows { get; set; } = [];
}

// Rows: همان ردیف‌هایی که کاربر می‌بیند و اصلاح می‌کند. IgnoredRows: ردیف‌های بی‌نام (قالب خالی) که اصلاً نمایش داده نمی‌شوند.
public sealed record ImportPreview(
    IReadOnlyList<ImportRowInput> Rows, IReadOnlyList<ImportRowStatus> Statuses, ImportSummary Summary, int IgnoredRows, bool CanCommit);

public sealed record ImportResult(int ProductsCreated, int ProductsExisting, int BatchesCreated, int LocationsCreated, int BarcodesCreated);
