using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lodestone.App.ViewModels;

namespace Lodestone.App.Views;

/// <summary>Code-behind for the Home screen. Handles the drag-and-drop file install and the drop-zone
/// highlight (which need direct access to the visual tree); the rest lives in <see cref="HomeViewModel"/>.</summary>
public partial class HomeView : UserControl
{
    private static readonly Brush IdleBorder = new SolidColorBrush(Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF));
    private static readonly Brush IdleFill = new SolidColorBrush(Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF));

    public HomeView()
    {
        InitializeComponent();
    }

    private HomeViewModel? ViewModel => DataContext as HomeViewModel;

    private void OnDragEnter(object sender, DragEventArgs e) => SetHighlight(e, active: true);

    private void OnDragOver(object sender, DragEventArgs e) => SetHighlight(e, active: true);

    private void OnDragLeave(object sender, DragEventArgs e) => ResetHighlight();

    private void OnDrop(object sender, DragEventArgs e)
    {
        ResetHighlight();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && ViewModel is { } vm)
        {
            _ = vm.HandleFilesAsync(files);
        }
    }

    private void OnDropZoneClick(object sender, RoutedEventArgs e) => ViewModel?.BrowseFilesCommand.Execute(null);

    private void SetHighlight(DragEventArgs e, bool active)
    {
        bool hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (hasFiles && active)
        {
            // Pull the live accent brushes (mutated by AccentApplier) so the highlight follows the chosen accent.
            DropZone.BorderBrush = (Brush)FindResource("AccentBrush");
            DropZone.Background = (Brush)FindResource("AccentSoftBrush");
        }
    }

    private void ResetHighlight()
    {
        DropZone.BorderBrush = IdleBorder;
        DropZone.Background = IdleFill;
    }
}
