namespace WindowsImageDownloader.Services;

public sealed record TaskOperationResult(bool Succeeded, string? Message = null)
{
    public static TaskOperationResult Success(string? message = null) => new(true, message);

    public static TaskOperationResult Failure(string message) => new(false, message);
}
