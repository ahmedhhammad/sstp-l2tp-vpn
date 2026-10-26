# MyVpnApp

MyVpnApp is a Windows desktop client for the built-in Windows VPN stack. It configures an SSTP or L2TP/IPsec profile, connects with `rasdial`, and manages user-defined IPv4 split-tunnel routes. It does not implement VPN protocols.

## Requirements

- Windows 10 or 11, x64
- .NET 8 SDK
- Administrator rights (the application manifest requests elevation)
- A reachable SSTP or L2TP/IPsec server and valid credentials

## Build and run

From the repository root in PowerShell:

```powershell
dotnet restore .\MyVpnApp.sln
dotnet build .\MyVpnApp.sln -c Release
dotnet run --project .\MyVpnApp\MyVpnApp.csproj
```

The application stores its files under `%APPDATA%\MyVpnApp`:

- `settings.json` stores the server, username, protocol, and remember-credentials choice. A remembered password and the L2TP PSK are encrypted with Windows DPAPI for the current user.
- `routes.json` stores the route list. The file changes only when the user adds, deletes, or toggles a route.
- `applied-routes.json` records routes added during a session so normal exit and the next launch can clean them up.

Writes use a temporary file followed by a replace. A corrupt settings or route list is moved aside with a `.corrupt-<timestamp>` suffix.

## Tests

```powershell
dotnet test .\MyVpnApp.sln -c Release
```

The tests cover CIDR parsing and masks, JSON persistence and recovery, route command construction and retries, gateway parsing, and DPAPI round trips. DPAPI tests require Windows and run under the current Windows user.

## Publish a single-file executable

```powershell
dotnet publish .\MyVpnApp\MyVpnApp.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The executable is produced in `MyVpnApp\bin\Release\net8.0-windows\win-x64\publish\MyVpnApp.exe`.

## L2TP behind NAT

Use **Enable L2TP behind NAT** to set `HKLM\SYSTEM\CurrentControlSet\Services\PolicyAgent\AssumeUDPEncapsulationContextOnSendRule` to DWORD `2`. The app runs elevated, but Windows must be restarted before the setting takes effect. The VPN server and network firewall must also allow the required L2TP/IPsec traffic.

## Connection errors

Profile creation or update errors are reported separately from rasdial connection errors. The activity log removes PowerShell's CLIXML wrapper and shows the readable Windows error text. rasdial failures include the RAS error code when Windows provides one, with targeted guidance for credential, SSTP certificate, L2TP/IPsec, and DNS or reachability problems. Passwords and L2TP keys are hidden from command logs and redacted from process output.

## Known limitations

- A live SSTP/L2TP server and credentials are required for end-to-end connection checks. The application relies on Windows VPN cmdlets and `rasdial`, so behavior can vary with Windows policy, server configuration, certificates, and firewall rules.
- Windows `route delete` identifies a route by destination and mask. If another application installs the same destination and mask, Windows may not distinguish ownership during cleanup.
- Forced process termination cannot run cleanup. The applied-route record allows the next app launch to attempt cleanup; cleanup still depends on Windows accepting the delete command.
- `ipconfig` does not report an interface index. If PowerShell cannot identify the active VPN adapter, gateway discovery fails and route application reports the error rather than guessing an interface.
- The app profile is named `MyVpnApp`; an existing profile with that name is updated when connecting.
