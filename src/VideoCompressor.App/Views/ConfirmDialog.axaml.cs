using Avalonia.Controls;

namespace VideoCompressor.App.Views;

/// <summary>A minimal yes/no dialog: Avalonia has no built-in MessageBox (plan §3).</summary>
public partial class ConfirmDialog : Window
{
    // Required by the XAML loader and the designer.
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string title, string message, string yes, string no) : this()
    {
        Title = title;
        MessageText.Text = message;
        YesButton.Content = yes;
        NoButton.Content = no;
        YesButton.Click += (_, _) => Close(true);
        NoButton.Click += (_, _) => Close(false);
    }
}
