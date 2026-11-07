using System.Security.Cryptography;
using System.Text;

namespace LazyBean.Collector.App.Security;

// Keeps the device token encrypted with DPAPI for the current Windows user. Another Windows account,
// or a copy of the file on another PC, cannot decrypt it. The entropy string ties the blob to this app.
public sealed class TokenStore(string path)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LazyBeanCollector.DeviceToken.v1");

    public void Save(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, protectedBytes);
    }

    // Returns null when there is no token or it cannot be read (for example after a Windows profile change).
    public string? Load()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Clear()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
