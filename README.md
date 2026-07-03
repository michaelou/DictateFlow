# DictateFlow

A lightweight Windows system-tray application that provides AI-powered dictation anywhere in Windows. DictateFlow is architected as an extensible AI pipeline, not a single-purpose utility:

```
Microphone → Speech Recognition → Pipeline Context → Prompt Resolution → LLM Enhancement → Output
```

All providers (speech, LLM, output) are replaceable via interfaces registered in dependency injection.

## Status

**M8: Polish** — feature-complete V1, hardened for daily use: global hotkey recording (M2), Azure AI Foundry transcription (M3), prompt-mode LLM enhancement (M4), automatic paste / preview output with history writes (M5), history window / cost dashboard / dictionary / app rules (M6), a named provider registry (M7, see [docs/extensibility.md](docs/extensibility.md)), and production polish (M8): settings validation with inline errors and a startup mock-provider fallback, settings import/export (API keys excluded unless opted in) and prompts zip import/export, launch-with-Windows with stale-path reconciliation, sub-2-second tray startup with deferred initialization and a first-run welcome, window-state persistence, an overlay that fades and follows the focused monitor, and a Diagnostics settings page with a log viewer and secrets-redacted copyable report.

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Build

```powershell
dotnet build
```

## Run

```powershell
dotnet run --project src/DictateFlow.App
```

The app starts with no window and puts a **DictateFlow icon in the system tray**. Right-click it for the menu:

- **Dictate** — starts/stops a dictation (also available on the global hotkey, default `Ctrl+Alt+D`)
- **Settings** — recording, providers, prompts, dictionary, application rules, output, history, pricing, backup (import/export) and diagnostics
- **History** — searchable list of past dictations (copy, delete, clear all)
- **Cost Dashboard** — estimated speech/LLM costs for today, this month and lifetime
- **Check for Updates** — compares the installed version with the latest GitHub release (manual; never auto-downloads)
- **Exit** — shuts the app down cleanly

## Test

```powershell
dotnet test
```

## Release

Packaging (installer + portable ZIP) and publishing a GitHub release is a manual local
process driven by [`scripts/release.ps1`](scripts/release.ps1):

```powershell
.\scripts\release.ps1 -Version 0.1.0
```

See [docs/release.md](docs/release.md) for prerequisites and details.

## Solution layout

```
DictateFlow.sln
src/
  DictateFlow.Core/                    # Interfaces, models, pipeline abstractions, provider registry, settings models (no WPF/vendor references)
  DictateFlow.App/                     # WPF tray app (net10.0-windows): views, viewmodels, Windows-specific services, DI bootstrap
  DictateFlow.Providers.AzureFoundry/  # Azure AI Foundry speech + LLM providers
  DictateFlow.Providers.AzureSpeech/   # Azure real-time speech provider (streaming transcription via the Speech SDK)
  DictateFlow.Providers.WhisperCpp/    # Local whisper.cpp transcription provider (fully offline)
samples/
  DictateFlow.Samples.NullOutput/      # Minimal output provider proving the one-class-one-line extensibility claim
tests/
  DictateFlow.Tests/                   # xUnit + Moq unit tests
docs/
  extensibility.md                     # How to add a speech/LLM/output provider
```

## App data

All user data lives in `%APPDATA%\DictateFlow\`:

```
%APPDATA%\DictateFlow\
  settings.json                  # Application settings (JSON)
  dictateflow.db                 # SQLite database (history, usage records)
  logs\dictateflow-YYYYMMDD.log  # Rolling daily log files
  Prompts\                       # Prompt definitions (populated in M4)
```

If `settings.json` is missing or unreadable, the app falls back to defaults and logs a warning — it never crashes on bad config.
