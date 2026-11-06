namespace PharmacyWarehouse.Api.Data;

// هر ردیف تاریخچه در تاریخچه‌ی کدام رکورد نمایش داده شود. رکورد اصلی خودش است؛
// رکوردهای فرعی (برش ۱: بچ و بارکد) در تاریخچه‌ی قلمشان می‌آیند.
internal static class AuditSubject
{
    public static Guid? Of(IAudited entity) => entity switch
    {
        StorageLocation location => location.Id,
        Batch batch => batch.ProductId,
        ProductBarcode barcode => barcode.ProductId,
        ReceiptLine line => line.ReceiptId,
        _ => entity.Id
    };
}
