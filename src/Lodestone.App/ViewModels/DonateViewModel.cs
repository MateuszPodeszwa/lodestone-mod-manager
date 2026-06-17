using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lodestone.App.Services;
using Lodestone.Application.Messaging;
using Lodestone.Application.Supporter;
using Lodestone.Domain.Common;

namespace Lodestone.App.ViewModels;

/// <summary>
/// The Support screen. Promotes Patreon (the website mints codes for active, paying patrons) and
/// redeems the offline, time-limited code minted by the website. Flips to a thank-you state once a
/// code is active.
/// </summary>
public sealed partial class DonateViewModel : ObservableObject
{
    private const string PatreonUrl = "https://www.patreon.com/c/mateuszpodeszwa";
    private const string WebsiteUrl = "https://lodestonemc.net/supporter";                  // Patreon login + code generation
    private const string PrioritySupportUrl = "https://lodestonemc.net/support";

    private readonly SupporterService _supporter;
    private readonly IDialogService _dialog;
    private readonly IMessageBus _bus;
    private readonly IUiDispatcher _ui;

    public DonateViewModel(SupporterService supporter, IDialogService dialog, IMessageBus bus, IUiDispatcher ui)
    {
        _supporter = supporter;
        _dialog = dialog;
        _bus = bus;
        _ui = ui;

        // Keep the page in sync if status changes (e.g. revoked, or redeemed elsewhere).
        _supporter.Changed += (_, _) => _ui.Post(RefreshState);
    }

    [ObservableProperty] private string _redeemCode = string.Empty;

    public bool IsSupporter => _supporter.IsSupporter;

    public bool IsNotSupporter => !_supporter.IsSupporter;

    public string ThanksHeadline => $"Thank you, {_supporter.Holder ?? "supporter"} 💚";

    [RelayCommand] private void OpenPatreon() => _dialog.OpenUrl(PatreonUrl);

    [RelayCommand] private void OpenWebsite() => _dialog.OpenUrl(WebsiteUrl);

    [RelayCommand] private void OpenPrioritySupport() => _dialog.OpenUrl(PrioritySupportUrl);

    [RelayCommand]
    private async Task RedeemAsync()
    {
        if (string.IsNullOrWhiteSpace(RedeemCode))
        {
            return;
        }

        Result<SupporterEntitlement> result = await _supporter.RedeemAsync(RedeemCode.Trim()).ConfigureAwait(true);
        if (result.IsSuccess)
        {
            RedeemCode = string.Empty;
            _bus.Publish(new ToastMessage("Thank you 💚", "Your supporter perks are unlocked."));
            RefreshState();
        }
        else
        {
            _bus.Publish(new ToastMessage("Couldn't redeem that", result.Error.Message, ToastKind.Error));
        }
    }

    [RelayCommand]
    private async Task RemoveCodeAsync()
    {
        await _supporter.RevokeAsync().ConfigureAwait(true);
        _bus.Publish(new ToastMessage("Supporter code removed", "Paste a fresh code any time to unlock perks again."));
        RefreshState();
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsSupporter));
        OnPropertyChanged(nameof(IsNotSupporter));
        OnPropertyChanged(nameof(ThanksHeadline));
    }
}
