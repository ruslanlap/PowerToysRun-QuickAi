#nullable enable
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.QuickAI
{
    public sealed class Main : IPlugin, ISettingProvider, IContextMenu, IDisposable
    {
        private const string ProviderGroq = "Groq";
        private const string ProviderTogether = "Together";
        private const string ProviderFireworks = "Fireworks";
        private const string ProviderOpenRouter = "OpenRouter";
        private const string ProviderCohere = "Cohere";
        private const string ProviderGoogle = "Google";

        private const string ProviderOptionKey = "quickai_provider";
        private const string PrimaryKeyOptionKey = "quickai_primary_key";
        private const string SecondaryKeyOptionKey = "quickai_secondary_key";
        private const string ModelOptionKey = "quickai_model";
        private const string MaxTokensOptionKey = "quickai_max_tokens";
        private const string TemperatureOptionKey = "quickai_temperature";

        private const string DefaultModelName = "llama-3.1-8b-instant";
        private const int DefaultMaxTokens = 128;
        private const double DefaultTemperature = 0.2d;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        // Configurable timeout settings
        private const int DefaultTimeoutSeconds = 8;
        private const int MinTimeoutSeconds = 3;
        private const int MaxTimeoutSeconds = 30;

        private static readonly List<string> SupportedProviders = new()
        {
            ProviderGroq,
            ProviderTogether,
            ProviderFireworks,
            ProviderOpenRouter,
            ProviderCohere,
            ProviderGoogle
        };

        private static readonly IReadOnlyDictionary<string, ProviderConfiguration> ProviderConfigurations =
            new Dictionary<string, ProviderConfiguration>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderGroq] = new("https://api.groq.com/openai/v1/chat/completions", ProviderSchemaType.OpenAI),
                [ProviderTogether] = new("https://api.together.xyz/v1/chat/completions", ProviderSchemaType.OpenAI),
                [ProviderFireworks] = new("https://api.fireworks.ai/inference/v1/chat/completions", ProviderSchemaType.OpenAI),
                [ProviderOpenRouter] = new("https://openrouter.ai/api/v1/chat/completions", ProviderSchemaType.OpenAI),
                [ProviderCohere] = new("https://api.cohere.com/v1/chat", ProviderSchemaType.Cohere),
                [ProviderGoogle] = new("https://generativelanguage.googleapis.com/v1beta", ProviderSchemaType.Google)
            };

        private static readonly HttpClient HttpClient = CreateHttpClient();

        private readonly object _sessionGate = new();

        private PluginInitContext? _context;
        private string _iconPath = string.Empty;
        private bool _disposed;

        private string _provider = ProviderGroq;
        private string? _primaryApiKey;
        private string? _secondaryApiKey;
        private string _modelName = DefaultModelName;
        private int _maxTokens = DefaultMaxTokens;
        private double _temperature = DefaultTemperature;
        private int _timeoutSeconds = DefaultTimeoutSeconds;

        private StreamingSession? _session;
        private bool _uiRefreshPending;
        private string _pendingPrompt = string.Empty;

        /// <summary>
        /// ID of the plugin.
        /// </summary>
        public static string PluginID => "420129A62ECA49848C5C7CA229BFD22C";

        /// <summary>
        /// Name of the plugin.
        /// </summary>
        public string Name => "QuickAI";

        /// <summary>
        /// Description of the plugin.
        /// </summary>
        public string Description => "Ask questions with streaming AI responses from multiple providers.";

        /// <summary>
        /// Initialize the plugin with the given <see cref="PluginInitContext"/>.
        /// </summary>
        /// <param name="context">The <see cref="PluginInitContext"/> for this plugin.</param>
        public void Init(PluginInitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _context.API.ThemeChanged += OnThemeChanged;
            UpdateIconPath(_context.API.GetCurrentTheme());
        }

        /// <summary>
        /// Return a filtered list, based on the given query.
        /// </summary>
        /// <param name="query">The query to filter the list.</param>
        /// <returns>A filtered list, can be empty when nothing was found.</returns>
        public List<Result> Query(Query query)
        {
            if (query is null)
            {
                return new List<Result>();
            }

            var rawQuery = query.RawQuery ?? string.Empty;
            var search = (query.Search ?? string.Empty).Trim();

            lock (_sessionGate)
            {
                if (_uiRefreshPending && _session is not null && string.Equals(_session.RawQuery, rawQuery, StringComparison.Ordinal))
                {
                    _uiRefreshPending = false;
                    return new List<Result> { _session.BuildResult(_iconPath, _provider, _modelName) };
                }

                if (_session is not null && !string.Equals(_session.RawQuery, rawQuery, StringComparison.Ordinal))
                {
                    _session.Cancel();
                    _session = null;
                }

                if (string.IsNullOrEmpty(search))
                {
                    _pendingPrompt = string.Empty;
                    return new List<Result>
                    {
                        BuildInfoResult(
                            "Type your question after \"ai\"",
                            "Example: ai explain recursion in simple terms.")
                    };
                }

                if (!HasConfiguredApiKey())
                {
                    return new List<Result>
                    {
                        BuildInfoResult(
                            "API key required",
                            "Open PowerToys Settings → PowerToys Run → QuickAI to configure keys.")
                    };
                }

                // If session is active, show streaming result
                if (_session is not null)
                {
                    return new List<Result> { _session.BuildResult(_iconPath, _provider, _modelName) };
                }

                // Otherwise, show prompt ready to submit
                _pendingPrompt = search;
                var displayTitle = search.Length > 100 ? search.Substring(0, 97) + "..." : search;
                return new List<Result>
                {
                    new Result
                    {
                        Title = displayTitle,
                        SubTitle = $"Press Enter to ask {_provider} · {_modelName}",
                        IcoPath = _iconPath,
                        Score = 100,
                        Action = _ =>
                        {
                            StartQuery(rawQuery, search);
                            return false;
                        }
                    }
                };
            }
        }

        /// <summary>
        /// Return a list context menu entries for a given <see cref="Result"/> (shown at the right side of the result).
        /// </summary>
        /// <param name="selectedResult">The <see cref="Result"/> for the list with context menu entries.</param>
        /// <returns>A list context menu entries.</returns>
        public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
        {
            if (selectedResult?.ContextData is string responseText && !string.IsNullOrWhiteSpace(responseText))
            {
                return new List<ContextMenuResult>
                {
                    new ContextMenuResult
                    {
                        PluginName = Name,
                        Title = "Show full response (Enter)",
                        FontFamily = "Segoe MDL2 Assets",
                        Glyph = "\xE8A7", // View icon
                        AcceleratorKey = Key.Enter,
                        AcceleratorModifiers = ModifierKeys.None,
                        Action = _ =>
                        {
                            try
                            {
                                ShowInfo("QuickAI Response", responseText);
                                return true;
                            }
                            catch
                            {
                                return false;
                            }
                        },
                    },
                    new ContextMenuResult
                    {
                        PluginName = Name,
                        Title = "Copy response (Ctrl+C)",
                        FontFamily = "Segoe MDL2 Assets",
                        Glyph = "\xE8C8", // Copy icon
                        AcceleratorKey = Key.C,
                        AcceleratorModifiers = ModifierKeys.Control,
                        Action = _ =>
                        {
                            try
                            {
                                Clipboard.SetText(responseText);
                                ShowNotification("Response copied to clipboard.");
                                return true;
                            }
                            catch
                            {
                                ShowInfo("QuickAI", "Unable to copy response.");
                                return false;
                            }
                        },
                    },
                    new ContextMenuResult
                    {
                        PluginName = Name,
                        Title = "Restart query (Ctrl+R)",
                        FontFamily = "Segoe MDL2 Assets",
                        Glyph = "\xE72C", // Refresh icon
                        AcceleratorKey = Key.R,
                        AcceleratorModifiers = ModifierKeys.Control,
                        Action = _ =>
                        {
                            lock (_sessionGate)
                            {
                                _session?.Restart();
                            }
                            return true;
                        },
                    }
                };
            }

            return new List<ContextMenuResult>();
        }

        public IEnumerable<PluginAdditionalOption> AdditionalOptions
        {
            get
            {
                lock (_sessionGate)
                {
                    var providerIndex = Math.Max(0, SupportedProviders.IndexOf(_provider));
                    return new List<PluginAdditionalOption>
                    {
                        new()
                        {
                            Key = ProviderOptionKey,
                            DisplayLabel = "API Provider",
                            DisplayDescription = "Select the AI provider used for requests.",
                            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Combobox,
                            ComboBoxItems = SupportedProviders
                                .Select((name, index) => new KeyValuePair<string, string>(name, index.ToString(CultureInfo.InvariantCulture)))
                                .ToList(),
                            ComboBoxValue = providerIndex
                        },
                        new()
                        {
                            Key = PrimaryKeyOptionKey,
                            DisplayLabel = "Primary API Key",
                            DisplayDescription = "Main provider key (stored as plain text in PowerToys settings).",
                            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                            TextValue = _primaryApiKey ?? string.Empty
                        },
                        new()
                        {
                            Key = SecondaryKeyOptionKey,
                            DisplayLabel = "Secondary API Key",
                            DisplayDescription = "Optional fallback key used when the primary key fails.",
                            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                            TextValue = _secondaryApiKey ?? string.Empty
                        },
                        new()
                        {
                            Key = ModelOptionKey,
                            DisplayLabel = "Model Name",
                            DisplayDescription = "Model identifier sent to the provider.",
                            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                            TextValue = _modelName
                        },
                        new()
                        {
                            Key = MaxTokensOptionKey,
                            DisplayLabel = "Max Tokens",
                            DisplayDescription = "Limits response length (lower values return faster).",
                            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Numberbox,
                            NumberValue = _maxTokens,
                            NumberBoxMin = 16,
                            NumberBoxMax = 4096,
                            NumberBoxSmallChange = 16,
                            NumberBoxLargeChange = 128
                        },
                        new()
                        {
                            Key = TemperatureOptionKey,
                            DisplayLabel = "Temperature",
                            DisplayDescription = "Controls response creativity (0.0 = focused, 2.0 = creative).",
                            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Numberbox,
                            NumberValue = _temperature,
                            NumberBoxMin = 0.0,
                            NumberBoxMax = 2.0,
                            NumberBoxSmallChange = 0.1,
                            NumberBoxLargeChange = 0.5
                        },
                        new()
                        {
                            Key = "quickai_timeout",
                            DisplayLabel = "Request Timeout (seconds)",
                            DisplayDescription = "Maximum time to wait for response. Lower = faster failure detection.",
                            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Numberbox,
                            NumberValue = _timeoutSeconds,
                            NumberBoxMin = MinTimeoutSeconds,
                            NumberBoxMax = MaxTimeoutSeconds,
                            NumberBoxSmallChange = 1,
                            NumberBoxLargeChange = 5
                        }
                    };
                }
            }
        }

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            lock (_sessionGate)
            {
                if (settings?.AdditionalOptions is null)
                {
                    ResetToDefaults();
                }
                else
                {
                    ApplySettings(settings.AdditionalOptions);
                }

                if (_session is not null)
                {
                    _session.Cancel();
                    _session = null;
                }
            }
        }

        public Control? CreateSettingPanel() => null;

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Wrapper method for <see cref="Dispose()"/> that dispose additional objects and events form the plugin itself.
        /// </summary>
        /// <param name="disposing">Indicate that the plugin is disposed.</param>
        private void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
            {
                return;
            }

            if (_context?.API != null)
            {
                _context.API.ThemeChanged -= OnThemeChanged;
            }

            lock (_sessionGate)
            {
                _session?.Dispose();
                _session = null;
            }

            _disposed = true;
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                // HTTP/2 optimization
                EnableMultipleHttp2Connections = true,

                // Connection pooling
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 10,

                // Compression (was already present)
                AutomaticDecompression = DecompressionMethods.Brotli |
                                         DecompressionMethods.Deflate |
                                         DecompressionMethods.GZip,

                // Security optimization
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                          System.Security.Authentication.SslProtocols.Tls13
                },

                // Performance tweak
                UseCookies = false  // Not needed for API calls
            };

            var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan,

                // HTTP/2 by default
                DefaultRequestVersion = new Version(2, 0),
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
            };

            return client;
        }

        private void BeginStreaming(StreamingSession session)
        {
            var configuration = CaptureConfiguration();

            _ = Task.Run(async () =>
            {
                await StreamWithConfigurationAsync(session, configuration).ConfigureAwait(false);
            });
        }

        private async Task StreamWithConfigurationAsync(StreamingSession session, ConfigurationSnapshot configuration)
        {
            if (!ProviderConfigurations.TryGetValue(configuration.Provider, out var providerConfiguration))
            {
                session.SetError($"Unsupported provider: {configuration.Provider}");
                TriggerRefresh(session.RawQuery);
                return;
            }

            session.SetStatus($"Requesting from {configuration.Provider}...");
            TriggerRefresh(session.RawQuery);

            var prompt = session.SnapshotPrompt();
            var apiKeys = EnumerateApiKeys(configuration.PrimaryApiKey, configuration.SecondaryApiKey).ToList();

            if (apiKeys.Count == 0)
            {
                session.SetError("Configure an API key to use this provider.");
                TriggerRefresh(session.RawQuery);
                return;
            }

            foreach (var candidate in apiKeys)
            {
                if (session.Token.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    await foreach (var chunk in ExecuteStreamingRequestAsync(
                        providerConfiguration,
                        configuration,
                        prompt,
                        candidate.Key,
                        session.Token).ConfigureAwait(false))
                    {
                        session.Append(chunk);
                        TriggerRefresh(session.RawQuery);
                    }

                    session.MarkCompleted();
                    TriggerRefresh(session.RawQuery);
                    return;
                }
                catch (AuthenticationException authEx)
                {
                    if (candidate.Kind == ApiKeyKind.Primary && apiKeys.Any(k => k.Kind == ApiKeyKind.Secondary))
                    {
                        session.SetStatus("Primary key failed. Trying secondary key...");
                        TriggerRefresh(session.RawQuery);
                        continue;
                    }

                    session.SetError(authEx.Message);
                    TriggerRefresh(session.RawQuery);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    session.SetError($"Request failed: {ex.Message}");
                    TriggerRefresh(session.RawQuery);
                }

                if (!session.HasCompleted)
                {
                    return;
                }
            }

            if (!session.HasCompleted)
            {
                session.SetError("All configured API keys failed. Verify credentials and provider status.");
                TriggerRefresh(session.RawQuery);
            }
        }

        private async IAsyncEnumerable<string> ExecuteStreamingRequestAsync(
            ProviderConfiguration providerConfiguration,
            ConfigurationSnapshot configuration,
            string prompt,
            string apiKey,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var request = BuildHttpRequest(providerConfiguration, configuration, prompt, apiKey);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));

            using var response = await HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new AuthenticationException("Authentication failed for the configured API key.");
            }

            response.EnsureSuccessStatusCode();

            await foreach (var token in ParseStreamAsync(response, providerConfiguration, timeoutCts, configuration.TimeoutSeconds, cancellationToken).ConfigureAwait(false))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
                yield return token;
            }
        }

        private async IAsyncEnumerable<string> ParseStreamAsync(
            HttpResponseMessage response,
            ProviderConfiguration configuration,
            CancellationTokenSource timeoutSource,
            int timeoutSeconds,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var payload = line[5..].Trim();
                if (string.IsNullOrEmpty(payload))
                {
                    continue;
                }

                if (payload.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    yield break;
                }

                string? token = null;

                try
                {
                    using var document = JsonDocument.Parse(payload);
                    token = configuration.SchemaType switch
                    {
                        ProviderSchemaType.OpenAI => ExtractOpenAiDelta(document.RootElement),
                        ProviderSchemaType.Cohere => ExtractCohereDelta(document.RootElement),
                        ProviderSchemaType.Google => ExtractGoogleDelta(document.RootElement),
                        _ => null
                    };
                }
                catch (JsonException)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(token))
                {
                    timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                    yield return token;
                }
            }
        }

        private HttpRequestMessage BuildHttpRequest(
            ProviderConfiguration providerConfiguration,
            ConfigurationSnapshot configuration,
            string prompt,
            string apiKey)
        {
            string endpoint = providerConfiguration.Endpoint;
            string json;

            // Build request based on provider schema type
            switch (providerConfiguration.SchemaType)
            {
                case ProviderSchemaType.Google:
                    // Google uses model in URL and API key as query parameter
                    endpoint = $"{providerConfiguration.Endpoint}/models/{configuration.Model}:streamGenerateContent?key={apiKey}";
                    var googlePayload = new
                    {
                        contents = new[]
                        {
                            new
                            {
                                parts = new[]
                                {
                                    new { text = prompt }
                                }
                            }
                        },
                        generationConfig = new
                        {
                            temperature = configuration.Temperature,
                            maxOutputTokens = configuration.MaxTokens
                        }
                    };
                    json = JsonSerializer.Serialize(googlePayload, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    });
                    break;

                case ProviderSchemaType.Cohere:
                    var coherePayload = new
                    {
                        model = configuration.Model,
                        message = prompt,
                        stream = true,
                        temperature = configuration.Temperature,
                        max_tokens = configuration.MaxTokens
                    };
                    json = JsonSerializer.Serialize(coherePayload, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    });
                    break;

                case ProviderSchemaType.OpenAI:
                default:
                    var openAiPayload = new
                    {
                        model = configuration.Model,
                        messages = new[]
                        {
                            new { role = "user", content = prompt }
                        },
                        stream = true,
                        temperature = configuration.Temperature,
                        max_tokens = configuration.MaxTokens
                    };
                    json = JsonSerializer.Serialize(openAiPayload, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    });
                    break;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

            // Google uses API key in URL, others use Authorization header
            if (providerConfiguration.SchemaType != ProviderSchemaType.Google)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("PowerToys-QuickAI/1.0");
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

            if (string.Equals(configuration.Provider, ProviderOpenRouter, StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/microsoft/PowerToys");
                request.Headers.TryAddWithoutValidation("X-Title", "PowerToys Run QuickAI");
            }

            request.Content = new StringContent(json, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
            return request;
        }

        private static string? ExtractOpenAiDelta(JsonElement root)
        {
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
            {
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString();
                }
            }

            if (choice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var messageContent) && messageContent.ValueKind == JsonValueKind.String)
            {
                return messageContent.GetString();
            }

            return null;
        }

        private static string? ExtractCohereDelta(JsonElement root)
        {
            if (root.TryGetProperty("event_type", out var eventType) && eventType.ValueKind == JsonValueKind.String)
            {
                var type = eventType.GetString();
                if (string.Equals(type, "text-generation", StringComparison.OrdinalIgnoreCase) &&
                    root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }

                if (string.Equals(type, "stream-end", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            if (root.TryGetProperty("text", out var shorthandText) && shorthandText.ValueKind == JsonValueKind.String)
            {
                return shorthandText.GetString();
            }

            return null;
        }

        private static string? ExtractGoogleDelta(JsonElement root)
        {
            // Google AI Studio streaming format: {"candidates":[{"content":{"parts":[{"text":"chunk"}]}}]}
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                return null;
            }

            var candidate = candidates[0];
            if (!candidate.TryGetProperty("content", out var content))
            {
                return null;
            }

            if (!content.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
            {
                return null;
            }

            var part = parts[0];
            if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }

            return null;
        }

        private void UpdateIconPath(Theme theme) => _iconPath = theme == Theme.Light || theme == Theme.HighContrastWhite 
            ? Path.Combine(_context?.CurrentPluginMetadata?.PluginDirectory ?? string.Empty, "Images", "ai.light.png")
            : Path.Combine(_context?.CurrentPluginMetadata?.PluginDirectory ?? string.Empty, "Images", "ai.dark.png");

        private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateIconPath(newTheme);

        private void StartQuery(string rawQuery, string search)
        {
            lock (_sessionGate)
            {
                if (_session is null)
                {
                    _session = new StreamingSession(this, rawQuery, search);
                    _session.Start();
                }
            }

            TriggerRefresh(rawQuery);
        }

        private void TriggerRefresh(string rawQuery)
        {
            var api = _context?.API;
            if (api is null)
            {
                return;
            }

            lock (_sessionGate)
            {
                if (_session is null || !string.Equals(_session.RawQuery, rawQuery, StringComparison.Ordinal))
                {
                    return;
                }

                _uiRefreshPending = true;
            }

            api.ChangeQuery(rawQuery, true);
        }

        private bool HasConfiguredApiKey()
        {
            lock (_sessionGate)
            {
                return !string.IsNullOrWhiteSpace(_primaryApiKey) || !string.IsNullOrWhiteSpace(_secondaryApiKey);
            }
        }

        private void ResetToDefaults()
        {
            _provider = ProviderGroq;
            _primaryApiKey = null;
            _secondaryApiKey = null;
            _modelName = DefaultModelName;
            _maxTokens = DefaultMaxTokens;
            _temperature = DefaultTemperature;
            _timeoutSeconds = DefaultTimeoutSeconds;
        }

        private void ApplySettings(IEnumerable<PluginAdditionalOption> options)
        {
            foreach (var option in options)
            {
                if (option is null)
                {
                    continue;
                }

                switch (option.Key)
                {
                    case ProviderOptionKey:
                        var index = option.ComboBoxValue;
                        if (index >= 0 && index < SupportedProviders.Count)
                        {
                            _provider = SupportedProviders[index];
                        }
                        break;
                    case PrimaryKeyOptionKey:
                        _primaryApiKey = string.IsNullOrWhiteSpace(option.TextValue) ? null : option.TextValue.Trim();
                        break;
                    case SecondaryKeyOptionKey:
                        _secondaryApiKey = string.IsNullOrWhiteSpace(option.TextValue) ? null : option.TextValue.Trim();
                        break;
                    case ModelOptionKey:
                        _modelName = string.IsNullOrWhiteSpace(option.TextValue) ? DefaultModelName : option.TextValue.Trim();
                        break;
                    case MaxTokensOptionKey:
                        _maxTokens = (int)Math.Clamp(option.NumberValue <= 0 ? DefaultMaxTokens : option.NumberValue, 16, 4096);
                        break;
                    case TemperatureOptionKey:
                        _temperature = Math.Clamp(option.NumberValue, 0.0, 2.0);
                        break;
                    case "quickai_timeout":
                        _timeoutSeconds = (int)Math.Clamp(
                            option.NumberValue,
                            MinTimeoutSeconds,
                            MaxTimeoutSeconds
                        );
                        break;
                }
            }
        }

        private ConfigurationSnapshot CaptureConfiguration()
        {
            lock (_sessionGate)
            {
                return new ConfigurationSnapshot(
                    _provider,
                    _primaryApiKey,
                    _secondaryApiKey,
                    _modelName,
                    _maxTokens,
                    _temperature,
                    _timeoutSeconds);
            }
        }

        private Result BuildInfoResult(string title, string subtitle, Func<bool>? action = null)
        {
            return new Result
            {
                Title = title,
                SubTitle = subtitle,
                IcoPath = _iconPath,
                Action = action is null
                    ? static _ => false
                    : new Func<ActionContext, bool>(_ => action())
            };
        }

        private IEnumerable<ApiKeyCandidate> EnumerateApiKeys(string? primary, string? secondary)
        {
            if (!string.IsNullOrWhiteSpace(primary))
            {
                yield return new ApiKeyCandidate(primary, ApiKeyKind.Primary);
            }

            if (!string.IsNullOrWhiteSpace(secondary))
            {
                if (!string.Equals(primary, secondary, StringComparison.Ordinal))
                {
                    yield return new ApiKeyCandidate(secondary, ApiKeyKind.Secondary);
                }
            }
        }

        private void ShowNotification(string message)
        {
            _context?.API?.ShowNotification(message);
        }

        private void ShowInfo(string title, string subtitle)
        {
            _context?.API?.ShowMsg(title, subtitle);
        }

        private enum ProviderSchemaType
        {
            OpenAI,
            Cohere,
            Google
        }

        private sealed record ProviderConfiguration(string Endpoint, ProviderSchemaType SchemaType);

        private sealed record ConfigurationSnapshot(
            string Provider,
            string? PrimaryApiKey,
            string? SecondaryApiKey,
            string Model,
            int MaxTokens,
            double Temperature,
            int TimeoutSeconds);

        private readonly record struct ApiKeyCandidate(string Key, ApiKeyKind Kind);

        private enum ApiKeyKind
        {
            Primary,
            Secondary
        }

        private sealed class StreamingSession : IDisposable
        {
            private readonly Main _owner;
            private readonly object _sync = new();
            private readonly StringBuilder _buffer = new();
            private CancellationTokenSource _cts = new();
            private string _prompt;
            private string? _status;
            private bool _hasError;
            private bool _completed;

            // UI Batching optimization
            private int _chunksSinceLastRefresh = 0;
            private const int ChunksPerRefresh = 3;  // Update UI every 3 chunks
            private DateTime _lastRefreshTime = DateTime.UtcNow;
            private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMilliseconds(150);

            public StreamingSession(Main owner, string rawQuery, string prompt)
            {
                _owner = owner;
                RawQuery = rawQuery;
                _prompt = prompt;
            }

            public string RawQuery { get; }

            public CancellationToken Token
            {
                get
                {
                    lock (_sync)
                    {
                        return _cts.Token;
                    }
                }
            }

            public bool HasCompleted
            {
                get
                {
                    lock (_sync)
                    {
                        return _completed;
                    }
                }
            }

            public void Start()
            {
                _owner.BeginStreaming(this);
            }

            public void UpdatePrompt(string prompt)
            {
                var shouldRestart = false;

                lock (_sync)
                {
                    if (!string.Equals(_prompt, prompt, StringComparison.Ordinal))
                    {
                        _prompt = prompt;
                        shouldRestart = true;
                    }
                }

                if (shouldRestart)
                {
                    Restart();
                }
            }

            public void Restart()
            {
                lock (_sync)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = new CancellationTokenSource();
                    _buffer.Clear();
                    _status = null;
                    _hasError = false;
                    _completed = false;

                    // Reset batching counters
                    _chunksSinceLastRefresh = 0;
                    _lastRefreshTime = DateTime.UtcNow;
                }

                _owner.BeginStreaming(this);
            }

            public void Cancel()
            {
                lock (_sync)
                {
                    _cts.Cancel();
                }
            }

            public void Append(string text)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                bool shouldRefresh = false;

                lock (_sync)
                {
                    _buffer.Append(text);
                    _status = null;
                    _chunksSinceLastRefresh++;

                    var timeSinceRefresh = DateTime.UtcNow - _lastRefreshTime;

                    // Update UI if:
                    // 1. Accumulated enough chunks (every 3 tokens) OR
                    // 2. Enough time has passed (150ms for smoothness)
                    if (_chunksSinceLastRefresh >= ChunksPerRefresh ||
                        timeSinceRefresh >= MinRefreshInterval)
                    {
                        shouldRefresh = true;
                        _chunksSinceLastRefresh = 0;
                        _lastRefreshTime = DateTime.UtcNow;
                    }
                }

                // Call refresh OUTSIDE of lock
                if (shouldRefresh)
                {
                    _owner.TriggerRefresh(RawQuery);
                }
            }

            public void MarkCompleted()
            {
                lock (_sync)
                {
                    _completed = true;
                }

                // Always refresh UI when completed
                _owner.TriggerRefresh(RawQuery);
            }

            public void SetStatus(string message)
            {
                lock (_sync)
                {
                    _status = message;
                    _hasError = false;
                }
            }

            public void SetError(string message)
            {
                lock (_sync)
                {
                    _status = message;
                    _hasError = true;
                }
            }

            public string SnapshotPrompt()
            {
                lock (_sync)
                {
                    return _prompt;
                }
            }

            public string SnapshotResponse()
            {
                lock (_sync)
                {
                    return _buffer.ToString();
                }
            }

            public Result BuildResult(string iconPath, string provider, string model)
            {
                lock (_sync)
                {
                    var responseText = _buffer.ToString();
                    var title = string.Empty;
                    var subtitle = string.Empty;

                    if (_buffer.Length > 0)
                    {
                        // Split response into lines for better display
                        var lines = responseText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        if (lines.Length > 0)
                        {
                            // Show first line as title
                            title = lines[0].Length > 100 ? lines[0].Substring(0, 97) + "..." : lines[0];
                            
                            // Show second line or status as subtitle
                            if (lines.Length > 1)
                            {
                                var secondLine = lines[1].Length > 80 ? lines[1].Substring(0, 77) + "..." : lines[1];
                                subtitle = _completed 
                                    ? $"{secondLine} | {provider} · {model}"
                                    : $"{secondLine} | Streaming...";
                            }
                            else
                            {
                                subtitle = _completed
                                    ? $"{provider} · {model}"
                                    : "Streaming...";
                            }
                        }
                        else
                        {
                            title = responseText.Length > 100 ? responseText.Substring(0, 97) + "..." : responseText;
                            subtitle = _completed
                                ? $"{provider} · {model}"
                                : "Streaming...";
                        }
                    }
                    else
                    {
                        title = _status ?? "Streaming response...";
                        subtitle = _hasError
                            ? "Request failed."
                            : string.Format(CultureInfo.InvariantCulture, "{0} · {1}", provider, model);
                    }

                    return new Result
                    {
                        Title = title,
                        SubTitle = subtitle,
                        IcoPath = iconPath,
                        Score = 100,
                        Action = action => CopyToClipboard(),
                        ContextData = responseText
                    };
                }
            }

            public void Dispose()
            {
                lock (_sync)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                }
            }

            private bool CopyToClipboard()
            {
                string text;

                lock (_sync)
                {
                    text = _buffer.ToString();
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                try
                {
                    Clipboard.SetText(text);
                    _owner.ShowNotification("QuickAI response copied to clipboard.");
                    return true;
                }
                catch (Exception)
                {
                    _owner.ShowInfo("QuickAI", "Unable to copy response to clipboard.");
                    return false;
                }
            }
        }
    }
}