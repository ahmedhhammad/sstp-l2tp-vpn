# Changelog

User-visible changes for SARMADI VPN are recorded here.

## 0.1.0-preview.6

- Enlarge the upper-left taskbar status dot to match the requested size.

## 0.1.0-preview.5

- Draw the connection status dot in the upper-left whitespace of the taskbar app icon.
- Remove the Windows-positioned overlay badge that placed the dot at the opposite corner.

## 0.1.0-preview.4

- Fix the taskbar status overlay interface identifier so Windows can apply the state dot.
- Wait for Windows to create the taskbar button before setting or restoring the overlay.

## 0.1.0-preview.3

- Refresh the VPN status dot through the Windows taskbar overlay API while keeping the application logo unchanged.

## 0.1.0-preview.2

- Store managed and native app data under `%APPDATA%\SarmadiVpn`.
- Copy existing settings, routes, and route cleanup state on first launch without deleting the previous files.

## 0.1.0-preview.1

- Initial public preview of the managed .NET 8 WPF client and native Win32 alternative.
- Windows RAS-based SSTP and L2TP/IPsec connection management.
- IPv4 CIDR route persistence and connect/disconnect cleanup.
- DPAPI-protected remembered credentials.
- Release automation for a self-contained Windows x64 executable.

This preview has not been verified against a live SSTP/L2TP server. See [MANUAL_TESTS.md](MANUAL_TESTS.md).
