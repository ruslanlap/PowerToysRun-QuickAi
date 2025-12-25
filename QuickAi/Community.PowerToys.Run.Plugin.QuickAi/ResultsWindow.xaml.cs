using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Community.PowerToys.Run.Plugin.QuickAI
{
    public partial class ResultsWindow : Window
    {
        public ResultsWindow()
        {
            InitializeComponent();
        }

        public void AppendText(string text)
        {
            // keep UI thread safety
            Dispatcher.Invoke(() =>
            {
                // now use a sampler way to append text
                // just add a new Run to the Paragraph Inlines
                // the markdown rendering will handle the rest
                MainParagraph.Inlines.Add(new System.Windows.Documents.Run(text));

                // scroll to end: bring last block into view
                try
                {
                    var last = OutputViewer.Document?.Blocks.LastBlock;
                    last?.BringIntoView();
                }
                catch
                {
                }
            });
        }

        public void SetFullText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                MainParagraph.Inlines.Clear();
                MainParagraph.Inlines.Add(new System.Windows.Documents.Run(text));
            });
        }

        // Apply theme by strict lowercase string: "dark" or "light"
        public void ApplyTheme(string theme)
        {
            if (theme != "dark" && theme != "light")
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (theme == "light")
                    {
                        Background = Brushes.White;
                        if (OutputViewer?.Document != null)
                        {
                            OutputViewer.Document.Foreground = Brushes.Black;
                            OutputViewer.Document.Background = Brushes.White;
                        }
                    }
                    else // "dark"
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
                        if (OutputViewer?.Document != null)
                        {
                            OutputViewer.Document.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
                            OutputViewer.Document.Background = Brushes.Transparent;
                        }
                    }
                }
                catch
                {
                    // best-effort, swallow errors
                }
            });
        }
    }
}