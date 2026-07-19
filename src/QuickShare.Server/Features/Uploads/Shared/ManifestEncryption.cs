using System.Security.Cryptography;
using System.Text.Json;

namespace QuickShare.Server.Features.Uploads.Shared;

public static class ManifestEncryption
{
    private static readonly byte[] AesIV = new byte[16]; // Zero IV — key is unique per manifest (shareCode)

    public static byte[] Encrypt(ManifestData data, string shareCode)
    {
        var json = JsonSerializer.Serialize(data);
        var plaintext = System.Text.Encoding.UTF8.GetBytes(json);
        var key = DeriveKey(shareCode);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = AesIV;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }

    public static ManifestData Decrypt(byte[] encryptedData, string shareCode)
    {
        var key = DeriveKey(shareCode);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = AesIV;

        using var decryptor = aes.CreateDecryptor();
        var plaintext = decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);

        var json = System.Text.Encoding.UTF8.GetString(plaintext);
        return JsonSerializer.Deserialize<ManifestData>(json)!;
    }

    private static byte[] DeriveKey(string shareCode)
    {
        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(shareCode));
    }
}
