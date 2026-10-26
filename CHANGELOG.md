# Changelog

User-visible changes for SARMADI VPN are recorded here.

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
