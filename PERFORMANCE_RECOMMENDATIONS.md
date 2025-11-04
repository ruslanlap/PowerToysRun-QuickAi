# 🚀 Рекомендації з покращення продуктивності та функціоналу QuickAI

## 📊 Поточний стан та виявлені проблеми

### Аналіз Main.cs (1087 рядків)

## 🔴 Критичні проблеми продуктивності

### 1. **Надмірні виклики UI refresh**
**Проблема**: Кожен chunk викликає `TriggerRefresh()` → `ChangeQuery()` → `Query()`
```csharp
// Main.cs:453-454
session.Append(chunk);
TriggerRefresh(session.RawQuery);  // ⚠️ Викликається на КОЖЕН token!
```

**Вплив**:
- PowerToys Run перемальовує UI на кожен token (може бути 100+ разів за запит)
- CPU spike через постійні UI updates
- Уповільнює відображення відповіді

**Рішення - Batching UI Updates**:
```csharp
private sealed class StreamingSession : IDisposable
{
    private int _chunksSinceLastRefresh = 0;
    private const int ChunksPerRefresh = 5; // Оновлювати UI кожні 5 chunks
    private DateTime _lastRefreshTime = DateTime.UtcNow;
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMilliseconds(100);

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

            // Оновлюємо UI якщо:
            // 1. Накопичилось достатньо chunks АБО
            // 2. Пройшло достатньо часу
            if (_chunksSinceLastRefresh >= ChunksPerRefresh ||
                timeSinceRefresh >= MinRefreshInterval)
            {
                shouldRefresh = true;
                _chunksSinceLastRefresh = 0;
                _lastRefreshTime = DateTime.UtcNow;
            }
        }

        if (shouldRefresh)
        {
            _owner.TriggerRefresh(RawQuery);
        }
    }
}
```

**Очікуване покращення**: ⚡ **70-80% зменшення UI refresh calls**, швидше відображення відповіді

---

### 2. **Неоптимальний HTTP timeout (10 секунд)**
**Проблема**:
```csharp
// Main.cs:43
private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
```

10 секунд - це:
- ❌ Занадто довго для користувача, який очікує "швидкої" відповіді
- ❌ Створює враження "зависання" якщо provider повільний
- ❌ Блокує можливість швидко переключитися на інший запит

**Рішення - Динамічний timeout**:
```csharp
private const int InitialResponseTimeoutSeconds = 5;      // Час на початок відповіді
private const int PerTokenTimeoutSeconds = 3;            // Час між tokens
private const int AbsoluteMaxTimeoutSeconds = 30;        // Абсолютний максимум

// У налаштуваннях додати опцію
private int _requestTimeout = InitialResponseTimeoutSeconds;

// В AdditionalOptions додати:
new PluginAdditionalOption
{
    Key = "quickai_timeout",
    DisplayLabel = "Request Timeout (seconds)",
    DisplayDescription = "Maximum time to wait for AI response (3-30 seconds)",
    PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Numberbox,
    NumberValue = _requestTimeout,
    NumberBoxMin = 3,
    NumberBoxMax = 30,
    NumberBoxSmallChange = 1,
    NumberBoxLargeChange = 5
}

// ExecuteStreamingRequestAsync змінити:
using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
timeoutCts.CancelAfter(TimeSpan.FromSeconds(_requestTimeout));

// В ParseStreamAsync:
timeoutCts.CancelAfter(TimeSpan.FromSeconds(PerTokenTimeoutSeconds)); // Продовжуємо timeout на кожен token
```

**Очікуване покращення**: ⚡ **50% швидше detection of slow/failed requests**

---

### 3. **Відсутність HTTP/2 та оптимізації з'єднань**
**Проблема**:
```csharp
// Main.cs:392-403
private static HttpClient CreateHttpClient()
{
    var handler = new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.Deflate | DecompressionMethods.GZip
    };

    return new HttpClient(handler)
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
}
```

❌ Не використовує HTTP/2 повною мірою
❌ Немає connection pooling налаштувань
❌ Немає DNS caching

**Рішення - Оптимізований HTTP клієнт**:
```csharp
private static HttpClient CreateHttpClient()
{
    var handler = new SocketsHttpHandler
    {
        // HTTP/2 оптимізації
        EnableMultipleHttp2Connections = true,  // ✅ Дозволяє паралельні HTTP/2 з'єднання

        // Connection pooling
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),  // ✅ Переиспользование з'єднань
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 10,  // ✅ Достатньо для паралельних запитів

        // Compression
        AutomaticDecompression = DecompressionMethods.Brotli |
                                 DecompressionMethods.Deflate |
                                 DecompressionMethods.GZip,

        // Security
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                  System.Security.Authentication.SslProtocols.Tls13
        },

        // DNS caching
        UseCookies = false  // ✅ Не потрібні cookies, вимикаємо для продуктивності
    };

    var client = new HttpClient(handler)
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestVersion = new Version(2, 0),  // ✅ HTTP/2 за замовчуванням
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
    };

    return client;
}
```

**Очікуване покращення**: ⚡ **20-30% швидше встановлення з'єднання**, менше latency

---

## 🟡 Середньої важливості проблеми

### 4. **Відсутність кешування відповідей**
**Проблема**: Ідентичні запити йдуть на API кожен раз

**Рішення - In-Memory Cache**:
```csharp
using System.Collections.Concurrent;

private sealed class ResponseCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private const int MaxCacheSize = 50;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(30);

    private record CacheEntry(string Response, DateTime Timestamp, string Provider, string Model);

    public bool TryGet(string prompt, string provider, string model, out string? response)
    {
        var key = GenerateKey(prompt, provider, model);

        if (_cache.TryGetValue(key, out var entry))
        {
            if (DateTime.UtcNow - entry.Timestamp < CacheExpiration)
            {
                response = entry.Response;
                return true;
            }

            // Expired, remove
            _cache.TryRemove(key, out _);
        }

        response = null;
        return false;
    }

    public void Set(string prompt, string provider, string model, string response)
    {
        var key = GenerateKey(prompt, provider, model);

        // LRU eviction якщо cache занадто великий
        if (_cache.Count >= MaxCacheSize)
        {
            var oldest = _cache.OrderBy(kvp => kvp.Value.Timestamp).FirstOrDefault();
            if (!string.IsNullOrEmpty(oldest.Key))
            {
                _cache.TryRemove(oldest.Key, out _);
            }
        }

        _cache[key] = new CacheEntry(response, DateTime.UtcNow, provider, model);
    }

    private static string GenerateKey(string prompt, string provider, string model)
    {
        // Normalize prompt (lowercase, trim)
        var normalized = prompt.Trim().ToLowerInvariant();
        return $"{provider}:{model}:{normalized}";
    }

    public void Clear() => _cache.Clear();
}

// В Main class:
private readonly ResponseCache _cache = new();

// В StreamWithConfigurationAsync перед ExecuteStreamingRequestAsync:
if (_cache.TryGet(prompt, configuration.Provider, configuration.Model, out var cachedResponse))
{
    session.SetStatus("Using cached response...");
    TriggerRefresh(session.RawQuery);

    // "Stream" cached response by words для nature відображення
    var words = cachedResponse.Split(' ');
    foreach (var word in words)
    {
        if (session.Token.IsCancellationRequested) return;

        session.Append(word + " ");
        TriggerRefresh(session.RawQuery);
        await Task.Delay(20, session.Token); // Simulate streaming
    }

    session.MarkCompleted();
    TriggerRefresh(session.RawQuery);
    return;
}

// Після успішної відповіді:
if (session.HasCompleted)
{
    var fullResponse = session.SnapshotResponse(); // Додати цей метод
    _cache.Set(prompt, configuration.Provider, configuration.Model, fullResponse);
}
```

**Очікуване покращення**: ⚡ **Миттєві відповіді** на повторювані запити, економія API calls

---

### 5. **Відсутність Smart Provider Selection**
**Проблема**: Користувач вручну вибирає provider, навіть якщо один з них швидший

**Рішення - Автоматичний вибір найшвидшого провайдера**:
```csharp
private sealed class ProviderStats
{
    public string Name { get; set; } = string.Empty;
    public double AverageResponseTime { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double SuccessRate => (SuccessCount + FailureCount) > 0
        ? (double)SuccessCount / (SuccessCount + FailureCount)
        : 0;

    public double Score => SuccessRate * (1000.0 / Math.Max(AverageResponseTime, 100));
}

private readonly ConcurrentDictionary<string, ProviderStats> _providerStats = new();

private void RecordProviderMetrics(string provider, TimeSpan responseTime, bool success)
{
    var stats = _providerStats.GetOrAdd(provider, _ => new ProviderStats { Name = provider });

    lock (stats)
    {
        if (success)
        {
            stats.SuccessCount++;

            // Exponential moving average
            if (stats.AverageResponseTime == 0)
            {
                stats.AverageResponseTime = responseTime.TotalMilliseconds;
            }
            else
            {
                stats.AverageResponseTime =
                    (stats.AverageResponseTime * 0.7) + (responseTime.TotalMilliseconds * 0.3);
            }
        }
        else
        {
            stats.FailureCount++;
        }
    }
}

// Додати в налаштування checkbox:
private bool _autoSelectBestProvider = false;

new PluginAdditionalOption
{
    Key = "quickai_auto_provider",
    DisplayLabel = "Auto-select fastest provider",
    DisplayDescription = "Automatically use the provider with best performance",
    PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Checkbox,
    Value = _autoSelectBestProvider
}

// В StreamWithConfigurationAsync:
var startTime = DateTime.UtcNow;
try
{
    // ... existing streaming code ...

    var elapsed = DateTime.UtcNow - startTime;
    RecordProviderMetrics(configuration.Provider, elapsed, true);
}
catch
{
    RecordProviderMetrics(configuration.Provider, DateTime.UtcNow - startTime, false);
    throw;
}

// Метод для вибору найкращого провайдера:
private string SelectBestProvider()
{
    if (!_autoSelectBestProvider || _providerStats.IsEmpty)
    {
        return _provider;
    }

    var best = _providerStats.Values
        .Where(s => s.SuccessCount > 0)
        .OrderByDescending(s => s.Score)
        .FirstOrDefault();

    return best?.Name ?? _provider;
}
```

**Очікуване покращення**: ⚡ **10-50% швидше** через вибір найшвидшого провайдера

---

## 💡 Нові функції для покращення якості відповідей

### 6. **System Prompt Customization**
```csharp
private string _systemPrompt = "You are a helpful assistant. Provide concise, accurate answers.";

// В налаштуваннях:
new PluginAdditionalOption
{
    Key = "quickai_system_prompt",
    DisplayLabel = "System Prompt",
    DisplayDescription = "Customize AI behavior (e.g., 'Be concise', 'Explain like I'm 5')",
    PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
    TextValue = _systemPrompt
}

// В BuildHttpRequest для OpenAI schema:
messages = new[]
{
    new { role = "system", content = configuration.SystemPrompt },  // ✅ Додати system prompt
    new { role = "user", content = prompt }
}
```

---

### 7. **Quick Commands для покращення відповідей**
```csharp
// Парсинг префіксів команд у запиті
private (string command, string actualPrompt) ParseCommand(string search)
{
    if (search.StartsWith("/short ", StringComparison.OrdinalIgnoreCase))
        return ("short", search[7..]);

    if (search.StartsWith("/detailed ", StringComparison.OrdinalIgnoreCase))
        return ("detailed", search[10..]);

    if (search.StartsWith("/code ", StringComparison.OrdinalIgnoreCase))
        return ("code", search[6..]);

    return (string.Empty, search);
}

private string EnhancePromptWithCommand(string command, string prompt)
{
    return command switch
    {
        "short" => $"Provide a very brief, concise answer (max 50 words): {prompt}",
        "detailed" => $"Provide a detailed, comprehensive explanation: {prompt}",
        "code" => $"Provide code example with explanation: {prompt}",
        _ => prompt
    };
}

// У Query методі:
var (command, actualPrompt) = ParseCommand(search);
var enhancedPrompt = EnhancePromptWithCommand(command, actualPrompt);
```

**Приклади використання**:
- `ai /short what is quantum computing` → короткі 2-3 речення
- `ai /detailed explain REST APIs` → детальне пояснення
- `ai /code sort array in python` → код з коментарями

---

### 8. **Conversation History (Context Memory)**
```csharp
private sealed class ConversationHistory
{
    private readonly Queue<Message> _messages = new();
    private const int MaxMessages = 10;

    public record Message(string Role, string Content, DateTime Timestamp);

    public void AddUserMessage(string content)
    {
        _messages.Enqueue(new Message("user", content, DateTime.UtcNow));
        TrimHistory();
    }

    public void AddAssistantMessage(string content)
    {
        _messages.Enqueue(new Message("assistant", content, DateTime.UtcNow));
        TrimHistory();
    }

    private void TrimHistory()
    {
        while (_messages.Count > MaxMessages)
        {
            _messages.Dequeue();
        }
    }

    public object[] GetMessagesForApi(string systemPrompt)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        messages.AddRange(_messages.Select(m => new { role = m.Role, content = m.Content }));

        return messages.ToArray();
    }

    public void Clear() => _messages.Clear();
}

private readonly ConversationHistory _history = new();
private bool _useConversationHistory = false;

// В BuildHttpRequest:
var messagesList = _useConversationHistory
    ? _history.GetMessagesForApi(configuration.SystemPrompt)
    : new object[]
      {
          new { role = "system", content = configuration.SystemPrompt },
          new { role = "user", content = prompt }
      };

var contentPayload = new
{
    model = configuration.Model,
    messages = messagesList,  // ✅ Використовуємо історію
    stream = true,
    temperature = configuration.Temperature,
    max_tokens = configuration.MaxTokens
};
```

---

### 9. **Response Quality Indicators**
```csharp
// Додати індикатор якості у SubTitle
public Result BuildResult(string iconPath, string provider, string model)
{
    lock (_sync)
    {
        var responseText = _buffer.ToString();
        var tokenCount = responseText.Split(' ').Length;
        var responseTime = DateTime.UtcNow - _startTime;

        // ...existing code...

        subtitle = _completed
            ? $"{provider} · {model} · {tokenCount} words · {responseTime.TotalSeconds:F1}s"
            : $"Streaming... ({tokenCount} words)";

        // ...
    }
}
```

---

## 📈 Очікувані результати

| Покращення | Поточний стан | Після оптимізації | Приріст |
|-----------|--------------|-------------------|---------|
| UI refresh rate | 100+ per response | 10-20 per response | **⚡ 80% менше** |
| Response detection | 10 sec timeout | 5 sec timeout | **⚡ 50% швидше** |
| Connection setup | ~300-500ms | ~100-200ms | **⚡ 40% швидше** |
| Repeat queries | Full API call | Cached (instant) | **⚡ 100x швидше** |
| Provider selection | Manual | Auto (best) | **⚡ 10-50% швидше** |

---

## 🎯 План впровадження (пріоритети)

### Фаза 1 - Критичні (тиждень 1):
1. ✅ Batching UI updates
2. ✅ Динамічний timeout
3. ✅ HTTP/2 оптимізації

### Фаза 2 - Середні (тиждень 2):
4. ✅ Response caching
5. ✅ Smart provider selection

### Фаза 3 - Нові функції (тиждень 3-4):
6. ✅ System prompt customization
7. ✅ Quick commands
8. ✅ Conversation history
9. ✅ Quality indicators

---

## 🧪 Тестування покращень

### Benchmark тест для UI updates:
```csharp
[TestMethod]
public async Task Benchmark_UIRefreshRate()
{
    var refreshCount = 0;
    var mockApi = new Mock<IPublicAPI>();
    mockApi.Setup(x => x.ChangeQuery(It.IsAny<string>(), It.IsAny<bool>()))
           .Callback(() => refreshCount++);

    // Simulate 100 tokens received
    var session = new StreamingSession(main, "test query", "test prompt");
    for (int i = 0; i < 100; i++)
    {
        session.Append("token ");
    }

    // З batching повинно бути ~20 refresh calls замість 100
    Assert.IsTrue(refreshCount < 25, $"Too many refreshes: {refreshCount}");
}
```

---

## 📝 Додаткові рекомендації

### Security Enhancement:
```csharp
// API keys encryption з Windows DPAPI
using System.Security.Cryptography;

private static string EncryptApiKey(string plainText)
{
    var bytes = Encoding.UTF8.GetBytes(plainText);
    var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
    return Convert.ToBase64String(encrypted);
}

private static string DecryptApiKey(string encrypted)
{
    var bytes = Convert.FromBase64String(encrypted);
    var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
    return Encoding.UTF8.GetString(decrypted);
}
```

### Telemetry для моніторингу:
```csharp
private void LogPerformanceMetrics(string provider, TimeSpan responseTime, int tokenCount)
{
    var metrics = new
    {
        Provider = provider,
        ResponseTimeMs = responseTime.TotalMilliseconds,
        TokenCount = tokenCount,
        TokensPerSecond = tokenCount / responseTime.TotalSeconds,
        Timestamp = DateTime.UtcNow
    };

    // Можна зберігати в файл для аналізу
    var logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "PowerToys", "PowerToys Run", "Logs", "QuickAI_metrics.jsonl"
    );

    File.AppendAllText(logPath, JsonSerializer.Serialize(metrics) + "\n");
}
```

---

Це детальний план оптимізації з конкретними прикладами коду. Чи хочете розпочати з якоїсь конкретної оптимізації?
