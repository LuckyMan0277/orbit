# Code signing policy

Orbit release binaries (`Orbit-Setup.exe` and the `Orbit.exe` it installs) are built by GitHub Actions from this repository's tagged commits (`.github/workflows/build.yml`). Code signing is being set up through the SignPath Foundation's free program for open-source projects; until it is approved, releases are **unsigned**, and Windows Smart App Control or SmartScreen may block them.

## Roles

| Role | Who |
| --- | --- |
| Author / committer | [LuckyMan0277](https://github.com/LuckyMan0277) |
| Reviewer | [LuckyMan0277](https://github.com/LuckyMan0277) |
| Approver of signing requests | [LuckyMan0277](https://github.com/LuckyMan0277) (multi-factor authentication required on the GitHub and SignPath accounts) |

Only binaries built by the workflow from a `v*` tag in this repository are submitted for signing. Nothing is signed from a developer machine.

## Privacy policy

Orbit does not collect telemetry. It makes these network connections only:

- **Update check**: once when the app starts, an anonymous `GET` to `https://api.github.com/repos/LuckyMan0277/orbit/releases/latest`. If the user clicks the update button, the installer is downloaded from this repository's GitHub Releases and its SHA-256 digest is verified when GitHub provides one.
- **Remote access** (opt-in, off by default): the user can expose their own PC through Tailscale Funnel or a Cloudflare tunnel, and can link an account on an account service whose address the user configures (`cloud/` is the reference implementation). When linked, the PC sends its current public URL and a device token to that service. Nothing is sent unless the user turns this on.

This program will not transfer any other information to networked systems unless specifically requested by the user.

## What the signed files contain

`Orbit.exe` is compiled from `native/*.cs` with the .NET Framework C# compiler. The installer bundles `Orbit.exe`, the WebView2 wrapper DLLs, the built front-end in `web/`, and `cloudflared.exe` (downloaded by `scripts/build.ps1` and checked against a pinned SHA-256). No prebuilt code from this project is bundled other than what the build produces.
