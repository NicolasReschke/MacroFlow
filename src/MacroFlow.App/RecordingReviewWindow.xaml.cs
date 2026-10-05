using System.Windows;
using MacroFlow.Core.Models;

namespace MacroFlow.App;

public enum RecordingReviewResult
{
    Discard,
    Append,
    Replace
}

public partial class RecordingReviewWindow : Window
{
    public RecordingReviewWindow(IReadOnlyList<MacroAction> actions)
    {
        InitializeComponent();
        ActionsGrid.ItemsSource = actions;
        SummaryText.Text = $"Se generaron {actions.Count} acciones a partir de tus entradas físicas.";
    }

    public RecordingReviewResult Result { get; private set; }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        Result = RecordingReviewResult.Discard;
        DialogResult = false;
    }

    private void Append_Click(object sender, RoutedEventArgs e)
    {
        Result = RecordingReviewResult.Append;
        DialogResult = true;
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        Result = RecordingReviewResult.Replace;
        DialogResult = true;
    }
}
