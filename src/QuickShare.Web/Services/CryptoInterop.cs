using Microsoft.JSInterop;

namespace QuickShare.Web.Services;

public sealed class CryptoInterop(IJSRuntime js)
{
    /// <summary>Generates AES-256-GCM key in browser. Returns base64url-encoded raw key.</summary>
    public async Task<string> GenerateKeyAsync()
    {
        var keyRef = await js.InvokeAsync<IJSObjectReference>("ZapDropCrypto.generateKey");
        return await js.InvokeAsync<string>("ZapDropCrypto.exportKey", keyRef);
    }

    /// <summary>Encrypts a file into chunks. Returns manifest + array of encrypted chunk data.</summary>
    public async Task<(byte[] encryptedManifest, string manifestNonce, byte[][] encryptedChunks, string[] chunkNonces)>
        EncryptFileAsync(string keyBase64Url, byte[] fileData, string fileName, string mimeType, string manifestId)
    {
        // This is a high-level orchestrator. Actual WebCrypto calls happen in JS.
        // For SSR Blazor, the encryption orchestration is called via JS interop per-step.

        // The actual per-chunk encryption is too granular for JS interop round-trips.
        // In practice, the upload page's JavaScript handles the full encrypt-then-upload loop.
        // This class serves as a type-safe interop bridge for the Blazor page to call.

        throw new NotImplementedException(
            "Full-file encryption should be orchestrated in JavaScript, not via per-chunk JS interop. " +
            "Use window.ZapDropCrypto directly from the upload page's JavaScript.");
    }

    /// <summary>Decrypts a manifest using the key from the URL fragment.</summary>
    public async Task<ManifestData> DecryptManifestAsync(string keyBase64Url, byte[] encryptedManifest, string nonce)
    {
        var keyRef = await js.InvokeAsync<IJSObjectReference>("ZapDropCrypto.importKey", keyBase64Url);
        var manifestJson = await js.InvokeAsync<string>("ZapDropCrypto.decryptManifest", keyRef, encryptedManifest, nonce);
        return System.Text.Json.JsonSerializer.Deserialize<ManifestData>(manifestJson)
               ?? throw new InvalidOperationException("Failed to parse manifest");
    }
}

public sealed class ManifestData
{
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long FileSize { get; set; }
    public int ChunkSize { get; set; }
    public ManifestChunk[] Chunks { get; set; } = [];
    public string FileSha256 { get; set; } = "";
}

public sealed class ManifestChunk
{
    public int Index { get; set; }
    public string StorageRef { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Nonce { get; set; } = "";
}
