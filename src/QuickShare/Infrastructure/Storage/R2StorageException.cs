using Amazon.S3;

namespace QuickShare.Infrastructure.Storage;

public sealed class R2StorageException : Exception
{
    public bool IsTransient { get; }

    public R2StorageException(string message, AmazonS3Exception inner, bool isTransient)
        : base(message, inner)
    {
        IsTransient = isTransient;
    }

    public static R2StorageException FromAmazonS3(AmazonS3Exception ex, string context)
    {
        var statusCode = (int)ex.StatusCode;
        var isTransient = statusCode >= 500 || statusCode == 0; // 5xx or timeout
        var message = $"R2 operation failed for {context}: {ex.Message}";
        return new R2StorageException(message, ex, isTransient);
    }
}
