namespace LazyBean.Application.Common.Interfaces;

public interface IAiKeyProtector
{
    string EncryptKey(string plainKey);
    string DecryptKey(string encryptedKey);
    string? MaskKey(string? key);
}
