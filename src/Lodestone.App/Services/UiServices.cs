using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Lodestone.App.Services;

/// <summary>Marshals work onto the WPF UI thread.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

/// <summary>Default <see cref="IUiDispatcher"/> backed by the WPF <see cref="Dispatcher"/>; runs the
/// action inline when already on the UI thread, otherwise marshals onto it.</summary>
public sealed class UiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = System.Windows.Application.Current.Dispatcher;

    public void Post(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Invoke(action);
        }
    }
}

/// <summary>Native dialogs and shell integration kept behind an interface so view models stay testable.</summary>
public interface IDialogService
{
    string? PickFolder(string? initialDirectory);

    void OpenUrl(string url);

    /// <summary>Opens a location in the OS file browser. An existing file is shown with its containing
    /// folder open and the file selected; anything else is treated as a folder and opened (created first
    /// if missing, so there's always something to show).</summary>
    void RevealInExplorer(string path);

    /// <summary>Modal yes/no confirmation; returns true only when the user explicitly confirms. Set
    /// <paramref name="warning"/> to false for benign prompts (e.g. an available update) so the dialog
    /// shows an information icon instead of a warning triangle.</summary>
    bool Confirm(string title, string message, bool warning = true);
}

/// <summary>Default <see cref="IDialogService"/> using native Win32 dialogs and the OS shell to open URLs.</summary>
public sealed class DialogService : IDialogService
{
    public string? PickFolder(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select your Minecraft folder",
            InitialDirectory = initialDirectory ?? string.Empty,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            MessageBox.Show($"Couldn't open the link:\n{url}", "Lodestone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                // Open the containing folder with the file highlighted (Windows shell convention).
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                Directory.CreateDirectory(path); // ensure the folder exists so there's always something to show
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception)
        {
            MessageBox.Show($"Couldn't open the location:\n{path}", "Lodestone", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public bool Confirm(string title, string message, bool warning = true)
        => MessageBox.Show(message, title, MessageBoxButton.YesNo,
            warning ? MessageBoxImage.Warning : MessageBoxImage.Information) == MessageBoxResult.Yes;
}
