using System.Security.Cryptography;
using System.Text;

namespace SarmadiVpn.Core;

public sealed class CredentialProtector
{
    public string Protect(string plaintext) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser));
    public string Unprotect(string protectedValue) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser));
}
