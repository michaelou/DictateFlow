# Releasing DictateFlow

DictateFlow is released manually from a local machine. One script,
[`scripts/release.ps1`](../scripts/release.ps1), builds the installer and portable ZIP,
tags the commit, and publishes a GitHub release. There is **no CI/CD and no auto-update** —
users update by downloading a newer release (the app can *check* for one; see below).

## Required local tools

| Tool | Purpose | Install |
| --- | --- | --- |
| **.NET 10 SDK** | Build & publish the app | <https://dotnet.microsoft.com/download/dotnet/10.0> |
| **Inno Setup 6.3+** | Build the installer (`ISCC.exe`) | <https://jrsoftware.org/isdl.php> |
| **GitHub CLI** (`gh`) | Create the GitHub release | <https://cli.github.com/> — then run `gh auth login` |
| **Git** | Tag & push | already required to work on the repo |

`ISCC.exe` is found automatically on `PATH` or in `C:\Program Files (x86)\Inno Setup 6\`.
If it lives elsewhere, pass `-InnoSetupPath`.

## One-line release

```powershell
.\scripts\release.ps1 -Version 0.1.0
```

That runs the whole pipeline:

1. `dotnet clean`
2. `dotnet publish -c Release -r win-x64 --self-contained true` with the assembly version set to `-Version`
3. builds the Inno Setup installer
4. creates the portable ZIP
5. creates and pushes the git tag `v0.1.0`
6. creates the GitHub release and uploads both artifacts

### Outputs

```
artifacts/
  DictateFlowSetup-v0.1.0.exe      # Inno Setup installer
  DictateFlowPortable-v0.1.0.zip   # portable (unzip and run DictateFlow.exe)
  publish/win-x64/                 # raw publish output (source for both of the above)
```

The `artifacts/` folder is git-ignored.

## Versioning

The version is passed once, to `-Version`, and used everywhere:

- the application assembly version (`-p:Version`), shown in **Settings → Diagnostics**;
- the installer and portable ZIP filenames;
- the git tag and GitHub release tag (`v<version>`).

Accepted formats: `1.2.3` or `1.2.3-beta.1`. The default dev version (when you just
`dotnet build` without the script) is set in
[`DictateFlow.App.csproj`](../src/DictateFlow.App/DictateFlow.App.csproj).

## Useful options

| Option | Effect |
| --- | --- |
| `-ReleaseNotes "text"` | Use explicit release notes instead of GitHub's auto-generated notes |
| `-Prerelease` | Mark the GitHub release as a pre-release |
| `-SkipGitHubRelease` | Build the installer and ZIP only — no tag, push, or release |
| `-InnoSetupPath "C:\...\ISCC.exe"` | Point at a non-default Inno Setup location |

Example — build the artifacts locally without publishing anything:

```powershell
.\scripts\release.ps1 -Version 0.1.0 -SkipGitHubRelease
```

## The installer

[`installer/DictateFlow.iss`](../installer/DictateFlow.iss) packages the publish output:

- App name **DictateFlow**, publisher **Belugga**, default path `C:\Program Files\DictateFlow`.
- Installs for all users by default, so it **prompts for UAC elevation** on launch (writing
  to Program Files requires admin rights). The user can drop to a per-user install (into
  `%LocalAppData%\Programs`, no elevation) from the wizard if they prefer.
- Creates a Start Menu shortcut and an optional (unchecked) Desktop shortcut.
- Supports uninstall and uses the app icon.

You can build just the installer by hand after a publish:

```powershell
iscc /DMyAppVersion=0.1.0 /DPublishDir="..\artifacts\publish\win-x64" /DOutputDir="..\artifacts" installer\DictateFlow.iss
```

## Check for updates (in-app)

Right-click the tray icon → **Check for Updates…**. DictateFlow queries the latest GitHub
release for `michaelou/DictateFlow`, compares its tag with the installed version, and:

- if a newer version exists, shows a dialog with the current version, the latest version and
  the release notes. When the release publishes an installer asset
  (`DictateFlowSetup-v<version>.exe`), the dialog offers **Download & install**: DictateFlow
  downloads the installer (with a progress bar), launches the setup wizard, and closes so the
  wizard can replace the running files. There is also an **Open release page** button to
  download manually (used as a fallback and for portable installs);
- otherwise reports that you're up to date;
- if the network is unavailable, reports the failure gracefully.

The update check itself never runs automatically — it happens only when you click
**Check for Updates…**, and nothing is downloaded until you click **Download & install**.

The in-app installer relies on the release asset being named `DictateFlowSetup-v*.exe`, which
`scripts/release.ps1` produces by default. The installer sets `CloseApplications=yes` so it can
replace a still-running instance and relaunches DictateFlow as the normal (non-elevated) user
when it finishes.
