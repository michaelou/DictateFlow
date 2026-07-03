# Extensibility: how to add a provider

DictateFlow has three replaceable provider slots, one per pipeline stage:

| Kind | Interface | Built-in providers |
|---|---|---|
| `Transcription` | `ITranscriptionProvider` | `Mock`, `AzureFoundry`, `AzureSpeech`, `WhisperCpp` |
| `Llm` | `ILLMProvider` | `Mock`, `AzureFoundry` |
| `Output` | `IOutputProvider` | `ClipboardPaste`, `SimulatedKeyboard`, `Null` (sample) |

Adding a provider is **one class + one registration line**. Nothing else changes: the
Settings dropdowns, the registry resolution and the pipeline pick the new provider up
automatically.

## 1. Implement the interface

Create a class implementing the interface of the slot you are filling, in any project that
references `DictateFlow.Core`. The minimal example is the sample
[`NullOutputProvider`](../samples/DictateFlow.Samples.NullOutput/NullOutputProvider.cs):

```csharp
public sealed class NullOutputProvider : IOutputProvider
{
    public const string RegistrationName = "Null";

    public string Name => RegistrationName;

    public Task OutputAsync(string text)
    {
        // deliver the text somewhere (this sample just logs it)
        return Task.CompletedTask;
    }
}
```

Provider guidelines:

- Take dependencies through the constructor; the class is built by the DI container.
- Throw `ProviderException` for user-actionable failures (bad key, unreachable endpoint,
  timeout). Its message is shown to the user; pass `isConfigurationError: true` when the fix
  lives in Settings. Never let raw transport exceptions escape.
- Honor the `CancellationToken` on every awaited call.

### Optional: streaming transcription

A transcription provider can additionally implement `IStreamingTranscriptionProvider` to
transcribe while the user is still speaking:

```csharp
public interface IStreamingTranscriptionProvider
{
    IAsyncEnumerable<TranscriptionUpdate> TranscribeStreamingAsync(
        IAsyncEnumerable<AudioChunk> audio,
        CancellationToken cancellationToken);
}
```

The capability is detected at runtime — no extra registration. When **Enable streaming
transcription** is on in Settings and the active provider implements the interface, each
recording feeds live 16 kHz/16-bit/mono PCM chunks to `TranscribeStreamingAsync` and shows
the partial transcript on the overlay; the update yielded with `IsFinal = true` (or the last
one) becomes the transcript handed to the rest of the pipeline, and the pipeline's own
transcription stage is skipped. Everything downstream (LLM enhancement, history, output) is
unchanged and runs exactly once on the final text.

Streaming is strictly an optimization: if the session throws, times out or yields nothing,
the completed WAV capture is transcribed through the regular `TranscribeAsync` path instead,
so a provider never has to implement its own fallback. Providers that only implement
`ITranscriptionProvider` keep working untouched. Two built-in providers implement the
interface: `AzureSpeech` (real-time recognition through the Azure Speech SDK) and `Mock`
(revealing its canned text word by word, so the streaming flow is demoable without any
cloud service).

## 2. Register it (the one line)

Add one line to `AddDictateFlow` in
[`src/DictateFlow.App/ServiceCollectionExtensions.cs`](../src/DictateFlow.App/ServiceCollectionExtensions.cs) —
the single place in the codebase that lists concrete providers:

```csharp
services.AddOutputProvider<NullOutputProvider>(NullOutputProvider.RegistrationName);
// or: AddTranscriptionProvider<T>(name) / AddLLMProvider<T>(name)
```

The extension records the provider in the `ProviderCatalog` (which feeds the dropdowns and
`IProviderRegistry.GetNames`) and registers it as a keyed service resolved by name. The
implementation type itself is registered as a singleton unless it already has a
registration — providers with special lifetimes (like the Azure typed `HttpClient`s added
via `AddAzureFoundryTranscription()`) keep theirs by registering **before** the
`Add*Provider` line.

The **first registered name of each kind is the fallback**: it is used when the configured
active name is empty or unknown (that is why `Mock` is registered first for speech and LLM —
a broken settings file can never break dictation).

## 3. Select it in settings

The provider now appears in the corresponding Settings dropdown, and in `settings.json` as a
valid `ActiveProviders` value:

```json
{ "ActiveProviders": { "Output": "Null" } }
```

Selection is read on every dictation, so switching applies to the next run — no restart.

## Per-provider configuration

If the provider needs settings, define a config class with defaults and read it through
`IProviderConfigReader` **on every call** (so edits apply live):

```csharp
public sealed class MyProviderConfig
{
    public string Endpoint { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}

// inside the provider:
var config = _configReader.GetConfig<MyProviderConfig>(ProviderKind.Output, RegistrationName);
```

The values live in `settings.json` under `Providers.<Kind>.<Name>`:

```json
{
  "Providers": {
    "Output": {
      "MyProvider": { "Endpoint": "https://…", "TimeoutSeconds": 30 }
    }
  }
}
```

Reads are tolerant: a missing section or property falls back to the config class defaults
(with a warning in the log), so a provider works before it is ever configured.

LLM provider sections may additionally carry `Temperature` and `MaxTokens` — the prompt
resolver reads them from the *active* LLM provider's section as the sampling defaults for
prompt modes without their own temperature.

Exposing the config in the Settings window currently means adding a section to the
Speech/LLM/Output page (see the `AzureFoundry`/`Mock` sections in `SettingsWindow.xaml` and
`SettingsViewModel`); providers without UI are still fully usable by editing
`settings.json`. A generated config UI is a later milestone.

## Proof

[`NullOutputProviderDemonstrationTests`](../tests/DictateFlow.Tests/NullOutputProviderDemonstrationTests.cs)
pins the claim in CI: the sample provider is discovered by the registry (which is exactly
what the dropdown binds to), resolves when selected in settings, and receives a full
dictation from the unmodified pipeline.
