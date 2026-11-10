using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nfty.App.ViewModels;

/// <summary>One clickable search term: a tray chip, a tile's owner or tag link, a rail link. Clicking toggles
/// <see cref="Token"/> in the search box, and <see cref="IsOn"/> says whether it is there now. It carries its own
/// command so it works inside a flyout, where a binding to the page's view model cannot reach.</summary>
public sealed partial class CommunityToken : ObservableObject
{
    private readonly Action<string> _toggle;

    /// <summary>Creates a term.</summary>
    /// <param name="label">The words on the button.</param>
    /// <param name="token">What it writes into the search box, such as <c>#props</c> or <c>@alice</c>.</param>
    /// <param name="toggle">What clicking does with the token.</param>
    public CommunityToken(string label, string token, Action<string> toggle)
    {
        Label = label;
        Token = token;
        _toggle = toggle;
    }

    /// <summary>The words on the button.</summary>
    public string Label { get; }

    /// <summary>What it writes into the search box.</summary>
    public string Token { get; }

    /// <summary>Whether the token is in the search box now.</summary>
    [ObservableProperty]
    private bool _isOn;

    /// <summary>Adds the token to the search box, or removes it when it is already there.</summary>
    [RelayCommand]
    private void Toggle() => _toggle(Token);
}
