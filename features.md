# DictateFlow — Features

DictateFlow is a lightweight Windows system-tray app that gives you AI-powered dictation anywhere in Windows. Press a hotkey, speak, and polished text appears in whatever app you're working in.

```
Microphone → Speech Recognition → Prompt Resolution → LLM Enhancement → Output
```

## Core dictation

- **Dictate into any Windows app** — the result is delivered straight into the focused window (email client, IDE, browser, chat…), no copy-pasting required.
- **Global hotkeys** with two independent styles:
  - **Push-to-talk** — recording runs while the chord is held (default `Ctrl+Alt+D`).
  - **Toggle** — press once to start, again to stop.
  - Hotkeys support side-specific modifiers (e.g. right-Ctrl only) and modifier-only chords.
- **Tray-first design** — no main window; everything lives in a system-tray icon with a right-click menu (Dictate, Prompt Mode picker, Settings, History, Cost Dashboard, Check for Updates, Exit).
- **Microphone selection** — pick a specific capture device or follow the system default.
- **Silence auto-stop** — recording ends automatically after a configurable stretch of silence.
- **On-screen overlay** — a small always-on-top overlay shows the current state (listening with a live audio-level indicator, processing, success, error), fades in/out, and follows the monitor that has focus.
- **Streaming transcription (optional)** — with a streaming-capable provider, transcription happens *while you speak* and the partial transcript appears live on the overlay. If the streaming session fails, the completed recording is transcribed through the normal path — dictation never breaks.

## Speech-to-text providers

- **Azure AI Foundry** — cloud transcription via Azure-hosted models.
- **Azure Speech** — real-time streaming recognition through the Azure Speech SDK.
- **Parakeet TDT v3 (fully offline)** — NVIDIA's multilingual speech model (25 European languages, auto-detected) running in-process through the bundled sherpa-onnx runtime; model files download from within Settings with pinned SHA-256 verification.
- **whisper.cpp (fully offline)** — local transcription with no cloud dependency:
  - Built-in **model manager**: download the whisper.cpp engine and Whisper Small/Medium models from within Settings, with progress, cancel, verify, and delete.
  - Every download is pinned by size and SHA-256 checksum and verified against known-good values.
- **Mock provider** — canned output (including a word-by-word streaming demo) so the whole pipeline can be tried without any service configured.

## LLM enhancement & prompt modes

The raw transcript is passed through an LLM with a selectable **prompt mode** that shapes the output:

- **Raw** — punctuation and casing fixes only; your words stay exactly as spoken.
- **Email** — professional business email.
- **GithubIssue** — well-structured GitHub issue in Markdown (title, description, repro steps…).
- **ChatPrompt** — a clear, well-structured prompt for an AI assistant (great for dictating into Claude/ChatGPT).
- **TechnicalSpec** — dictated notes turned into a Markdown technical specification.

Prompt modes are fully user-editable:

- Modes are plain JSON files in a **prompts folder** — add, edit, or delete them freely; the tray menu picks up changes without a restart.
- A built-in **prompt mode editor** with template variables (`{{Transcript}}`, `{{TechnicalDictionary}}`, `{{CurrentDate}}`), per-mode temperature, a resolved-system-prompt preview, and a **test-run** button to try a mode on sample text.
- **Quick switching** from the tray menu — the active mode is checked, and the list refreshes each time the menu opens.

Smart mode selection:

- **Technical dictionary** — a user-defined list of terms (product names, jargon, identifiers) that the LLM is instructed never to alter.
- **Per-application rules** — map foreground apps to prompt modes (e.g. Outlook → Email, Visual Studio → TechnicalSpec). The rule matching the app you were in when recording started wins; otherwise the active mode applies.

LLM providers: **Anthropic (Claude)**, **Ollama** (local server or Ollama Cloud), **Azure AI Foundry**, and a **Mock** provider — each with per-provider temperature/max-token defaults and a "Test connection" button in Settings.

## Output delivery

- **Automatic mode** — text is delivered immediately after enhancement.
- **Preview mode** — a dialog shows the result first; edit, confirm, or cancel before anything is typed.
- Two delivery mechanisms (selectable as providers):
  - **Clipboard paste** — puts the text on the clipboard and sends a paste.
  - **Simulated keyboard** — types the text as real keystrokes.

## History & costs

- **Dictation history** — every dictation (raw transcript + enhanced text) is saved to a local SQLite database; a History window offers search, copy, delete, and clear-all. History can be disabled entirely, and the maximum entry count is configurable with automatic pruning.
- **Cost dashboard** — estimated speech and LLM costs for today, this month, and lifetime, computed from user-editable pricing rates (per audio minute and per million prompt/completion tokens) in your chosen currency. Purely informational — DictateFlow never bills anything.

## Extensible provider architecture

- Three replaceable provider slots — **Transcription**, **LLM**, and **Output** — resolved through a named provider registry.
- Adding a provider is **one class + one registration line**; it automatically appears in the Settings dropdowns and is usable by the pipeline. A sample `NullOutput` provider (with a CI test pinning the claim) proves it.
- Provider selection is read on every dictation — switching providers applies to the next run, no restart needed.
- Per-provider configuration lives in named sections of `settings.json`, read live on every call; missing config falls back to safe defaults.
- Streaming transcription is an optional capability interface, detected at runtime.

## Reliability & polish

- **Fast startup** — sub-2-second to tray, with deferred initialization and a one-time first-run welcome.
- **Safe by default** — mock providers are the fallback, so a broken settings file can never break dictation; unreadable settings fall back to defaults with a logged warning instead of crashing.
- **Settings validation** with inline error messages.
- **Import/export** — settings backup and restore (API keys excluded unless explicitly opted in) and prompt-mode zip import/export.
- **Launch with Windows** (HKCU Run key) with stale-path reconciliation.
- **Window-state persistence** — each window remembers its size and position.
- **Error notifications** — failed dictations surface a clear, user-actionable message (configuration errors point you to Settings).
- **Diagnostics page** — log viewer, configurable log level, quick-open buttons for the data folders, and a copyable diagnostics report with secrets redacted.
- **Manual update check** — compares the installed version against the latest GitHub release; never auto-downloads.
- **Dark theme** UI.

## Privacy & data

- All user data stays local in `%APPDATA%\DictateFlow\`: `settings.json`, a SQLite database (history + usage records), rolling daily logs, and the prompts folder.
- With the whisper.cpp provider and LLM enhancement disabled (Raw handled locally by mock/offline options), dictation can run **fully offline**.
- Cloud calls only go to the providers *you* configure with *your* keys.

## Tech

- Windows 10/11, .NET 10, WPF (MVVM with CommunityToolkit), Serilog logging, SQLite storage, xUnit + Moq test suite.
- MIT licensed. Installer (Inno Setup) and portable ZIP produced by a scripted release process.
