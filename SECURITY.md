# Security policy

## Supported versions

Security fixes are made against the latest source on the `main` branch. Check the GitHub Releases page for the latest available binary.

## Reporting a vulnerability

Please do not report exploitable vulnerabilities in a public issue. Use GitHub's private vulnerability reporting feature from the repository's **Security** tab when available. If private reporting is not enabled, contact the repository maintainer through the GitHub profile before sharing technical details publicly.

Do not send VPN passwords, pre-shared keys, private certificates, or DPAPI-protected settings files in a report. Provide the affected version, Windows version, impact, and a minimal reproduction with secrets removed.

## Security notes

- The application requests administrator access to configure Windows VPN profiles and routes.
- Remembered credentials use Windows DPAPI with the current-user scope.
- SSTP certificate validation is performed by Windows and is not bypassed by this application.
- Review the source and verify the published SHA-256 checksum before running a release.
