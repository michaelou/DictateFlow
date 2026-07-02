# DictateFlow

A lightweight Windows system-tray application that provides AI-powered dictation anywhere in Windows. DictateFlow is architected as an extensible AI pipeline, not a single-purpose utility:

```
Microphone → Speech Recognition → Pipeline Context → Prompt Resolution → LLM Enhancement → Output
```

All providers (speech, LLM, output) are replaceable via interfaces registered in dependency injection.

## Status

**M1: Foundation** — the application skeleton is in place: tray icon with menu, Settings window shell, settings persistence, SQLite database, logging and global exception handling. Dictation functionality arrives in later milestones (M2: audio recording, M3: transcription, M4: LLM enhancement, M5: output pipeline, M6: history & cost tracking).

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

- **Dictate** — stub (arrives with M2/M3)
- **Settings** — opens the settings window
- **History** — stub (arrives with M6)
- **Cost Dashboard** — stub (arrives with M6)
- **Exit** — shuts the app down cleanly

## Test

```powershell
dotnet test
```

## Solution layout

```
DictateFlow.sln
src/
  DictateFlow.Core/        # Interfaces, models, pipeline abstractions, settings models (no WPF/Windows references)
  DictateFlow.App/         # WPF tray app (net10.0-windows): views, viewmodels, Windows-specific services, DI bootstrap
tests/
  DictateFlow.Tests/       # xUnit + Moq unit tests
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
