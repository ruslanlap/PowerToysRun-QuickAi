# PR Security Review Checklist

Use this checklist when reviewing PRs, especially those that add new UI components or handle user input.

## 🔒 Critical Security Checks

### 1. XAML/WPF UI Security

#### XSS and Code Injection
- [ ] **No dynamic XAML parsing**: Ensure no `XamlReader.Parse()` or `XamlReader.Load()` with user-controlled strings
- [ ] **No dynamic code execution**: Check for `Assembly.Load()`, `Activator.CreateInstance()` with user input
- [ ] **Safe data binding**: Verify all bindings use proper value converters and validation
- [ ] **Command injection**: No shell commands constructed from user input without sanitization

#### Content Rendering
- [ ] **FlowDocument/RichTextBox safety**: If using FlowDocument, ensure:
  - No `Hyperlink` navigation to arbitrary URLs without validation
  - No JavaScript injection via WebBrowser control
  - Proper encoding of AI response text before rendering
- [ ] **HTML rendering**: If rendering HTML, use safe libraries and sanitize content
- [ ] **Image loading**: Validate image sources, avoid loading from untrusted URLs

### 2. Thread Safety

#### Dispatcher Usage
- [ ] **Correct Dispatcher.Invoke usage**:
  ```csharp
  // ✅ GOOD: Async, non-blocking
  Dispatcher.BeginInvoke(() => { ... }, DispatcherPriority.Background);

  // ❌ BAD: Synchronous, can deadlock
  Dispatcher.Invoke(() => { ... });
  ```
- [ ] **No deadlocks**: Ensure UI thread never waits synchronously on background thread
- [ ] **Proper priority**: Use `DispatcherPriority.Background` for non-critical updates
- [ ] **Exception handling**: Wrap Dispatcher calls in try-catch to prevent crashes

#### Race Conditions
- [ ] **Proper locking**: Check all shared state access is protected by locks
- [ ] **Thread-safe collections**: Use `ConcurrentQueue`, `ConcurrentDictionary` if needed
- [ ] **Cancellation tokens**: Verify proper `CancellationToken` usage in async methods
- [ ] **No torn reads/writes**: Ensure atomic operations for shared variables

### 3. Input Validation and Sanitization

#### AI Response Handling
- [ ] **Length limits**: Enforce maximum response length to prevent memory exhaustion
- [ ] **Character encoding**: Properly handle UTF-8, emoji, special characters
- [ ] **Null/empty checks**: Validate all external input before processing
- [ ] **Malicious content**: Consider filtering dangerous patterns (if applicable)

#### User Input
- [ ] **Query validation**: Sanitize user queries before sending to API
- [ ] **Settings validation**: Validate all settings (URLs, tokens, numbers) before use
- [ ] **Path traversal**: If handling file paths, prevent directory traversal attacks

### 4. Resource Management

#### Memory Leaks
- [ ] **Event unsubscription**: All `+=` event handlers have matching `-=` in Dispose
- [ ] **IDisposable implementation**: Proper Dispose pattern for:
  - Windows/controls
  - Event subscriptions
  - Timers
  - HTTP clients
  - Streams
- [ ] **No circular references**: Check for object graphs that prevent GC
- [ ] **StringBuilder capacity**: Set initial capacity to avoid reallocations

#### Window Management
- [ ] **Window disposal**: ResultsWindow properly disposed after closing
- [ ] **Singleton pattern**: Only one results window per query (avoid duplicates)
- [ ] **Owner window**: Set `Owner` property to prevent orphaned windows
- [ ] **ShowDialog vs Show**: Use appropriate method to control lifetime

### 5. Error Handling

#### Exception Safety
- [ ] **Try-catch blocks**: Wrap risky operations (networking, UI updates, parsing)
- [ ] **User-friendly errors**: Don't expose stack traces to users
- [ ] **Logging**: Log errors for debugging without exposing sensitive data
- [ ] **Graceful degradation**: Plugin continues working even if one feature fails

#### Edge Cases
- [ ] **Null reference protection**: Use null-conditional operators (`?.`, `??`)
- [ ] **Empty responses**: Handle empty/whitespace-only AI responses
- [ ] **Network failures**: Proper timeout and retry logic
- [ ] **Window closed during streaming**: Handle window disposal mid-stream

### 6. Performance and DoS Protection

#### Resource Limits
- [ ] **Streaming throttling**: Batch UI updates (e.g., every 3 tokens or 150ms)
- [ ] **Max tokens**: Enforce reasonable limits (prevent huge responses)
- [ ] **Timeout protection**: All HTTP requests have timeouts
- [ ] **Concurrent requests**: Limit number of simultaneous queries

#### UI Responsiveness
- [ ] **No UI blocking**: All heavy operations on background threads
- [ ] **Smooth scrolling**: Auto-scroll doesn't freeze UI
- [ ] **Animation performance**: Theme transitions don't lag
- [ ] **Memory usage**: Monitor memory during long streaming sessions

## 🎯 Specific Checks for ResultsWindow PR

Based on the PR description, check these specific items:

### New ResultsWindow.xaml
- [ ] **FlowDocument content source**: How is AI text added to FlowDocument?
  - Using `Run` elements (safe) or dynamic XAML (unsafe)?
  - Proper escaping of special characters?
- [ ] **Hyperlink handling**: If supporting hyperlinks, validate URLs before navigation
- [ ] **Resource dictionary**: Theme resources properly scoped, no global pollution
- [ ] **Window properties**: Proper `SizeToContent`, `ResizeMode`, `WindowStartupLocation`

### StreamingSession.TokenReceived Event
- [ ] **Event subscription**: Where is `TokenReceived +=` added? Is it unsubscribed?
- [ ] **Event args**: Does event pass sanitized data or raw API response?
- [ ] **Thread context**: Is event raised on UI thread or background thread?
- [ ] **Exception propagation**: What happens if event handler throws?

### Dispatcher.BeginInvoke Usage
- [ ] **Correct priority**: Using `Background` or `Normal`, not `Send`?
- [ ] **No closure captures**: Avoid capturing large objects in lambda
- [ ] **Exception handling**: Try-catch inside Dispatcher callback
- [ ] **Cancellation check**: Verify window still exists before updating

### Theme Support
- [ ] **Theme resources**: Using PowerToys theme API correctly?
- [ ] **Dynamic updates**: Theme change event handler properly registered/unregistered
- [ ] **Resource lookup**: Fallback values if theme resource missing
- [ ] **Window already open**: What happens if theme changes while window is visible?

### Error Handling in TriggerRefresh
- [ ] **What error was being thrown**: Understanding root cause
- [ ] **Proper fix**: Is try-catch the right solution or symptom fix?
- [ ] **Window disposal check**: Verify window exists before calling API
- [ ] **Null reference safety**: Check `_context?.API?.ChangeQuery()` pattern

## 📋 Code Review Process

### Step 1: Read the Diff
- [ ] Review all changed files line by line
- [ ] Identify new classes, methods, events
- [ ] Note any changed access modifiers or visibility

### Step 2: Check Dependencies
- [ ] Any new NuGet packages? Verify they're trusted and updated
- [ ] New assembly references?
- [ ] Check for supply chain risks

### Step 3: Test Locally
- [ ] Build the PR branch locally
- [ ] Test streaming functionality
- [ ] Test theme switching
- [ ] Test edge cases (close window mid-stream, network failure, etc.)
- [ ] Check Task Manager for memory leaks
- [ ] Test on both x64 and ARM64 (if possible)

### Step 4: Security Scan
- [ ] Run static analysis (if available)
- [ ] Check for common CWE patterns
- [ ] Review crypto usage (if any)
- [ ] Check for hardcoded secrets or API keys

### Step 5: Performance Test
- [ ] Long streaming responses (1000+ tokens)
- [ ] Rapid queries (open/close window quickly)
- [ ] Theme switching during streaming
- [ ] Multiple windows (if allowed)

## 🚨 Red Flags (Immediate Review Required)

- ❌ Any use of `eval()`, `Invoke()`, `DynamicInvoke()`
- ❌ `Process.Start()` with user input
- ❌ SQL queries with string concatenation
- ❌ Deserializing untrusted data without validation
- ❌ `AllowUnsafeHeaderParsing = true`
- ❌ Disabled SSL certificate validation
- ❌ Hardcoded credentials or API keys
- ❌ Unbounded loops or recursion
- ❌ File I/O without path validation
- ❌ Registry access without validation

## ✅ Approval Criteria

Before approving PR:
- [ ] All critical security checks passed
- [ ] No memory leaks detected in testing
- [ ] UI remains responsive during streaming
- [ ] Theme switching works correctly
- [ ] No exceptions in normal operation
- [ ] Edge cases handled gracefully
- [ ] Code follows existing patterns
- [ ] Proper error messages for users
- [ ] Documentation updated (if needed)
- [ ] CLAUDE.md updated (if architecture changed)

## 📚 References

- [Microsoft WPF Security Guidelines](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/security-wpf)
- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [CWE Top 25](https://cwe.mitre.org/top25/)
- [PowerToys Security Policy](https://github.com/microsoft/PowerToys/security/policy)
