#nullable enable
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Community.PowerToys.Run.Plugin.QuickAI
{
    public partial class ResultsWindow : Window
    {
        private readonly StringBuilder _fullText = new();
        private string _currentTheme = "dark";
        private bool _wordWrapEnabled = true;

        // Theme colors
        private static readonly Color DarkBackground = Color.FromRgb(0x1E, 0x1E, 0x1E);
        private static readonly Color DarkSurface = Color.FromRgb(0x25, 0x25, 0x26);
        private static readonly Color DarkBorder = Color.FromRgb(0x3C, 0x3C, 0x3C);
        private static readonly Color DarkText = Color.FromRgb(0xE4, 0xE4, 0xE4);
        private static readonly Color DarkTextSecondary = Color.FromRgb(0xA0, 0xA0, 0xA0);
        private static readonly Color DarkCodeBg = Color.FromRgb(0x0D, 0x0D, 0x0D);
        private static readonly Color DarkCodeText = Color.FromRgb(0xCE, 0x91, 0x78);
        private static readonly Color DarkAccent = Color.FromRgb(0x00, 0x78, 0xD4);

        private static readonly Color LightBackground = Color.FromRgb(0xFA, 0xFA, 0xFA);
        private static readonly Color LightSurface = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color LightBorder = Color.FromRgb(0xE0, 0xE0, 0xE0);
        private static readonly Color LightText = Color.FromRgb(0x1E, 0x1E, 0x1E);
        private static readonly Color LightTextSecondary = Color.FromRgb(0x66, 0x66, 0x66);
        private static readonly Color LightCodeBg = Color.FromRgb(0xF0, 0xF0, 0xF0);
        private static readonly Color LightCodeText = Color.FromRgb(0xA3, 0x1F, 0x34);
        private static readonly Color LightAccent = Color.FromRgb(0x00, 0x78, 0xD4);

        public ResultsWindow()
        {
            InitializeComponent();
            ApplyTheme("dark");
        }

        public void AppendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            Dispatcher.BeginInvoke(() =>
            {
                _fullText.Append(text);
                StatusText.Visibility = Visibility.Visible;
                
                // Re-render the entire document with markdown
                RenderMarkdown(_fullText.ToString());
                UpdateCharCount();
                ScrollToEnd();
            });
        }

        public void SetFullText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                _fullText.Clear();
                _fullText.Append(text ?? string.Empty);
                StatusText.Visibility = Visibility.Collapsed;
                
                RenderMarkdown(_fullText.ToString());
                UpdateCharCount();
            });
        }

        public void SetStreamingComplete()
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Visibility = Visibility.Collapsed;
            });
        }

        public void ApplyTheme(string theme)
        {
            if (theme != "dark" && theme != "light") return;
            _currentTheme = theme;

            Dispatcher.Invoke(() =>
            {
                try
                {
                    var isDark = theme == "dark";
                    
                    // Window background
                    Background = new SolidColorBrush(isDark ? DarkBackground : LightBackground);
                    Foreground = new SolidColorBrush(isDark ? DarkText : LightText);

                    // Header
                    HeaderBorder.Background = new SolidColorBrush(isDark ? DarkSurface : LightSurface);
                    HeaderBorder.BorderBrush = new SolidColorBrush(isDark ? DarkBorder : LightBorder);
                    TitleText.Foreground = new SolidColorBrush(isDark ? DarkText : LightText);

                    // Content area
                    ContentBorder.Background = new SolidColorBrush(isDark ? DarkBackground : LightBackground);
                    if (OutputDocument != null)
                    {
                        OutputDocument.Foreground = new SolidColorBrush(isDark ? DarkText : LightText);
                        OutputDocument.Background = Brushes.Transparent;
                    }

                    // Footer
                    FooterBorder.Background = new SolidColorBrush(isDark ? DarkSurface : LightSurface);
                    FooterBorder.BorderBrush = new SolidColorBrush(isDark ? DarkBorder : LightBorder);
                    CharCountText.Foreground = new SolidColorBrush(isDark ? DarkTextSecondary : LightTextSecondary);

                    // Re-render markdown with new theme colors
                    if (_fullText.Length > 0)
                    {
                        RenderMarkdown(_fullText.ToString());
                    }
                }
                catch
                {
                    // Best-effort theme application
                }
            });
        }

        private void RenderMarkdown(string text)
        {
            OutputDocument.Blocks.Clear();

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var isDark = _currentTheme == "dark";
            var textColor = isDark ? DarkText : LightText;
            var codeTextColor = isDark ? DarkCodeText : LightCodeText;
            var codeBgColor = isDark ? DarkCodeBg : LightCodeBg;

            // Split by code blocks first (``` blocks)
            var codeBlockPattern = @"```(\w*)\r?\n([\s\S]*?)```";
            var parts = Regex.Split(text, codeBlockPattern);

            var currentPara = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
            var i = 0;

            while (i < parts.Length)
            {
                // Check if this part is followed by code block content
                if (i + 2 < parts.Length && IsCodeBlockLanguage(parts, i))
                {
                    // Flush current paragraph if it has content
                    if (currentPara.Inlines.Count > 0)
                    {
                        OutputDocument.Blocks.Add(currentPara);
                        currentPara = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
                    }

                    // Parts[i+1] is language, parts[i+2] is code content
                    var language = parts[i + 1];
                    var code = parts[i + 2];

                    // Create code block
                    var codeBlock = CreateCodeBlock(code, language, codeBgColor, codeTextColor, textColor);
                    OutputDocument.Blocks.Add(codeBlock);

                    i += 3;
                    continue;
                }

                // Regular text - parse inline markdown
                var inlines = ParseInlineMarkdown(parts[i], textColor, codeTextColor, codeBgColor);
                foreach (var inline in inlines)
                {
                    currentPara.Inlines.Add(inline);
                }
                i++;
            }

            if (currentPara.Inlines.Count > 0)
            {
                OutputDocument.Blocks.Add(currentPara);
            }

            // Ensure at least one empty paragraph if no content
            if (OutputDocument.Blocks.Count == 0)
            {
                OutputDocument.Blocks.Add(new Paragraph());
            }
        }

        private static bool IsCodeBlockLanguage(string[] parts, int index)
        {
            // Simple heuristic: if text before is short or ends with newline, likely code block
            if (index >= parts.Length - 2) return false;
            var before = parts[index];
            return string.IsNullOrWhiteSpace(before) || before.EndsWith("\n") || before.EndsWith("\r");
        }

        private Section CreateCodeBlock(string code, string language, Color bgColor, Color codeColor, Color textColor)
        {
            var section = new Section
            {
                Margin = new Thickness(0, 8, 0, 8),
                Padding = new Thickness(0),
            };

            // Language label if present
            if (!string.IsNullOrWhiteSpace(language))
            {
                var langPara = new Paragraph(new System.Windows.Documents.Run(language))
                {
                    FontFamily = new FontFamily("Inter, Segoe UI Variable, Segoe UI"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(12, 4, 12, 0),
                    Background = new SolidColorBrush(bgColor),
                };
                section.Blocks.Add(langPara);
            }

            var codePara = new Paragraph
            {
                FontFamily = new FontFamily("JetBrains Mono, Cascadia Code, Consolas, Courier New"),
                FontSize = 13,
                Background = new SolidColorBrush(bgColor),
                Foreground = new SolidColorBrush(codeColor),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0),
                LineHeight = 20,
            };

            codePara.Inlines.Add(new System.Windows.Documents.Run(code.TrimEnd()));
            section.Blocks.Add(codePara);

            return section;
        }

        private Inline[] ParseInlineMarkdown(string text, Color textColor, Color codeTextColor, Color codeBgColor)
        {
            var inlines = new System.Collections.Generic.List<Inline>();

            // Pattern matches: **bold**, *italic*, `code`, __bold__, _italic_
            var pattern = @"(\*\*(.+?)\*\*)|(__(.+?)__)|(\*(.+?)\*)|(_([^_]+)_)|(`([^`]+)`)";
            var lastIndex = 0;

            foreach (Match match in Regex.Matches(text, pattern))
            {
                // Add text before match
                if (match.Index > lastIndex)
                {
                    var before = text.Substring(lastIndex, match.Index - lastIndex);
                    inlines.AddRange(ParseNewlines(before, textColor));
                }

                // Determine match type and create inline
                if (match.Groups[2].Success) // **bold**
                {
                    inlines.Add(new Bold(new System.Windows.Documents.Run(match.Groups[2].Value))
                    {
                        Foreground = new SolidColorBrush(textColor)
                    });
                }
                else if (match.Groups[4].Success) // __bold__
                {
                    inlines.Add(new Bold(new System.Windows.Documents.Run(match.Groups[4].Value))
                    {
                        Foreground = new SolidColorBrush(textColor)
                    });
                }
                else if (match.Groups[6].Success) // *italic*
                {
                    inlines.Add(new Italic(new System.Windows.Documents.Run(match.Groups[6].Value))
                    {
                        Foreground = new SolidColorBrush(textColor)
                    });
                }
                else if (match.Groups[8].Success) // _italic_
                {
                    inlines.Add(new Italic(new System.Windows.Documents.Run(match.Groups[8].Value))
                    {
                        Foreground = new SolidColorBrush(textColor)
                    });
                }
                else if (match.Groups[10].Success) // `code`
                {
                    var codeRun = new System.Windows.Documents.Run(match.Groups[10].Value)
                    {
                        FontFamily = new FontFamily("JetBrains Mono, Cascadia Code, Consolas, Courier New"),
                        Foreground = new SolidColorBrush(codeTextColor),
                        Background = new SolidColorBrush(codeBgColor),
                    };
                    inlines.Add(codeRun);
                }

                lastIndex = match.Index + match.Length;
            }

            // Add remaining text
            if (lastIndex < text.Length)
            {
                var remaining = text.Substring(lastIndex);
                inlines.AddRange(ParseNewlines(remaining, textColor));
            }

            return inlines.ToArray();
        }

        private Inline[] ParseNewlines(string text, Color textColor)
        {
            var inlines = new System.Collections.Generic.List<Inline>();
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    inlines.Add(new LineBreak());
                }
                if (!string.IsNullOrEmpty(lines[i]))
                {
                    inlines.Add(new System.Windows.Documents.Run(lines[i]) { Foreground = new SolidColorBrush(textColor) });
                }
            }

            return inlines.ToArray();
        }

        private void UpdateCharCount()
        {
            CharCountText.Text = $"{_fullText.Length:N0} characters";
        }

        private void ScrollToEnd()
        {
            try
            {
                var last = OutputViewer.Document?.Blocks.LastBlock;
                last?.BringIntoView();
            }
            catch
            {
                // Ignore scroll errors
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard();
        }

        private void WrapButton_Click(object sender, RoutedEventArgs e)
        {
            _wordWrapEnabled = !_wordWrapEnabled;
            OutputViewer.HorizontalScrollBarVisibility = _wordWrapEnabled 
                ? ScrollBarVisibility.Disabled 
                : ScrollBarVisibility.Auto;
        }

        private void Copy_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            CopyToClipboard();
        }

        private void Close_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Close();
        }

        private void CopyToClipboard()
        {
            try
            {
                var text = _fullText.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    Clipboard.SetText(text);
                    
                    // Brief visual feedback
                    var originalText = CharCountText.Text;
                    CharCountText.Text = "✓ Copied to clipboard";
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.5)
                    };
                    timer.Tick += (s, e) =>
                    {
                        CharCountText.Text = originalText;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
            catch
            {
                // Clipboard operation failed
            }
        }
    }
}
