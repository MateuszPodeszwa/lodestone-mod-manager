using System.Diagnostics;
using System.IO;
using System.Windows;
using Lodestone.App.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Lodestone.App.Controls;

/// <summary>
/// Renders a mod description (Markdown + embedded HTML) with full website fidelity inside a hardened
/// WebView2. JavaScript is disabled and a strict CSP is applied (see <see cref="MarkdownDocument"/>),
/// and any link the user clicks opens in their default browser instead of navigating inside the app.
/// The browser process is created lazily — the first time the detail modal is shown — so it adds no
/// cost to app start-up.
/// </summary>
public sealed class MarkdownWebView : WebView2
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownWebView),
        new PropertyMetadata(null, OnMarkdownChanged));

    public string? Markdown
    {
        get => (string?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private bool _initStarted;
    private bool _ready;
    private bool _expectingInternalNavigation;

    public MarkdownWebView()
    {
        // Paint the modal colour from the first frame so there is no white flash before content loads.
        DefaultBackgroundColor = System.Drawing.Color.FromArgb(0xFF, 0x22, 0x22, 0x27);
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownWebView { _ready: true } view)
        {
            view.Render();
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Defer creating the browser process until the detail modal is actually opened.
        if (e.NewValue is true)
        {
            _ = InitializeAsync();
        }
    }

    private async Task InitializeAsync()
    {
        if (_initStarted)
        {
            return;
        }

        _initStarted = true;

        try
        {
            // Keep the user-data folder in LocalAppData so it survives Velopack updates and never
            // tries to write under a read-only install directory.
            string dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Lodestone",
                "WebView2");
            Directory.CreateDirectory(dataDir);

            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            // No WebView2 runtime (or it failed to start): leave the area blank — the modal still shows
            // the stats and the "Open on Modrinth" button. Allow a later attempt on the next open.
            _initStarted = false;
            Lodestone.Infrastructure.Persistence.LodestoneLog.Error("WebView2 initialisation failed", ex);
            return;
        }

        CoreWebView2Settings settings = CoreWebView2.Settings;
        settings.IsScriptEnabled = false;            // descriptions are untrusted; never run their script
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;

        CoreWebView2.NavigationStarting += OnNavigationStarting;
        CoreWebView2.NewWindowRequested += OnNewWindowRequested;

        _ready = true;
        Render();
    }

    private void Render()
    {
        if (!_ready || string.IsNullOrWhiteSpace(Markdown))
        {
            return;
        }

        _expectingInternalNavigation = true;
        // Render with the current accent so description links/quotes match the chosen accent. The document is
        // static HTML, so an open description won't recolour mid-view, but each newly opened one picks it up.
        CoreWebView2.NavigateToString(MarkdownDocument.ToHtml(Markdown, AccentApplier.CurrentAccentHex()));
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // The only navigation we start is the NavigateToString above; anything else is the user
        // clicking a link, which must leave the app rather than replace the description in-place.
        if (_expectingInternalNavigation)
        {
            _expectingInternalNavigation = false;
            return;
        }

        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    }

    private static void OpenExternal(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
        {
            return;
        }

        // Ignore in-page anchors and any unexpected scheme; only hand real web/mail links to the OS.
        if (parsed.Scheme != Uri.UriSchemeHttp &&
            parsed.Scheme != Uri.UriSchemeHttps &&
            parsed.Scheme != Uri.UriSchemeMailto)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Lodestone.Infrastructure.Persistence.LodestoneLog.Error("Failed to open external link", ex);
        }
    }
}
