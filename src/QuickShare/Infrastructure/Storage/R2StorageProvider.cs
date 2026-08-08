using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;


namespace QuickShare.Infrastructure.Storage;

public sealed class R2StorageProvider : IStorageProvider
{
    private readonly IAmazonS3 _s3;
    private readonly R2Options _options;
    private readonly ILogger<R2StorageProvider> _logger;
    private bool _bucketVerified;

    public R2StorageProvider(IOptions<R2Options> options, ILogger<R2StorageProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        var config = new AmazonS3Config
        {
            ServiceURL = $"https://{_options.AccountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
        };

        _s3 = new AmazonS3Client(_options.AccessKeyId, _options.AccessKeySecret, config);
    }

    // ── Startup validation ─────────────────────────────────────────────────

    public async Task EnsureBucketExistsAsync(CancellationToken ct = default)
    {
        if (_bucketVerified) return;

        try
        {
            var response = await _s3.ListBucketsAsync(ct);
            var exists = response.Buckets.Any(b => b.BucketName == _options.BucketName);
            if (!exists)
                _logger.LogWarning("R2 bucket '{Bucket}' not found", _options.BucketName);
            else
                _logger.LogInformation("R2 bucket '{Bucket}' verified", _options.BucketName);

            _bucketVerified = true;
        }
        catch (AmazonS3Exception ex)
        {
            throw R2StorageException.FromAmazonS3(ex, $"ListBuckets({_options.BucketName})");
        }
    }

    // ── R2 multipart upload ────────────────────────────────────────────────

    public async Task<R2InitResult> InitR2MultipartUploadAsync(
        Guid deliveryId, string fileName, CancellationToken ct = default)
    {
        var objectKey = R2ObjectKey.For(Guid.Empty, deliveryId, fileName);

        try
        {
            var request = new InitiateMultipartUploadRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                ContentType = "application/octet-stream",
            };

            var response = await _s3.InitiateMultipartUploadAsync(request, ct);
            _logger.LogDebug("Initiated R2 multipart upload: UploadId={UploadId}, Key={Key}",
                response.UploadId, objectKey);

            return new R2InitResult(response.UploadId, objectKey);
        }
        catch (AmazonS3Exception ex)
        {
            throw R2StorageException.FromAmazonS3(ex, $"InitR2MultipartUpload({objectKey})");
        }
    }

    public Task<IReadOnlyList<R2PartUrl>> GenerateUploadPartUrlsAsync(
        string uploadId, string objectKey, IReadOnlyList<int> partNumbers, CancellationToken ct = default)
    {
        var results = new List<R2PartUrl>(partNumbers.Count);

        foreach (var partNumber in partNumbers)
        {
            ct.ThrowIfCancellationRequested();

            var url = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                PartNumber = partNumber,
                UploadId = uploadId,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.AddMinutes(_options.PresignedUrlExpiry.UploadPartMinutes),
            });

            results.Add(new R2PartUrl(partNumber, new Uri(url)));
        }

        return Task.FromResult<IReadOnlyList<R2PartUrl>>(results.AsReadOnly());
    }

    public async Task<CompletionResult> CompleteR2MultipartUploadAsync(
        string uploadId, string objectKey, IReadOnlyList<R2PartEtag> parts, CancellationToken ct = default)
    {
        try
        {
            var listResponse = await _s3.ListPartsAsync(new ListPartsRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                UploadId = uploadId,
            }, ct);

            if (listResponse.Parts.Count == 0)
            {
                _logger.LogWarning("No parts found for upload {UploadId}", uploadId);
                return new CompletionResult(false, "No parts found on R2");
            }

            var completeRequest = new CompleteMultipartUploadRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                UploadId = uploadId,
                PartETags = parts
                    .Select(p => new PartETag(p.PartNumber, p.ETag))
                    .ToList(),
            };

            var response = await _s3.CompleteMultipartUploadAsync(completeRequest, ct);
            _logger.LogInformation("Completed R2 multipart upload: Key={Key}, UploadId={UploadId}",
                response.Key, uploadId);

            return new CompletionResult(true, null);
        }
        catch (AmazonS3Exception ex)
        {
            return new CompletionResult(false, $"R2 commit failed: {ex.Message}");
        }
    }

    public async Task AbortR2MultipartUploadAsync(
        string uploadId, string objectKey, CancellationToken ct = default)
    {
        try
        {
            await _s3.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                UploadId = uploadId,
            }, ct);

            _logger.LogInformation("Aborted R2 multipart upload: Key={Key}, UploadId={UploadId}",
                objectKey, uploadId);
        }
        catch (AmazonS3Exception ex)
        {
            throw R2StorageException.FromAmazonS3(ex, $"AbortR2MultipartUpload({objectKey})");
        }
    }

    // ── R2 download ────────────────────────────────────────────────────────

    public Task<Uri> GetPresignedDownloadUrlAsync(
        string objectKey, TimeSpan expiry, CancellationToken ct = default)
    {
        var url = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry),
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = "attachment",
            },
        });

        return Task.FromResult(new Uri(url));
    }

    // ── R2 cleanup ─────────────────────────────────────────────────────────

    public async Task DeleteObjectsAsync(
        string prefix, CancellationToken ct = default)
    {
        const int maxKeys = 1000;

        try
        {
            string? continuationToken = null;
            var totalDeleted = 0;

            do
            {
                var listResponse = await _s3.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = _options.BucketName,
                    Prefix = prefix,
                    MaxKeys = maxKeys,
                    ContinuationToken = continuationToken,
                }, ct);

                if (listResponse.S3Objects.Count == 0)
                    break;

                var deleteResponse = await _s3.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = _options.BucketName,
                    Objects = listResponse.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList(),
                }, ct);

                totalDeleted += deleteResponse.DeletedObjects.Count;

                foreach (var err in deleteResponse.DeleteErrors)
                {
                    _logger.LogError("Failed to delete R2 object {Key}: {Error}", err.Key, err.Message);
                }

                continuationToken = listResponse.NextContinuationToken;
            }
            while (continuationToken != null);

            _logger.LogInformation("Deleted {Count} R2 objects with prefix '{Prefix}'", totalDeleted, prefix);
        }
        catch (AmazonS3Exception ex)
        {
            throw R2StorageException.FromAmazonS3(ex, $"DeleteObjectsAsync({prefix})");
        }
    }

    public void Dispose()
    {
        _s3.Dispose();
    }
}
