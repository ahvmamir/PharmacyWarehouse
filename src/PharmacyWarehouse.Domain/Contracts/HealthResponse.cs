namespace PharmacyWarehouse.Domain.Contracts;

public sealed record HealthResponse(string Status, DateTimeOffset ServerTimeUtc)
{
    public const string OkStatus = "ok";

    // نسخه‌ی سرور، مثل «1.3.0+abc1234…». کلاینت آن را نشان می‌دهد و با نسخه‌ی خودش مقایسه می‌کند.
    public string? Version { get; init; }

    public static HealthResponse Ok(DateTimeOffset nowUtc, string? version = null) => new(OkStatus, nowUtc) { Version = version };
}
