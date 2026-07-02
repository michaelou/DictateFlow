# DictateFlow

A lightweight Windows system-tray application that provides AI-powered dictation anywhere in Windows. DictateFlow is architected as an extensible AI pipeline, not a single-purpose utility:

```
Microphone → Speech Recognition → Pipeline Context → Prompt Resolution → LLM Enhancement → Output
```

All providers (speech, LLM, output) are replaceable via interfaces registered in dependency injection.

## Status

**M6: User experience** — the full dictation loop works end to end: global hotkey recording (M2), Azure AI Foundry transcription (M3), prompt-mode LLM enhancement (M4), automatic paste / preview output with history writes (M5), plus a searchable History window, a cost dashboard with configurable pricing, a technical dictionary editor, per-application prompt-mode rules, and polished overlay/error messaging with configurable logging (M6). Next: provider registry (M7), import/export & startup polish (M8).

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
- **Settings** — recording, providers, prompts, dictionary, application rules, output, history and pricing
- **History** — searchable list of past dictations (copy, delete, clear all)
- **Cost Dashboard** — estimated speech/LLM costs for today, this month and lifetime
- **Exit** — shuts the app down cleanly

## Test

```powershell
dotnet test
```

## Solution layout

```
DictateFlow.sln
src/
  DictateFlow.Core/                    # Interfaces, models, pipeline abstractions, settings models (no WPF/vendor references)
  DictateFlow.App/                     # WPF tray app (net10.0-windows): views, viewmodels, Windows-specific services, DI bootstrap
  DictateFlow.Providers.AzureFoundry/  # Azure AI Foundry speech + LLM providers
tests/
  DictateFlow.Tests/                   # xUnit + Moq unit tests
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
