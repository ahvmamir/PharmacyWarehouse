namespace PharmacyWarehouse.Domain.Contracts;

// فهرست مرجع TTAC (بخش ۱۵.۴ spec). هیچ‌کدام قلم، بچ یا حرکت نمی‌سازد؛ فقط فرم تعریف قلم را پر می‌کند.

public sealed record CatalogImportResult(int Inserted, int Empty, int Invalid, int DuplicateGtin);

public sealed record CatalogStatus(int Count, DateTime? LastImportAtUtc, string? LastFileName, string? LastImportedBy, int? StaleAfterDays);

// Name، Brand و PackDescription همان نگاشت تأییدشده‌اند (CatalogMapping). ExistingProduct: GTIN از قبل بارکد این قلم است.
public sealed record CatalogSuggestion(
    Guid Id, string Gtin, string? Irc, string Name, string? Brand, string? PackDescription,
    string? GenericName, string? BrandName, string? ProducerCountry, long? ConsumerPrice, bool IsTemporaryIrc,
    Guid? ExistingProductId, string? ExistingProductName);
