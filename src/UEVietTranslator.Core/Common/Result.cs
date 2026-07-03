namespace UEVietTranslator.Core.Common;

/// <summary>
/// Kết quả của 1 thao tác có thể fail theo cách "dự kiến được" (sai AES key,
/// thiếu file, game không nhận diện được...). Dùng thay cho exception ở mọi
/// luồng lỗi không phải bug lập trình. Xem CLAUDE.md §5.1 và
/// docs/CODING_STANDARDS.md.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }

    protected Result(bool isSuccess, string? error)
    {
        if (isSuccess && error is not null)
            throw new InvalidOperationException("Result thành công không được có Error.");
        if (!isSuccess && error is null)
            throw new InvalidOperationException("Result thất bại phải có Error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
}

/// <summary>
/// Result kèm giá trị trả về khi thành công.
/// </summary>
public sealed class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool isSuccess, T? value, string? error) : base(isSuccess, error) =>
        Value = value;

    public static Result<T> Success(T value) => new(true, value, null);

    public new static Result<T> Failure(string error) => new(false, default, error);
}
