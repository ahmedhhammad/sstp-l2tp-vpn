# SARMADI VPN

SARMADI VPN is a Windows desktop client for SSTP and L2TP/IPsec. It configures Windows' built-in Remote Access Service (RAS), connects with `rasdial`, and applies the user's IPv4 split-tunnel routes while connected. It does not implement VPN protocols or install a network driver.

> **Preview:** The application and automated tests build successfully, but a live SSTP/L2TP server and end-to-end routing have not been verified by this project. Use the manual checklist below before relying on it.

## Download and run

Download `SarmadiVpn-win-x64.exe` and its `SHA256SUMS.txt` from the project's [GitHub Releases](https://github.com/ahmedhhammad/sstp-l2tp-vpn/releases). Releases are created by the tag workflow for version tags (`v*`). The executable is a self-contained, single-file .NET 8 WPF application for 64-bit Windows; no .NET runtime installation is needed.

The app requests administrator access at launch because it manages Windows VPN profiles, routes, and an optional L2TP NAT registry setting. Review the source and release checksum before running it. There is no installer or automatic update service.

## Features

- Connect to SSTP or L2TP/IPsec using the Windows RAS client.
- Remember credentials encrypted with Windows DPAPI for the current Windows user.
- Add enabled IPv4 destination routes in address or CIDR form. Routes are installed after connection and removed at disconnect; stale app-owned routes are cleaned up on the next launch.
- Detect connection drops and show connection and per-route status in the activity log.
- Set the Windows L2TP NAT traversal option when required (a reboot is needed).

## SSTP certificate requirements

Enter the DNS hostname that appears in the SSTP server certificate. Windows must trust the certificate chain, and the certificate must be valid and configured for server authentication. RAS error 778 means Windows could not verify the server identity; it is not evidence that the username or password is wrong. Ask the VPN server administrator to verify the certificate, chain, expiry, and SSTP binding. SARMADI VPN does not disable certificate validation.

## Build from source

Requirements: Windows 10 or 11 x64, the .NET 8 SDK, and (for Visual Studio) the **.NET desktop development** workload. Open `SarmadiVpn.sln`, set **SARMADI VPN** as the startup project, and start Visual Studio elevated when testing route or VPN profile operations.

From PowerShell at the repository root:

```powershell
dotnet restore .\SarmadiVpn.sln
dotnet build .\SarmadiVpn\SarmadiVpn.csproj -c Debug
dotnet run --project .\SarmadiVpn\SarmadiVpn.csproj
dotnet test .\SarmadiVpn.sln -c Release
```

Build a self-contained, single-file x64 preview executable and SHA-256 checksum:

```powershell
.\scripts\publish-release.ps1 -Version 0.1.0-preview.1
```

The output is written under `dist\0.1.0-preview.1\`. To publish another build, pass a new version string. The script runs the xUnit suite before publishing.

To create a GitHub release after the repository is pushed, push a version tag such as `v0.1.0-preview.1`. The Windows release workflow runs the tests, builds the self-contained EXE, and attaches it and its checksum file to that tag's GitHub Release. Pull requests and pushes to `main` also build the EXE and retain it as a short-lived CI artifact.

## Configure a VPN

1. Enter the VPN server address, username, password, and protocol. For L2TP/IPsec, enter the pre-shared key.
2. Optionally select **Remember credentials**. The password and L2TP key are protected using DPAPI and can only be decrypted by the same Windows user on the same machine.
3. Add one or more IPv4 destinations such as `192.168.1.50` or `192.168.1.0/24`. `0.0.0.0/0` is rejected to prevent replacing the default route.
4. Connect. The client discovers the PPP interface and peer from Windows networking information, then applies enabled routes. Check the activity log and route status if discovery or route installation fails.

The app stores settings, routes, and applied-route state at `%APPDATA%\SarmadiVpn`. On first launch after upgrading from an earlier build, existing data is copied into this folder; the original files are kept as a backup. Saved passwords and L2TP keys remain DPAPI-encrypted for the current Windows user.

## L2TP behind NAT

Use **Enable L2TP behind NAT** to set `HKLM\SYSTEM\CurrentControlSet\Services\PolicyAgent\AssumeUDPEncapsulationContextOnSendRule` to DWORD `2`. Restart Windows before connecting. The server and network firewalls must also allow L2TP/IPsec traffic.

## Tests and manual verification

```powershell
dotnet test .\SarmadiVpn.sln -c Release
```

The xUnit suite covers CIDR parsing, persistence, route command behavior, gateway parsing, credential protection, and VPN service behavior using a mock process runner. It does not prove connectivity to a real VPN server. Follow [MANUAL_TESTS.md](MANUAL_TESTS.md) on a Windows machine with a test VPN endpoint and reachable routed hosts.

## Repository layout

- `SarmadiVpn/` — WPF desktop client.
- `SarmadiVpn.Core/` — VPN, route, settings, and credential logic.
- `SarmadiVpn.Tests/` — xUnit tests.
- `native/` — separate native Win32 alternative for users who need a build without .NET. It uses the Windows RAS and IP Helper APIs and has its own Visual Studio solution and test harness.
- `.github/workflows/` — pull request CI and tag-based Windows release publishing.

The native alternative has a separate build and manual verification path; the downloadable x64 release is the managed WPF app described above.

## Known limitations

- Live SSTP/L2TP authentication, certificate validation, and remote route reachability require a real VPN server and have not been verified in CI.
- The app requires administrator elevation for its Windows networking operations.
- Saved DPAPI credentials do not transfer to another Windows user or computer.
- Route ownership can be ambiguous if another program adds an identical route.
- A route that Windows refuses to remove remains recorded for a later cleanup attempt.
- The WPF .NET 8 application is for supported current Windows versions. Use the separate native project for its Windows 7 SP1 target.

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md) for build and pull request guidance and [SECURITY.md](SECURITY.md) for vulnerability reporting. Do not include VPN credentials, PSKs, private certificates, or unredacted connection logs in issues.
