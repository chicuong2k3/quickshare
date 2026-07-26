using System.Net;
using Amazon.S3;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Tests.Infrastructure;

public sealed class R2StorageExceptionTests
{
    private static AmazonS3Exception MakeException(string message, int statusCode)
    {
        return new AmazonS3Exception(message) { StatusCode = (HttpStatusCode)statusCode };
    }

    [Fact]
    public void FromAmazonS3_with_500_sets_IsTransient_true()
    {
        var inner = MakeException("Internal error", 500);
        var ex = R2StorageException.FromAmazonS3(inner, "TestOp");
        Assert.True(ex.IsTransient);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void FromAmazonS3_with_502_sets_IsTransient_true()
    {
        var inner = MakeException("Bad gateway", 502);
        var ex = R2StorageException.FromAmazonS3(inner, "TestOp");
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public void FromAmazonS3_with_503_sets_IsTransient_true()
    {
        var inner = MakeException("Service unavailable", 503);
        var ex = R2StorageException.FromAmazonS3(inner, "TestOp");
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public void FromAmazonS3_with_0_sets_IsTransient_true_for_timeout()
    {
        var inner = MakeException("Timeout", 0);
        var ex = R2StorageException.FromAmazonS3(inner, "TestOp");
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public void FromAmazonS3_with_400_sets_IsTransient_false()
    {
        var inner = MakeException("Bad request", 400);
        var ex = R2StorageException.FromAmazonS3(inner, "TestOp");
        Assert.False(ex.IsTransient);
    }

    [Fact]
    public void FromAmazonS3_with_403_sets_IsTransient_false()
    {
        var inner = MakeException("Forbidden", 403);
        var ex = R2StorageException.FromAmazonS3(inner, "TestOp");
        Assert.False(ex.IsTransient);
    }

    [Fact]
    public void FromAmazonS3_with_404_sets_IsTransient_false()
    {
        var inner = MakeException("Not found", 404);
        var ex = R2StorageException.FromAmazonS3(inner, "TestOp");
        Assert.False(ex.IsTransient);
    }

    [Fact]
    public void FromAmazonS3_includes_context_in_message()
    {
        var inner = MakeException("fail", 400);
        var ex = R2StorageException.FromAmazonS3(inner, "DeleteObjectsAsync(uploads/user/)");
        Assert.Contains("DeleteObjectsAsync(uploads/user/)", ex.Message);
    }
}
