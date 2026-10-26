# Audit and change plan

## Audit (before implementation)

| Item | Finding |
| --- | --- |
| Language and UI | C# on .NET 8, WPF with a small MVVM view model (`MyVpnApp/MainWindow.xaml`, `MyVpnApp/MainViewModel.cs`). |
| Target | `net8.0-windows`, x64 Release build. |
| VPN connection | Windows RAS profile configured by `Add-VpnConnection` / `Set-VpnConnection` through Windows PowerShell, then connected and disconnected with `rasdial.exe` (`MyVpnApp.Core/VpnService.cs`). Routes use `route.exe`; gateway discovery uses the PPP block from `ipconfig /all`, maps its local IPv4 address with `Get-NetIPAddress`, and reads the peer from its Default Gateway or interface routes. |
| Current executable | `MyVpnApp/artifacts/verified-final/Release/net8.0-windows/MyVpnApp.exe`: 151,552 bytes (148 KiB). It is the framework-dependent .NET app host, not a standalone deployment. Its adjacent managed files include `MyVpnApp.dll` (49,152 bytes), `MyVpnApp.Core.dll` (77,312 bytes), `.deps.json`, and `.runtimeconfig.json`. PDBs are build artifacts and are not needed at runtime. `dumpbin.exe` is not installed in this environment, so a native import report cannot be produced for this managed app host. |
| Runtime dependencies | Requires a compatible .NET 8 Desktop Runtime for WPF. The application also invokes Windows PowerShell (`powershell.exe`), `rasdial.exe`, `route.exe`, and `ipconfig.exe`; these are not bundled. A self-contained .NET publish would include a much larger runtime and still would not make .NET 8 supported on Windows 7. |
| OS compatibility with nothing installed | The current framework-dependent app cannot run on a clean machine without the .NET Desktop Runtime. .NET 8 does not support Windows 7 SP1 or Windows 8.1; its current Windows support table lists Windows 10 LTSC/Enterprise releases and Windows 11. See [Microsoft's .NET on Windows requirements](https://learn.microsoft.com/en-us/dotnet/core/install/windows). |
| Native build tools | `cl.exe`, `dumpbin.exe`, CMake, and other C++ compilers are absent from PATH and the installed Visual Studio directory. Without a native compiler, a C++ port cannot be built, sized, or tested in this environment. |

## Requirement gap table

| Requirement | Status | Existing implementation |
| --- | --- | --- |
| Credentials, SSTP/L2TP, split tunneling, DPAPI, disconnect monitoring | Partial | `VpnService.ConnectAsync` configures SSTP/L2TP and split tunneling, `CredentialProtector` uses DPAPI, `MainViewModel.PollConnectionAsync` polls for drops. RAS error messages are mapped in `VpnService.FriendlyError`, but error-code coverage and the credential workflow depend on external PowerShell and `rasdial`. |
| SSTP certificate help and optional CA import | Partial | `VpnService.FriendlyError` offers generic certificate guidance. There is no specific 0x800B0109 / 0x800B010F / 0x80092013 help, bundled CA import, hostname warning, or Windows 7 TLS 1.2 guidance. |
| L2TP behind NAT | Done | `NatTraversalSettings.EnableL2tpNatTraversal` writes the requested DWORD; `MainViewModel.EnableNatTraversalAsync` tells the user to reboot. |
| CIDR routes, validation, normalization, list controls | Done | `CidrHelper`, `RouteStore`, `RouteManager`, and the route grid in `MainWindow.xaml` implement the required IPv4 route workflow. |
| Automatic gateway discovery | Improved | `GatewayResolver.ResolveAsync` parses the matching PPP adapter from `ipconfig /all`, resolves its local address to an interface index, then uses the PPP peer from Default Gateway or interface routes. If Windows reports no peer, the route uses `0.0.0.0` on the resolved VPN interface. A missing interface index is reported as a route failure rather than guessed. |
| Apply, delete, drop cleanup, stale-route record, per-route status | Partial | `RouteManager` retries route additions, tracks applied routes, and cleans stale records; `MainViewModel` applies and removes routes. The path depends on a resolvable gateway/interface and external processes, and real VPN behavior has not been tested here. |
| Atomic settings and route persistence | Done | `RouteStore`, `SettingsStore`, and `AtomicJsonFile` save to the app data directory with corruption backup. `CredentialProtector` encrypts saved credentials with DPAPI. |
| Responsiveness and usability | Partial | Async commands and an activity log exist. There is no single-instance guard, tray icon, or log-copy button. |
| Manifest | Partial | `MyVpnApp/app.manifest` requests administrator access. It has no supportedOS declarations or DPI-awareness setting. |
| Automated tests | Done | `MyVpnApp.Tests` contains xUnit tests for CIDR parsing, route persistence, route management, gateway parsing, DPAPI, and VPN service behavior. The latest verified run passed 34 tests. |

## Native implementation update

After installing the selected MSVC C++ workload and SDK, a native Win32 build was added in `native\MyVpnAppNative.sln`. The managed solution remains intact. The native UI uses Windows RAS and IP Helper APIs (not an SSTP/L2TP implementation), embeds a requireAdministrator manifest, statically links the CRT, and targets Windows 7 SP1 minimum. It stores settings, protected secrets, route list, and applied-route cleanup data under `%APPDATA%\MyVpnApp`.

Verified in this environment:

- The native Visual Studio Release x64 solution builds successfully with MSBuild.
- The native test harness passes CIDR/mask/normalization checks, invalid-input checks, DPAPI encryption/decryption, route JSON save/reload, and corrupt-file backup/recovery.
- The managed xUnit suite passes all 34 tests (these exercise the legacy managed route manager, not native IP Helper calls). The native harness covers CIDR, DPAPI, and persistence, while real native RAS/routing still requires manual verification.
- The native executable has been inspected with `dumpbin /dependents`; it imports Windows system DLLs and no .NET runtime or PowerShell dependency.
- The managed self-contained `dotnet publish` was attempted but could not restore the Windows x64 runtime pack because `api.nuget.org` was unreachable in this environment. The .NET 8 target also does not meet the Windows 7 compatibility target; the verified distribution build is the native executable.
- No real VPN server or Windows 7 test machine was available, so live SSTP/L2TP, route forwarding, elevation UX, and compatibility on older Windows still need the manual checks in `MANUAL_TESTS.md`.

## Size record

| Milestone | Executable size |
| --- | ---: |
| Existing framework-dependent .NET app host, before changes | 151,552 bytes (148 KiB) |
| Native Release x64 build | 314,880 bytes (308 KiB) |
