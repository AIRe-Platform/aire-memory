using System.Security.Cryptography;
using Aire.Sdk.Helpers;

namespace Aire.Memory.Helpers;

public static class EncryptionHelper
{
    public static T? DecryptObject<T>(string data, string key) where T : class, new()
    {
        var keyBytes = Convert.FromBase64String(key);
        var parts = data.ToString().Split(".");

        if (parts == null || parts.Length != 2)
            return null;

        var cipherText = parts[0];
        var iv = Convert.FromBase64String(parts[1]);
        var json = cipherText.DecryptString(keyBytes, iv);

        return json?.JsonToObject<T>();
    }

    public static string EncryptObject<T>(T obj, string key) where T : class, new()
    {
        var json = obj.ObjectToJson();
        var keyBytes = Convert.FromBase64String(key);
        var iv = RandomNumberGenerator.GetBytes(16);
        return $"{json.EncryptString(keyBytes, iv)}.{Convert.ToBase64String(iv)}";
    }
}
