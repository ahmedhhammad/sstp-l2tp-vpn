using Microsoft.Win32;

namespace MyVpnApp.Core;

public static class NatTraversalSettings
{
    public static void EnableL2tpNatTraversal()
    {
        using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\PolicyAgent", writable: true)
            ?? throw new InvalidOperationException("Could not open the PolicyAgent registry key.");
        key.SetValue("AssumeUDPEncapsulationContextOnSendRule", 2, RegistryValueKind.DWord);
    }
}
