# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**QuickAI** is a PowerToys Run plugin that provides streaming AI responses from multiple providers (Groq, Together, Fireworks, OpenRouter, Cohere) directly within PowerToys Run launcher.

- **Plugin ID**: `420129A62ECA49848C5C7CA229BFD22C`
- **Default Action Keyword**: `ai`
- **Target Platform**: Windows 10/11 (x64, ARM64)
- **Framework**: .NET 9.0 (net9.0-windows10.0.22621.0)
- **UI**: WPF with theme-aware design

## Building and Testing

### Build for Distribution
```bash
# Build both x64 and ARM64, create distributable ZIPs at repo root
./build-and-zip.sh
```

This script:
- Builds for both platforms using `dotnet publish`
- Excludes PowerToys-provided dependencies (Wox.*, PowerToys.*.dll)
- Creates versioned ZIPs: `QuickAi-{version}-{platform}.zip`
- Generates SHA256 checksums

### Manual Build Commands
```bash
# Build main plugin solution
cd QuickAi
dotnet build QuickAi.sln -c Release -p:Platform=x64

# Build for specific platform
dotnet publish Community.PowerToys.Run.Plugin.QuickAi/Community.PowerToys.Run.Plugin.QuickAi.csproj \
  -c Release -r win-x64 --self-contained false

# Run unit tests
dotnet test QuickAi.sln -c Release -p:Platform=x64
```

### Installation for Local Testing
Extract build output to:
```
%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\QuickAi
```

Then restart PowerToys completely (exit from tray, restart from Start Menu).

### Linting
```bash
# Install PowerToys Run linting tool
dotnet tool install -g Community.PowerToys.Run.Plugin.Lint

# Run linter (note: ptrun-lint.sh targets different repo, use manually)
ptrun-lint https://github.com/ruslanlap/PowerToysRun-QuickAi
```

## Architecture

### Core Implementation Pattern

The plugin follows PowerToys Run plugin architecture implementing multiple interfaces:

- **`IPlugin`**: Core plugin lifecycle (`Init`, `Query`)
- **`ISettingProvider`**: Exposes `AdditionalOptions` for settings UI
- **`IContextMenu`**: Provides right-click actions on results
- **`IDisposable`**: Resource cleanup

### Key Architectural Concepts

**1. Streaming Session Management**
- Each query creates a `StreamingSession` object that manages state
- Session stores: raw query text, prompt, accumulated response, cancellation token
- Thread-safe lock (`_sessionGate`) protects session creation/access
- UI updates triggered via `_context.API.ChangeQuery()` to force PowerToys Run refresh

**2. Dual API Key Fallback**
- Primary and secondary API keys configurable per provider
- On `AuthenticationException`, automatically retries with secondary key
- `EnumerateApiKeys()` yields keys in order (primary first)

**3. Provider Abstraction**
- `ProviderConfiguration` records contain endpoint URL and schema type
- OpenAI-compatible providers (Groq, Together, Fireworks, OpenRouter) use same request/response format
- Cohere uses different schema, handled separately in `BuildRequestBody()` and parsing

**4. Server-Sent Events (SSE) Streaming**
- HTTP responses streamed line-by-line via `StreamReader`
- Lines starting with `data:` contain JSON chunks
- `[DONE]` message signals completion
- Delta content extracted and appended to response builder

### Important Classes and Methods

**Main.cs (1088 lines)**:
- `Query()`: Entry point for all user input, manages session lifecycle
- `StartQuery()`: Creates new `StreamingSession` and initiates background streaming
- `BeginStreaming()`: Captures configuration snapshot and starts `Task.Run`
- `StreamWithConfigurationAsync()`: Main streaming loop with retry logic
- `ExecuteStreamingRequestAsync()`: HTTP request/response handling with SSE parsing
- `LoadContextMenus()`: Defines right-click actions (copy, restart, show full)

**Settings Configuration Keys** (in `AdditionalOptions`):
- `quickai_provider`: Selected AI provider (dropdown)
- `quickai_primary_key`: Primary API key (password field)
- `quickai_secondary_key`: Secondary API key (password field)
- `quickai_model`: Model name (text field, default: `llama-3.1-8b-instant`)
- `quickai_max_tokens`: Max response tokens (int, default: 128)
- `quickai_temperature`: Sampling temperature (double, default: 0.2)

### HTTP Client Configuration

Static singleton `HttpClient` with:
- 10-second timeout (`RequestTimeout`)
- TLS 1.2+ only (`SecurityProtocolType.Tls12 | Tls13`)
- Infinite `Timeout` property (handled manually per request)

### Threading Model

- PowerToys Run calls `Query()` on UI thread
- Streaming runs in background via `Task.Run()`
- UI refresh triggered by calling `_context.API.ChangeQuery(_session.RawQuery, true)`
- All session state access protected by `lock (_sessionGate)`

## Version Management

Version is defined in `plugin.json`:
```json
{
  "Version": "1.0.0"
}
```

The `build-and-zip.sh` script extracts this version for ZIP naming.

## CI/CD Pipeline

`.github/workflows/build-and-release-optimized.yml`:
- Triggers on git tags matching `v*` (e.g., `v1.0.0`)
- Matrix build: x64 and ARM64 in parallel
- Uses `robocopy` for fast file copying, `.NET compression` for ZIPs
- Removes PowerToys-provided dependencies before packaging
- Creates GitHub release with both platform ZIPs and checksums

**To create a release**:
1. Update version in `QuickAi/Community.PowerToys.Run.Plugin.QuickAi/plugin.json`
2. Commit changes
3. Create and push git tag: `git tag v1.0.1 && git push origin v1.0.1`
4. GitHub Actions automatically builds and publishes release

## Testing

Unit tests located in:
```
QuickAi/Community.PowerToys.Run.Plugin.QuickAi.UnitTests/
```

Test framework: MSTest (v3.6.3)

Run tests:
```bash
cd QuickAi
dotnet test Community.PowerToys.Run.Plugin.QuickAi.UnitTests/Community.PowerToys.Run.Plugin.QuickAi.UnitTests.csproj
```

## Dependencies

**Plugin Dependencies** (excluded from distribution):
- `Community.PowerToys.Run.Plugin.Dependencies` (v0.93.0)
  - Provides: Wox.Plugin, Wox.Infrastructure, PowerToys.ManagedCommon, PowerToys.Settings.UI.Lib

**Direct Dependencies** (included in distribution):
- `System.Text.Json` (v9.0.10)

## Security Notes

**API Key Storage**: Keys are stored in plain text in PowerToys settings JSON:
```
%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\settings.json
```

Recommend using limited-permission API keys. Future enhancement planned: Windows DPAPI encryption.

## Model Name Configuration

**Critical**: Model names must exactly match provider's API specification:

- **Groq**: `llama-3.1-8b-instant`, `mixtral-8x7b-32768`
- **Together**: `meta-llama/Llama-3-8b-chat-hf` (includes namespace)
- **Fireworks**: `accounts/fireworks/models/llama-v3p1-8b-instruct` (full path required)
- **OpenRouter**: `meta-llama/llama-3.1-8b-instruct`
- **Cohere**: `command`, `command-light`, `command-nightly`

Incorrect model names result in 400/404 errors from provider APIs.

## Common Development Scenarios

### Adding a New AI Provider

1. Add provider name constant in `Main.cs` (e.g., `ProviderNewAI`)
2. Add to `SupportedProviders` list
3. Add endpoint and schema type to `ProviderConfigurations` dictionary
4. If using non-OpenAI schema, update `BuildRequestBody()` and parsing logic in `ExecuteStreamingRequestAsync()`
5. Update `AdditionalOptions` dropdown choices
6. Update README.md with provider details

### Modifying Streaming Behavior

Core streaming logic in `ExecuteStreamingRequestAsync()`:
- SSE parsing: reads `data:` lines
- JSON deserialization: extracts `choices[0].delta.content`
- Completion detection: `data: [DONE]` or `finish_reason` present

### Changing Result Display

Result building in `StreamingSession.BuildResult()`:
- `Title`: Shows response text (truncated to 100 chars)
- `SubTitle`: Shows status/model/provider info
- `ContextData`: Stores full response text for context menu
- `Action`: Copies response to clipboard on Enter

### Debug Installation Issues

Check PowerToys logs:
```
%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Logs\
```

Verify plugin directory structure:
```
QuickAi/
├── Community.PowerToys.Run.Plugin.QuickAi.dll
├── plugin.json
├── System.Text.Json.dll
└── Images/
    ├── ai.dark.png
    └── ai.light.png
```

Ensure excluded dependencies are NOT present (PowerToys provides them).
