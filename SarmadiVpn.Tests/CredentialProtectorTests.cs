using SarmadiVpn.Core;

namespace SarmadiVpn.Tests;

public sealed class CredentialProtectorTests
{
    [Fact]
    public void ProtectsAndRestoresForCurrentWindowsUser()
    {
        var protector = new CredentialProtector();
        const string value = "test-password-123";
        var protectedValue = protector.Protect(value);
        Assert.NotEqual(value, protectedValue);
        Assert.Equal(value, protector.Unprotect(protectedValue));
    }
}
