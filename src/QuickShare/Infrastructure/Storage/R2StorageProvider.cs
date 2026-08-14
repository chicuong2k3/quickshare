using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;


namespace QuickShare.Infrastructure.Storage;

public sealed class R2StorageProvider : IStorageProvider, IDisposable
{
    private readonly AmazonS3Client _s3;
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
            // HeadBucket works with R2 tokens scoped to a single bucket, whereas
            // ListBuckets requires "All buckets" permission on the token.
            await _s3.HeadBucketAsync(new HeadBucketRequest
            {
                BucketName = _options.BucketName,
            }, ct);
            _logger.LogInformation("R2 bucket '{Bucket}' verified", _options.BucketName);

            _bucketVerified = true;
        }
        catch (AmazonS3Exception ex)
        {
            throw R2StorageException.FromAmazonS3(ex, $"HeadBucket({_options.BucketName})");
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
                ContentType = MediaTypes.OctetStream,
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

    // ── R2 small-object upload (avatars, etc.) ─────────────────────────────

    public async Task UploadObjectAsync(
        string objectKey, Stream content, string contentType, CancellationToken ct = default)
    {
        try
        {
            var request = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                InputStream = content,
                ContentType = contentType,
            };

            await _s3.PutObjectAsync(request, ct);
            _logger.LogDebug("Uploaded R2 object: Key={Key}", objectKey);
        }
        catch (AmazonS3Exception ex)
        {
            throw R2StorageException.FromAmazonS3(ex, $"UploadObjectAsync({objectKey})");
        }
    }

    // ── R2 download ────────────────────────────────────────────────────────

    public async Task<long> GetObjectSizeAsync(string objectKey, CancellationToken ct = default)
    {
        try
        {
            var metadata = await _s3.GetObjectMetadataAsync(_options.BucketName, objectKey, ct);
            return metadata.ContentLength;
        }
        catch (AmazonS3Exception)
        {
            return -1;
        }
    }

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

    public async Task DeleteObjectAsync(
        string objectKey, CancellationToken ct = default)
    {
        try
        {
            await _s3.DeleteObjectAsync(_options.BucketName, objectKey, ct);
        }
        catch (AmazonS3Exception ex)
        {
            throw R2StorageException.FromAmazonS3(ex, $"DeleteObjectAsync({objectKey})");
        }
    }

    // ── R2 orphan sweep (issue #107) ───────────────────────────────────────

    // S3/R2 timestamps are UTC; some SDK versions surface them as Unspecified or Local.
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    public async Task<IReadOnlyList<string>> ListObjectsAsync(
        string prefix, DateTime olderThan, CancellationToken ct = default)
    {
        const int maxKeys = 1000;

        try
        {
            var keys = new List<string>();
            string? continuationToken = null;

            do
            {
                var response = await _s3.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = _options.BucketName,
                    Prefix = prefix,
                    MaxKeys = maxKeys,
                    ContinuationToken = continuationToken,
                }, ct);

                keys.AddRange((response.S3Objects ?? [])
                    .Where(o => o.Key is not null && o.LastModified.HasValue && AsUtc(o.LastModified.Value) < olderThan)
                    .Select(o => o.Key));

                continuationToken = response.NextContinuationToken;
            } while (continuationToken is not null);

            return keys;
        }
        catch (AmazonS3Exception ex)
        {
            throw R2StorageException.FromAmazonS3(ex, $"ListObjectsAsync({prefix})");
        }
    }

    public async Task<IReadOnlyList<R2MultipartInfo>> ListMultipartUploadsAsync(
        string prefix, DateTime olderThan, CancellationToken ct = default)
    {
        try
        {
            var uploads = new List<R2MultipartInfo>();
            string? keyMarker = null;
            string? uploadIdMarker = null;

            ListMultipartUploadsResponse response;
            do
            {
                response = await _s3.ListMultipartUploadsAsync(new ListMultipartUploadsRequest
                {
                    BucketName = _options.BucketName,
                    Prefix = prefix,
                    KeyMarker = keyMarker,
                    UploadIdMarker = uploadIdMarker,
                }, ct);

                uploads.AddRange((response.MultipartUploads ?? [])
                    // Key filter repeated client-side: R2's S3 compat has had gaps around
                    // ListMultipartUploads Prefix handling — never trust it alone.
                    .Where(u => u.UploadId is not null && u.Key is not null
                                && u.Key.StartsWith(prefix, StringComparison.Ordinal)
                                && u.Initiated.HasValue && AsUtc(u.Initiated.Value) < olderThan)
                    .Select(u => new R2MultipartInfo(u.UploadId, u.Key, AsUtc(u.Initiated!.Value))));

                keyMarker = response.NextKeyMarker;
                uploadIdMarker = response.NextUploadIdMarker;
                // Null markers with IsTruncated would re-fetch page 1 forever.
            } while (response.IsTruncated == true && (keyMarker is not null || uploadIdMarker is not null));

            return uploads;
        }
        catch (AmazonS3Exception ex)
        {
            throw R2StorageException.FromAmazonS3(ex, $"ListMultipartUploadsAsync({prefix})");
        }
    }

    public void Dispose()
    {
        _s3.Dispose();
    }
}
