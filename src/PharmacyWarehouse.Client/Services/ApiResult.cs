namespace PharmacyWarehouse.Client.Services;

public enum ApiOutcome
{
    Success,
    Unauthorized,
    Forbidden,
    NotFound,
    Rejected,
    TooManyRequests,
    Conflict,
    BadRequest,
    NetworkError,
    ServerError
}

// بدنه‌ی ProblemDetails: عنوان فارسی، خطاهای فیلدبه‌فیلد و نوع ۴۰۹.
public sealed class ApiProblem
{
    public string? Title { get; init; }
    public string? Type { get; init; }
    public Dictionary<string, string[]> Errors { get; init; } = [];
    public Guid? ExistingId { get; init; }
    public string? ExistingSummary { get; init; }
}

public sealed record DownloadedFile(string Name, string ContentType, byte[] Content);

public sealed class ApiResult<T> where T : class
{
    private ApiResult(ApiOutcome outcome, T? value, ApiProblem? problem)
    {
        Outcome = outcome;
        Value = value;
        Problem = problem;
    }

    public ApiOutcome Outcome { get; }
    public T? Value { get; }
    public ApiProblem? Problem { get; }
    public bool IsSuccess => Outcome == ApiOutcome.Success;

    // پیام آماده برای نمایش؛ اگر سرور عنوان نداد، پیام عمومی همان نتیجه.
    public string ErrorMessage => Problem?.Title ?? Outcome switch
    {
        ApiOutcome.NetworkError => "ارتباط با سرور برقرار نشد. اتصال شبکه را بررسی کنید و دوباره تلاش کنید.",
        ApiOutcome.Unauthorized => "نشست شما تمام شده است. دوباره وارد شوید.",
        ApiOutcome.Forbidden => "اجازه‌ی این کار را ندارید.",
        ApiOutcome.NotFound => "پیدا نشد.",
        _ => "خطای غیرمنتظره. دوباره تلاش کنید."
    };

    public static ApiResult<T> Success(T value) => new(ApiOutcome.Success, value, null);
    public static ApiResult<T> Failure(ApiOutcome outcome, ApiProblem? problem = null) => new(outcome, null, problem);
}
