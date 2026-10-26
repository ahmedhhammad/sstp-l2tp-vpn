# MyVpnApp

MyVpnApp is a Windows desktop GUI for SSTP and L2TP/IPsec using the Windows RAS client. It configures a Windows VPN profile and adds IPv4 split routes after the connection is established. The native build targets Windows 7 SP1 and later and has no .NET runtime dependency.

## Visual Studio: build and debug

Open `native\MyVpnAppNative.sln` in Visual Studio 2026 with **Desktop development with C++**, the MSVC x64 tools, and a Windows SDK installed. Choose **Debug | x64** and press **F5**. Start Visual Studio as administrator so the debugger can attach to the elevated app. The executable requests administrator elevation because route management and the NAT setting require it.

The original managed solution is still available as `MyVpnApp.sln`; it contains the .NET 8 WPF application and xUnit tests. In that solution select **MyVpnApp** (the WPF app) as the startup project. `MyVpnApp.Core` is a class library and cannot run directly.

## Native build

In a Visual Studio Developer PowerShell:

```powershell
msbuild .\native\MyVpnAppNative.sln /t:Build /p:Configuration=Release /p:Platform=x64
```

The standalone, statically linked executable is `native\bin\Release\MyVpnApp.exe`. It is a single-file Win32 application. The project links the C runtime statically and embeds `native\app.manifest` with `requireAdministrator`. Release uses size optimization. `native\run-tests.ps1` compiles and runs the native logic checks.

The original .NET WPF application can be built and published separately:

```powershell
dotnet restore .\MyVpnApp.sln
dotnet build .\MyVpnApp.sln -c Release
dotnet run --project .\MyVpnApp\MyVpnApp.csproj
dotnet publish .\MyVpnApp\MyVpnApp.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The .NET 8 build does not support Windows 7; use the native project for the Windows 7 target.

## Automated checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\native\run-tests.ps1
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-cli')
dotnet test .\MyVpnApp.sln -c Release --no-restore
```

The native checks cover IPv4 CIDR parsing and normalization, dotted masks, invalid input, DPAPI encryption/decryption, and route-list save/reload. The managed xUnit suite additionally tests route command building, route retry and cleanup behavior, gateway parsing, settings persistence, and credential protection. Neither suite can prove behavior against a live VPN server; use [MANUAL_TESTS.md](MANUAL_TESTS.md) for Windows verification.

## Data and credentials

Application data is stored under `%APPDATA%\MyVpnApp`:

- `settings.json` contains the server, username, protocol, remember choice, and DPAPI-protected secrets. Passwords and PSKs are never written as plaintext.
- `routes.json` stores route destinations, labels, and enabled state. Routes persist until explicitly deleted.
- `applied-routes.json` tracks routes installed by the current run so they can be removed on disconnect or at the next start after a crash.

Writes use a temporary file and replace/move operation. Corrupt JSON data is backed up with a `.corrupt-<timestamp>` suffix when detected.

## L2TP behind NAT

Use **Enable L2TP behind NAT** to set `HKLM\SYSTEM\CurrentControlSet\Services\PolicyAgent\AssumeUDPEncapsulationContextOnSendRule` to DWORD `2`. Restart Windows before connecting. The VPN server and firewalls must also permit L2TP/IPsec traffic.

## Known limitations

- A live SSTP/L2TP server, credentials, and reachable routed hosts are needed for end-to-end checks. No live VPN connection was available in the build environment.
- Native tests cover CIDR, DPAPI, and persistence logic, but do not mock the native RAS and IP Helper calls. The 32-test xUnit suite covers the legacy managed route manager; actual native route installation and cleanup still need the manual checks.
- Windows 7 uses its legacy networking APIs and needs current servicing updates and TLS 1.2 enabled for modern SSTP endpoints. See the SSTP certificate help button for common certificate failures.
- A failed route cleanup remains in the applied-route record for a later retry. Windows route APIs identify a route by its route tuple, so another program installing an identical route can make ownership ambiguous.
- If Windows cannot match the PPP local address to a network interface, automatic route setup is reported as failed rather than guessing an interface.
- The embedded elevation manifest prompts at launch. The app does not install a VPN driver or alter server configuration.
