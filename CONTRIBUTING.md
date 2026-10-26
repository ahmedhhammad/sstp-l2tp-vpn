# Contributing

Contributions are welcome. Please open an issue to discuss larger changes before sending a pull request.

## Before opening a pull request

1. Create a focused branch from `main`.
2. Build the managed application and run its tests:

   ```powershell
   dotnet build .\SarmadiVpn\SarmadiVpn.csproj -c Release
   dotnet test .\SarmadiVpn.sln -c Release
   ```

3. For changes to the native project, build `native\SarmadiVpnNative.sln` in Visual Studio and run `native\run-tests.ps1`.
4. Update user documentation for visible behavior or configuration changes.
5. Do not commit passwords, L2TP PSKs, private keys/certificates, app-data files, or unredacted VPN logs.

## Pull requests

- Explain the user-facing problem and the approach.
- Include test results and note any manual checks that still need a live VPN server.
- Keep unrelated formatting or generated build output out of the change.
- Do not add a way to disable Windows VPN server certificate validation.
