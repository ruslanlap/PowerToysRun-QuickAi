# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

QuickAI is a PowerToys Run plugin that provides AI-powered assistance directly in the Windows PowerToys launcher. Users can type `ai <question>` and get streaming responses from multiple AI providers (Groq, Together, Fireworks, OpenRouter, Cohere, Google, Ollama, and OpenAI Compatible endpoints).

**Key Information:**
- Plugin ID: `420129A62ECA49848C5C7CA229BFD22C`
- Default action keyword: `ai`
- Target: .NET 9.0 for Windows 10.0.22621.0
- Main implementation: Single file `QuickAi/Community.PowerToys.Run.Plugin.QuickAi/Main.cs` (~1320 lines)

## Build and Development Commands

### Building the plugin
```bash
# Build for both x64 and ARM64, create distribution ZIPs
./build-and-zip.sh

# Or use .NET directly for a specific platform
cd QuickAi
dotnet build QuickAi.sln -c Release -p:Platform=x64
dotnet build QuickAi.sln -c Release -p:Platform=ARM64

# Publish for distribution
dotnet publish Community.PowerToys.Run.Plugin.QuickAi/Community.PowerToys.Run.Plugin.QuickAi.csproj \
  -c Release -r win-x64 --self-contained false
```

### Running tests
```bash
cd QuickAi
dotnet test Community.PowerToys.Run.Plugin.QuickAi.UnitTests/Community.PowerToys.Run.Plugin.QuickAi.UnitTests.csproj
```

### Local development
```bash
# Build and manually copy to PowerToys plugin directory
dotnet build -c Release -p:Platform=x64
# Then copy output from bin/x64/Release/net9.0-windows10.0.22621.0/
# to %LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\QuickAi\
```

## Architecture

### Core Plugin Interface Implementation

The `Main` class implements four PowerToys interfaces:
- **IPlugin**: Core plugin functionality (Init, Query)
- **ISettingProvider**: Exposes settings UI via `AdditionalOptions` property
- **IContextMenu**: Right-click context menu (show full response, copy, restart)
- **IDisposable**: Proper cleanup of HTTP resources and streaming sessions

### Multi-Provider Architecture

**Provider Configuration (`ProviderConfigurations` dictionary):**
- Maps provider name → endpoint URL + schema type
- Schema types: `OpenAI`, `Cohere`, `Google` (different JSON payload formats)
- Ollama and OpenAI Compatible providers use OpenAI schema

**API Request Flow:**
1. User types query → `Query()` method called by PowerToys
2. Query validation (check for API key, prompt)
3. `StartQuery()` creates a `StreamingSession`
4. `StreamWithConfigurationAsync()` builds HTTP request based on provider schema
5. Server-Sent Events (SSE) stream parsed line-by-line
6. Delta extraction logic differs by provider (OpenAI vs Cohere vs Google)
7. UI refreshed via `_context.API.ChangeQuery()` callback mechanism

### Streaming Response System

**Key Components:**
- **StreamingSession class**: Manages per-query state (prompt, response builder, cancellation)
- **Batched UI updates**: Refreshes UI every 3 tokens OR 150ms to avoid performance issues
- **Thread safety**: All session access protected by `_sessionGate` lock
- **Cancellation**: When user types new query, previous session cancelled via `CancellationTokenSource`

**Performance optimizations:**
- Static `HttpClient` singleton with HTTP/2 support
- Connection pooling (5 min lifetime, 2 min idle timeout)
- Brotli/Gzip/Deflate compression support
- TLS 1.2/1.3 only
- Configurable timeout (default 8s, range 3-30s)

### API Key Management

**Dual key system:**
- Primary API key: First attempt
- Secondary API key: Automatic fallback on primary failure
- Provider-specific authentication:
  - Google: `x-goog-api-key` header
  - Ollama: No authentication
  - Others: `Authorization: Bearer` header

### Settings Configuration

Settings stored in PowerToys settings JSON (`%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\settings.json`).

**Configurable options:**
- Provider selection (dropdown)
- Primary/Secondary API keys (textbox)
- Model name (textbox)
- Max tokens (16-4096, default 128)
- Temperature (0.0-2.0, default 0.2)
- Request timeout (3-30 seconds, default 8)
- Ollama host URL (for Ollama provider)
- OpenAI Compatible endpoint URL

**Important:** Settings use plain text storage - API keys are NOT encrypted.

## Critical Implementation Details

### Provider Schema Handling

Three distinct schemas must be handled:

1. **OpenAI Schema** (Groq, Together, Fireworks, OpenRouter, Ollama, OpenAI Compatible):
```json
{
  "model": "...",
  "messages": [{"role": "user", "content": "..."}],
  "stream": true,
  "temperature": 0.2,
  "max_tokens": 128
}
```
Delta extraction: `root.choices[0].delta.content`

2. **Cohere Schema**:
```json
{
  "model": "command",
  "message": "...",
  "stream": true,
  "temperature": 0.2,
  "max_tokens": 128
}
```
Delta extraction: `root.event_type == "text-generation"` → `root.text`

3. **Google Schema** (Gemini):
```json
{
  "contents": [{"parts": [{"text": "..."}]}],
  "generationConfig": {
    "temperature": 0.2,
    "maxOutputTokens": 128
  }
}
```
Endpoint includes model: `/v1beta/models/{model}:streamGenerateContent?key={apiKey}&alt=sse`
Delta extraction: `root.candidates[0].content.parts[0].text`

### SSE Stream Parsing

Streaming responses use Server-Sent Events format:
```
data: {"choices":[{"delta":{"content":"Hello"}}]}

data: {"choices":[{"delta":{"content":" world"}}]}

data: [DONE]
```

Parser logic (`StreamWithConfigurationAsync`):
- Read line-by-line from response stream
- Lines starting with `"data: "` contain JSON payloads
- `"data: [DONE]"` signals end of stream
- Empty lines are field separators (ignored)
- Extract delta based on provider schema type
- Build cumulative response in `StringBuilder`

### UI Refresh Mechanism

PowerToys Run doesn't support live UI updates. Workaround:
1. Plugin calls `_context.API.ChangeQuery(rawQuery, true)`
2. This triggers PowerToys to re-call `Query()` method
3. `Query()` returns updated `StreamingSession.BuildResult()` with latest response
4. Flag `_uiRefreshPending` prevents infinite loops

### Context Menu Integration

Right-click on result shows:
- **Show full response (Enter)**: Opens WPF MessageBox with full text
- **Copy response (Ctrl+C)**: Copies to clipboard
- **Restart query (Ctrl+R)**: Re-runs the same prompt

Context data stored in `Result.ContextData` as the full response string.

## File Structure

```
PowerToysRun-QuickAi/
├── QuickAi/
│   ├── Community.PowerToys.Run.Plugin.QuickAi/
│   │   ├── Main.cs                    # Entire plugin logic (1320 lines)
│   │   ├── plugin.json                # Plugin manifest
│   │   ├── *.csproj                   # Project file
│   │   └── Images/
│   │       ├── ai.dark.png           # Dark theme icon
│   │       └── ai.light.png          # Light theme icon
│   ├── Community.PowerToys.Run.Plugin.QuickAi.UnitTests/
│   │   ├── MainTests.cs              # Basic unit tests
│   │   └── *.csproj
│   └── QuickAi.sln                   # Main solution
├── build-and-zip.sh                   # Build script (creates release ZIPs)
├── .github/workflows/
│   └── build-and-release-optimized.yml  # CI/CD workflow
└── README.md
```

**Note:** The `src/` and `Templates.sln` are for a separate PowerToys plugin template project, not part of QuickAI plugin itself.

## Build Output & Packaging

Build outputs go to:
- `QuickAi/Community.PowerToys.Run.Plugin.QuickAi/bin/Release/net9.0-windows10.0.22621.0/{platform}/`

Packaging (`build-and-zip.sh`) excludes PowerToys-provided dependencies:
- `PowerToys.Common.UI.*`
- `PowerToys.ManagedCommon.*`
- `PowerToys.Settings.UI.Lib.*`
- `Wox.Infrastructure.*`
- `Wox.Plugin.*`

Final ZIPs created at repo root: `QuickAi-{version}-{platform}.zip`

## CI/CD Pipeline

GitHub Actions workflow (`.github/workflows/build-and-release-optimized.yml`):
- Triggers on version tags (`v*`) or manual dispatch
- Matrix build: x64 and ARM64 in parallel
- Optimizations:
  - Shallow checkout with sparse-checkout
  - NuGet package caching
  - Parallel builds (`BuildInParallel=true`)
  - Skip debug symbols (`DebugType=none`)
  - Robocopy for fast file copying
  - .NET compression APIs instead of `Compress-Archive`
- Creates GitHub release with checksums

## Version Management

Version controlled in `plugin.json`:
```json
{
  "Version": "1.1.0"
}
```

Build script extracts version via `sed`:
```bash
VERSION="$(sed -n 's/.*"Version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$PLUGIN_JSON")"
```

## Adding a New AI Provider

To add a new provider:

1. Add provider constant in `Main.cs`:
```csharp
private const string ProviderNewProvider = "NewProvider";
```

2. Add to `SupportedProviders` list

3. Add to `ProviderConfigurations` dictionary:
```csharp
[ProviderNewProvider] = new("https://api.example.com/v1/chat", ProviderSchemaType.OpenAI)
```

4. If using non-OpenAI schema:
   - Add new `ProviderSchemaType` enum value
   - Implement payload building in `BuildHttpRequest()`
   - Implement delta extraction in new method (similar to `ExtractOpenAiDelta`)
   - Add case in SSE stream parsing logic

5. Handle any special authentication in `BuildHttpRequest()`

6. Update README.md with provider details

## Common Patterns

### Adding a new setting

1. Add constant key:
```csharp
private const string NewSettingKey = "quickai_new_setting";
```

2. Add field to store value:
```csharp
private string _newSetting = "default";
```

3. Add to `AdditionalOptions` property (see existing examples)

4. Parse in `UpdateSettings()` method

### Error handling

Always set user-friendly errors via:
```csharp
session.SetError("User-friendly error message");
TriggerRefresh(session.RawQuery);
```

### Testing streaming locally

Ollama is best for local testing:
```bash
ollama serve
ollama pull llama3.2
# Configure plugin to use Ollama provider with model "llama3.2"
```

## Known Issues

- API keys stored in plain text in PowerToys settings JSON (issue noted in README)
- "Show full response" modal may show partial text if response was truncated by low max_tokens or timeout (issue #3)
- Plugin currently doesn't support conversation history (stateless queries)
