#nullable enable
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WpfMath;
using WpfMath.Controls;

namespace Community.PowerToys.Run.Plugin.QuickAI
{
    public partial class ResultsWindow : Window
    {
        /// <summary>Raised when the user submits a follow-up question from the input box.</summary>
        public event Action<string>? FollowUpRequested;

        private readonly StringBuilder _fullText = new();
        private readonly StringBuilder _pendingText = new();
        private System.Windows.Threading.DispatcherTimer? _renderTimer;
        private bool _renderTimerPending;
        private const int RenderThrottleMs = 220;
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
            PositionAsStickyNote();
        }

        /// <summary>
        /// Position the window like a sticky note: bottom-right of the work area,
        /// with a small offset from the corner.
        /// </summary>
        private void PositionAsStickyNote()
        {
            try
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - Width - 24;
                Top = wa.Bottom - Height - 24;
            }
            catch
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public void AppendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            Dispatcher.BeginInvoke(() =>
            {
                _fullText.Append(text);
                _pendingText.Append(text);
                StatusText.Visibility = Visibility.Visible;

                ScheduleRender();
                UpdateCharCount();
            });
        }

        /// <summary>
        /// Throttle re-rendering during streaming: multiple tokens arriving in a
        /// short window are coalesced into a single full render + scroll.
        /// </summary>
        private void ScheduleRender()
        {
            if (_renderTimerPending)
            {
                return;
            }

            _renderTimerPending = true;

            if (_renderTimer == null)
            {
                _renderTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(RenderThrottleMs)
                };
                _renderTimer.Tick += (_, _) =>
                {
                    _renderTimer.Stop();
                    _renderTimerPending = false;
                    FlushPendingRender();
                };
            }

            _renderTimer.Start();
        }

        private void FlushPendingRender()
        {
            if (_pendingText.Length == 0)
            {
                return;
            }

            _pendingText.Clear();
            RenderMarkdown(_fullText.ToString());
            ScrollToEnd();
        }

        private void FlushRenderNow()
        {
            if (_renderTimer != null && _renderTimer.IsEnabled)
            {
                _renderTimer.Stop();
                _renderTimerPending = false;
            }
            _pendingText.Clear();
            RenderMarkdown(_fullText.ToString());
            ScrollToEnd();
        }

        public void SetFullText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                _fullText.Clear();
                _fullText.Append(text ?? string.Empty);
                _pendingText.Clear();
                StatusText.Visibility = Visibility.Collapsed;
                
                FlushRenderNow();
                UpdateCharCount();
            });
        }

        public void SetStreamingComplete()
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Visibility = Visibility.Collapsed;
                // Final flush: render the complete text with full markdown.
                FlushRenderNow();
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

                    // Window background (transparent outer; the card itself carries the theme)
                    Background = Brushes.Transparent;
                    Foreground = new SolidColorBrush(isDark ? DarkText : LightText);

                    // Card shell
                    if (CardBorder != null)
                    {
                        CardBorder.Background = new SolidColorBrush(isDark ? DarkBackground : LightBackground);
                        CardBorder.BorderBrush = new SolidColorBrush(isDark ? DarkBorder : LightBorder);
                    }

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

                    // Follow-up input area
                    if (FollowUpBorder != null)
                    {
                        FollowUpBorder.Background = new SolidColorBrush(isDark ? DarkSurface : LightSurface);
                        FollowUpBorder.BorderBrush = new SolidColorBrush(isDark ? DarkBorder : LightBorder);
                    }

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

            var i = 0;

            while (i < parts.Length)
            {
                // Check if this part is followed by code block content
                if (i + 2 < parts.Length && IsCodeBlockLanguage(parts, i))
                {
                    // Parts[i+1] is language, parts[i+2] is code content
                    var language = parts[i + 1];
                    var code = parts[i + 2];

                    // Create code block
                    var codeBlock = CreateCodeBlock(code, language, codeBgColor, codeTextColor, textColor);
                    OutputDocument.Blocks.Add(codeBlock);

                    i += 3;
                    continue;
                }

                // Regular text - parse block-level markdown (headings, quotes, lists, hr) + inline
                ParseBlockMarkdown(parts[i], textColor, codeTextColor, codeBgColor);
                i++;
            }

            // Ensure at least one empty paragraph if no content
            if (OutputDocument.Blocks.Count == 0)
            {
                OutputDocument.Blocks.Add(new Paragraph());
            }
        }

        /// <summary>
        /// Parse block-level markdown (headings, blockquotes, lists, horizontal rules)
        /// and emit the corresponding WPF FlowDocument blocks.
        /// </summary>
        private void ParseBlockMarkdown(string text, Color textColor, Color codeTextColor, Color codeBgColor)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var blocks = new System.Collections.Generic.List<Block>();
            var paraLines = new System.Collections.Generic.List<string>();

            // Accumulate blockquote lines
            var quoteLines = new System.Collections.Generic.List<string>();
            // Accumulate list lines
            var listLines = new System.Collections.Generic.List<(string Marker, string Content)>();
            var inList = false;

            // Accumulate table rows
            var tableRows = new System.Collections.Generic.List<string[]>();
            var tableAlign = new System.Collections.Generic.List<string>();
            var inTable = false;

            void FlushTable()
            {
                if (tableRows.Count == 0)
                {
                    return;
                }

                // Render the table as a Grid (UIElement) wrapped in a BlockUIContainer so it
                // hugs its content instead of stretching across the full window width.
                var grid = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 12),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    SnapsToDevicePixels = true
                };

                var colCount = tableRows.Max(r => r.Length);
                for (var c = 0; c < colCount; c++)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                }
                for (var r = 0; r < tableRows.Count; r++)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }

                var borderBrush = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128));
                var cellBorder = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128));

                for (var r = 0; r < tableRows.Count; r++)
                {
                    var isHeader = r == 0;
                    var cells = tableRows[r];

                    for (var c = 0; c < cells.Length; c++)
                    {
                        var border = new Border
                        {
                            BorderBrush = borderBrush,
                            BorderThickness = new Thickness(1),
                            Padding = new Thickness(8, 4, 8, 4),
                            Background = isHeader
                                ? new SolidColorBrush(Color.FromArgb(25, 128, 128, 128))
                                : Brushes.Transparent,
                            Child = new TextBlock
                            {
                                TextWrapping = TextWrapping.Wrap,
                                FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
                                FontFamily = new FontFamily("Segoe UI, Inter, Segoe UI Variable"),
                                Foreground = new SolidColorBrush(textColor)
                            }
                        };

                        var tb = (TextBlock)border.Child;
                        foreach (var inline in ParseInlineMarkdown(cells[c].Trim(), textColor, codeTextColor, codeBgColor))
                        {
                            tb.Inlines.Add(inline);
                        }

                        Grid.SetRow(border, r);
                        Grid.SetColumn(border, c);
                        grid.Children.Add(border);
                    }
                }

                blocks.Add(new BlockUIContainer(grid));
                tableRows.Clear();
                tableAlign.Clear();
                inTable = false;
            }

            void FlushParagraph()
            {
                if (paraLines.Count > 0)
                {
                    var para = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
                    para.Inlines.AddRange(ParseInlineMarkdown(string.Join("\n", paraLines), textColor, codeTextColor, codeBgColor));
                    blocks.Add(para);
                    paraLines.Clear();
                }
            }

            void FlushQuote()
            {
                if (quoteLines.Count > 0)
                {
                    var quote = new BlockUIContainer
                    {
                        Child = new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 128, 128, 128)),
                            BorderThickness = new Thickness(3, 0, 0, 0),
                            Padding = new Thickness(10, 6, 10, 6),
                            Margin = new Thickness(0, 0, 0, 12),
                            CornerRadius = new CornerRadius(0, 4, 4, 0),
                            Child = new TextBlock
                            {
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = new SolidColorBrush(Color.FromArgb(220, textColor.R, textColor.G, textColor.B)),
                                Inlines = { }
                            }
                        }
                    };

                    // Build quote content as a TextBlock with parsed inlines
                    var tb = (TextBlock)((Border)quote.Child).Child;
                    tb.Inlines.Clear();
                    foreach (var inline in ParseInlineMarkdown(string.Join("\n", quoteLines), textColor, codeTextColor, codeBgColor))
                    {
                        tb.Inlines.Add(inline);
                    }
                    blocks.Add(quote);
                    quoteLines.Clear();
                }
            }

            void FlushList()
            {
                if (listLines.Count > 0)
                {
                    var list = new List
                    {
                        MarkerStyle = inList ? TextMarkerStyle.Disc : TextMarkerStyle.Disc,
                        Margin = new Thickness(0, 0, 0, 12),
                        Padding = new Thickness(20, 0, 0, 0)
                    };
                    foreach (var (marker, content) in listLines)
                    {
                        var li = new ListItem
                        {
                            Margin = new Thickness(0, 0, 0, 4)
                        };
                        var para = new Paragraph();
                        para.Inlines.AddRange(ParseInlineMarkdown(content, textColor, codeTextColor, codeBgColor));
                        li.Blocks.Add(para);
                        list.ListItems.Add(li);
                    }
                    blocks.Add(list);
                    listLines.Clear();
                }
            }

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();

                // Table row: | a | b | ... |
                if (line.Trim().StartsWith("|") && line.Trim().EndsWith("|") && line.Count(ch => ch == '|') >= 2)
                {
                    // Split cells (strip outer pipes, split on unescaped pipes)
                    var trimmed = line.Trim().TrimStart('|').TrimEnd('|');
                    var cells = trimmed.Split('|').Select(c => c.Trim()).ToArray();

                    // Separator row: |---|---| -> alignment row, skip but keep table going
                    if (tableRows.Count == 0 && cells.All(c => Regex.IsMatch(c, @"^:?-{3,}:?$")))
                    {
                        tableAlign = cells.Select(c => c.StartsWith(":") ? "left" : c.EndsWith(":") ? "right" : "center").ToList();
                        inTable = true;
                        continue;
                    }

                    // Start a new table if previous content existed
                    FlushParagraph();
                    FlushQuote();
                    FlushList();
                    inTable = true;
                    tableRows.Add(cells);
                    continue;
                }

                // Table separator row (often has no leading pipe: --- | --- | ---)
                if (inTable && tableRows.Count >= 1 && Regex.IsMatch(line.Trim(), @"^:?-{3,}:?(?:\s*\|\s*:?-{3,}:?)+$"))
                {
                    continue;
                }

                // Leaving a table: flush it
                if (inTable && tableRows.Count > 0)
                {
                    FlushTable();
                }

                // Horizontal rule: ---, ***, ___
                if (Regex.IsMatch(line, @"^\s*(?:-{3,}|\*{3,}|_{3,})\s*$"))
                {
                    FlushParagraph();
                    FlushQuote();
                    FlushList();
                    blocks.Add(new BlockUIContainer
                    {
                        Child = new Border
                        {
                            Height = 1,
                            Background = new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)),
                            Margin = new Thickness(0, 4, 0, 12)
                        }
                    });
                    inList = false;
                    continue;
                }

                // Heading: #, ##, ### ...
                var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
                if (headingMatch.Success)
                {
                    FlushParagraph();
                    FlushQuote();
                    FlushList();
                    var level = headingMatch.Groups[1].Value.Length;
                    var content = headingMatch.Groups[2].Value;
                    var fontSize = level <= 1 ? 20 : level == 2 ? 17 : level == 3 ? 15 : 13.5;
                    var heading = new Paragraph
                    {
                        FontSize = fontSize,
                        FontWeight = level <= 3 ? FontWeights.Bold : FontWeights.SemiBold,
                        Margin = new Thickness(0, level <= 2 ? 10 : 8, 0, 6),
                        Foreground = new SolidColorBrush(textColor),
                        FontFamily = new FontFamily("Segoe UI, Inter, Segoe UI Variable")
                    };
                    heading.Inlines.AddRange(ParseInlineMarkdown(content, textColor, codeTextColor, codeBgColor));
                    blocks.Add(heading);
                    inList = false;
                    continue;
                }

                // Blockquote: > ...
                if (line.StartsWith(">"))
                {
                    FlushParagraph();
                    FlushList();
                    quoteLines.Add(line.TrimStart('>').Trim());
                    inList = false;
                    continue;
                }

                // Unordered list: - , * , + followed by space
                var ulMatch = Regex.Match(line, @"^\s*[-*+]\s+(.*)$");
                // Ordered list: 1. , 2. etc.
                var olMatch = Regex.Match(line, @"^\s*(\d+)\.\s+(.*)$");
                if (ulMatch.Success || olMatch.Success)
                {
                    FlushParagraph();
                    FlushQuote();
                    inList = true;
                    listLines.Add((ulMatch.Success ? "•" : $"{olMatch.Groups[1].Value}.", ulMatch.Success ? ulMatch.Groups[1].Value : olMatch.Groups[2].Value));
                    continue;
                }

                // Blank line separates blocks
                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushParagraph();
                    FlushQuote();
                    FlushList();
                    inList = false;
                    continue;
                }

                // Regular text: accumulate into current paragraph
                paraLines.Add(line);
            }

            FlushParagraph();
            FlushQuote();
            FlushList();
            FlushTable();

            foreach (var block in blocks)
            {
                OutputDocument.Blocks.Add(block);
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

            // Pattern matches: **bold**, *italic*, `code`, __bold__, _italic_, and LaTeX math: \(...\), \[...\], $...$, $$...$$
            // Math must be matched BEFORE regular markdown to avoid conflicts
            var pattern = @"(\$\$([\s\S]+?)\$\$)|(\\(?:\[|\()([\s\S]+?)\\(?:\]|\)))|(\$([^\s$][^$]*?)\$)|(\*\*(.+?)\*\*)|(__(.+?)__)|(\*(.+?)\*)|(_([^_]+)_)|(`([^`]+)`)";
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
                if (match.Groups[2].Success) // $$...$$ display math
                {
                    var math = CreateMathInline(match.Groups[2].Value, textColor);
                    if (math != null) inlines.Add(math);
                }
                else if (match.Groups[4].Success) // \(...\) or \[...\]
                {
                    var math = CreateMathInline(match.Groups[4].Value, textColor);
                    if (math != null) inlines.Add(math);
                }
                else if (match.Groups[6].Success) // $...$ inline math
                {
                    var math = CreateMathInline(match.Groups[6].Value, textColor);
                    if (math != null) inlines.Add(math);
                }
                else if (match.Groups[8].Success) // **bold**
                {
                    inlines.Add(new Bold(new System.Windows.Documents.Run(match.Groups[8].Value)
                    {
                        FontFamily = new FontFamily("Segoe UI, Inter, Segoe UI Variable")
                    })
                    {
                        Foreground = new SolidColorBrush(textColor)
                    });
                }
                else if (match.Groups[10].Success) // __bold__
                {
                    inlines.Add(new Bold(new System.Windows.Documents.Run(match.Groups[10].Value)
                    {
                        FontFamily = new FontFamily("Segoe UI, Inter, Segoe UI Variable")
                    })
                    {
                        Foreground = new SolidColorBrush(textColor)
                    });
                }
                else if (match.Groups[12].Success) // *italic*
                {
                    inlines.Add(new Italic(new System.Windows.Documents.Run(match.Groups[12].Value)
                    {
                        FontFamily = new FontFamily("Segoe UI, Inter, Segoe UI Variable")
                    })
                    {
                        Foreground = new SolidColorBrush(textColor)
                    });
                }
                else if (match.Groups[14].Success) // _italic_
                {
                    inlines.Add(new Italic(new System.Windows.Documents.Run(match.Groups[14].Value)
                    {
                        FontFamily = new FontFamily("Segoe UI, Inter, Segoe UI Variable")
                    })
                    {
                        Foreground = new SolidColorBrush(textColor)
                    });
                }
                else if (match.Groups[16].Success) // `code`
                {
                    var codeRun = new System.Windows.Documents.Run(match.Groups[16].Value)
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

        /// <summary>
        /// Create a WPF inline element that renders a LaTeX formula using WpfMath.
        /// </summary>
        private Inline? CreateMathInline(string latex, Color textColor)
        {
            var original = latex.Trim();
            if (string.IsNullOrEmpty(original))
            {
                return new System.Windows.Documents.Run(string.Empty);
            }

            // Clean up LaTeX that WpfMath can't handle but is common in LLM output.
            var cleaned = SanitizeLatex(original);

            try
            {
                // Validate BEFORE rendering: WpfMath renders an error placeholder (red dot)
                // instead of throwing when parsing fails, so pre-parse to detect problems.
                var parser = WpfMath.Parsers.WpfTeXFormulaParser.Instance;
                parser.Parse(cleaned, null);

                var control = new FormulaControl
                {
                    Formula = cleaned,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 2, 0),
                    Foreground = new SolidColorBrush(textColor),
                    FontFamily = new FontFamily("Segoe UI, Inter, Segoe UI Variable"),
                    FontSize = 10
                };

                // Some failures slip past Parse() but surface as an error state when
                // the control renders (red dot). Detect that and fall back to raw text.
                if (control.HasError)
                {
                    return new System.Windows.Documents.Run(original);
                }

                return new InlineUIContainer(control)
                {
                    BaselineAlignment = BaselineAlignment.Center
                };
            }
            catch (Exception)
            {
                // If LaTeX parsing fails, fall back to showing the raw text (no red dot).
                return new System.Windows.Documents.Run(original);
            }
        }

        /// <summary>
        /// Rewrite common LaTeX constructs that WpfMath (xaml-math) cannot render
        /// into equivalent forms it understands, or strip unsupported noise.
        /// </summary>
        private static string SanitizeLatex(string latex)
        {
            var s = latex;

            // \begin{...} ... \end{...} environments: strip wrappers, keep inner content.
            // WpfMath does not support environments like align/array/cases; the inner
            // math (often with \\ line breaks) still renders as a formula body.
            s = Regex.Replace(s, @"\\begin\{(align\*?|aligned\*?|array\*?|cases\*?|matrix\*?|pmatrix\*?|bmatrix\*?|vmatrix\*?|equation\*?|gather\*?|split\*?|multline\*?|tabular\*?|center\*?)\*?\}", "");
            s = Regex.Replace(s, @"\\end\{(align\*?|aligned\*?|array\*?|cases\*?|matrix\*?|pmatrix\*?|bmatrix\*?|vmatrix\*?|equation\*?|gather\*?|split\*?|multline\*?|tabular\*?|center\*?)\*?\}", "");
            s = Regex.Replace(s, @"\\begin\{.*?\}", "");
            s = Regex.Replace(s, @"\\end\{.*?\}", "");

            // \operatorname{...}: WpfMath doesn't support it; convert to \mathrm{...}
            s = Regex.Replace(s, @"\\operatorname\*?\{([^{}]*?)\}\s*", m => "\\mathrm{" + m.Groups[1].Value + "}");

            // Unsupported multi-letter operator commands (\arccot, \arcsinh, ...): map to \operatorname-equivalent \mathrm{}
            s = Regex.Replace(s, @"\\(arccot|arcsec|arccsc|arcsinh|arccosh|arctanh|coth|sech|csch|sgn|argmax|argmin|diag|bmod|pmod)\b", m => "\\mathrm{" + m.Groups[1].Value + "}");

            // \text{...} with CJK/non-math content: WpfMath can't render text mode well.
            // Replace \text{...} with the content wrapped in \mathrm{} (renders as upright text).
            s = Regex.Replace(s, @"\\text\{([^{}]*?)\}\s*", m => "\\mathrm{" + m.Groups[1].Value + "}");
            s = Regex.Replace(s, @"\\textnormal\{([^{}]*?)\}\s*", m => "\\mathrm{" + m.Groups[1].Value + "}");
            s = Regex.Replace(s, @"\\textrm\{([^{}]*?)\}\s*", m => "\\mathrm{" + m.Groups[1].Value + "}");

            // \left. and \right. (invisible delimiters): WpfMath may choke; drop them.
            s = s.Replace("\\left.", "").Replace("\\right.", "");

            // \displaystyle / \textstyle hints: remove.
            s = s.Replace("\\displaystyle", "").Replace("\\textstyle", "");

            // \hspace / \vspace / \quad noise: keep \quad (renders as space), drop hspace/vspace.
            s = Regex.Replace(s, @"\\hspace\{[^}]*\}", " ");
            s = Regex.Replace(s, @"\\vspace\{[^}]*\}", " ");

            // Unsupported color commands.
            s = Regex.Replace(s, @"\\color\{[^}]*\}", "");
            s = Regex.Replace(s, @"\\textcolor\{[^}]*\}\{([^{}]*?)\}", m => m.Groups[1].Value);

            // \n (newline) / double backslash line breaks inside math: convert to spaces.
            s = s.Replace("\\\\", " ").Replace("\\n", " ");

            // Common commands WpfMath doesn't know: \tag, \label, \nonumber, \notag, \hline
            s = Regex.Replace(s, @"\\tag\{[^}]*\}", "");
            s = Regex.Replace(s, @"\\label\{[^}]*\}", "");
            s = Regex.Replace(s, @"\\nonumber", "").Replace("\\notag", "");
            s = s.Replace("\\hline", " ");

            // \degree / \textdegree / \deg
            s = s.Replace("\\degree", "^\\circ").Replace("\\textdegree", "^\\circ");

            return s.Trim();
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
                    inlines.Add(new System.Windows.Documents.Run(lines[i])
                    {
                        Foreground = new SolidColorBrush(textColor),
                        FontFamily = new FontFamily("Segoe UI, Inter, Segoe UI Variable")
                    });
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
                var doc = OutputViewer?.Document;
                var last = doc?.Blocks.LastBlock;
                if (last != null)
                {
                    // Scroll the last block into view (WPF FlowDocument pattern)
                    last.BringIntoView();
                }
            }
            catch
            {
                // Ignore scroll errors
            }
        }

        private void FollowUpBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(FollowUpBox.Text))
            {
                e.Handled = true;
                SubmitFollowUp();
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SubmitFollowUp();
        }

        private void FollowUpBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SendButton.IsEnabled = !string.IsNullOrWhiteSpace(FollowUpBox.Text);
        }

        private void SubmitFollowUp()
        {
            var question = FollowUpBox.Text.Trim();
            if (string.IsNullOrEmpty(question))
            {
                return;
            }

            FollowUpBox.Text = string.Empty;
            SendButton.IsEnabled = false;
            FollowUpRequested?.Invoke(question);
        }

        /// <summary>
        /// Append a user question into the document as a distinct "You" block,
        /// then re-render. Keeps the conversation visible in the sticky note.
        /// </summary>
        public void AppendUserQuestion(string question)
        {
            if (string.IsNullOrEmpty(question)) return;

            Dispatcher.Invoke(() =>
            {
                var isDark = _currentTheme == "dark";
                var qColor = isDark ? DarkAccent : LightAccent;
                var accentText = isDark ? DarkTextSecondary : LightTextSecondary;

                // Question marker + text
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 6, 0, 2),
                    FontFamily = new FontFamily("Inter, Segoe UI Variable, Segoe UI")
                };
                paragraph.Inlines.Add(new System.Windows.Documents.Run("❓ ")
                {
                    FontSize = 12,
                    Foreground = new SolidColorBrush(qColor)
                });
                paragraph.Inlines.Add(new System.Windows.Documents.Run(question)
                {
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(accentText)
                });

                OutputDocument.Blocks.Add(paragraph);

                // Divider between turns (a thin bordered paragraph)
                OutputDocument.Blocks.Add(new Paragraph
                {
                    Margin = new Thickness(0, 4, 0, 6),
                    BorderBrush = new SolidColorBrush(isDark ? DarkBorder : LightBorder),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0)
                });

                ScrollToEnd();
            });
        }

        public void SetFollowUpBusy(bool busy)
        {
            Dispatcher.BeginInvoke(() =>
            {
                SendButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(FollowUpBox.Text);
                FollowUpBox.IsEnabled = !busy;
                StatusText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
                if (busy)
                {
                    StatusText.Text = " · Thinking...";
                }
            });
        }

        public void SetStatusText(string text)
        {
            Dispatcher.BeginInvoke(() =>
            {
                StatusText.Text = text;
                StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            });
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
