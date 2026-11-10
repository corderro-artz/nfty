using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The GITHUB row on the Settings card's PUBLISHING page: the account line, the sign-in code, and the height it must not outgrow.</summary>
public class SettingsGitHubTests
{
    private sealed class FakeAccount : IGitHubAccount
    {
        public TaskCompletionSource Approved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Login { get; set; }
        public bool IsConnected => Login is not null;
        public string? UnavailableReason { get; set; }
        public Exception? Fail { get; set; }
        public int Disconnects { get; private set; }
        public event EventHandler? Changed;

        public Task<DeviceCode> BeginAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceCode("dev", "ABCD-1234", new Uri("https://github.com/login/device"),
                TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(5)));

        public async Task ConnectAsync(DeviceCode code, CancellationToken cancellationToken)
        {
            await Approved.Task.WaitAsync(cancellationToken);
            if (Fail is not null) throw Fail;
            Login = "octo";
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Disconnect() { Disconnects++; Login = null; Changed?.Invoke(this, EventArgs.Empty); }
        public GitHubShareClient? Client() => null;
    }

    private static SettingsDialogViewModel Card(IGitHubAccount account, IUrlOpener? opener = null,
        IClipboardService? clipboard = null) =>
        new(new FakeDialogs(), new ParallelismSettingService(StateStore.InMemory(), () => null),
            github: account, opener: opener, clipboard: clipboard) { Tab = SettingsTab.Publishing };

    [AvaloniaFact]
    public void Settings_shows_the_login_not_the_token()
    {
        var store = StateStore.InMemory();
        var secrets = new InMemorySecretStore();
        secrets.Write(GitHubAccountService.Target, "gho_do-not-show-me");
        SettingsJson.Set(store, GitHubAccountService.LoginKey, "octo");
        var account = new GitHubAccountService(store, secrets, CommunityEndpoints.Default with { ClientId = "x" });

        var vm = Card(account);

        Assert.Equal("Connected as @octo", vm.GitHubLine);
        Assert.True(vm.IsGitHubConnected);
        Assert.False(vm.ConnectGitHubCommand.CanExecute(null));
        Assert.True(vm.DisconnectGitHubCommand.CanExecute(null));
        var text = string.Join("|", vm.GetType().GetProperties()
            .Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => p.GetValue(vm)?.ToString() ?? ""));
        Assert.DoesNotContain("gho_do-not-show-me", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void An_unavailable_store_disables_connect_with_its_reason()
    {
        var account = new GitHubAccountService(StateStore.InMemory(),
            new UnavailableSecretStore("Secure storage is unavailable: secret-tool was not found."),
            CommunityEndpoints.Default with { ClientId = "x" });

        var vm = Card(account);

        Assert.False(vm.CanConnectGitHub);
        Assert.False(vm.ConnectGitHubCommand.CanExecute(null));
        Assert.Contains("secret-tool was not found", vm.GitHubNote, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_build_without_an_oauth_app_says_sharing_is_not_set_up()
    {
        var vm = Card(new GitHubAccountService(StateStore.InMemory(), new InMemorySecretStore()));
        Assert.False(vm.ConnectGitHubCommand.CanExecute(null));
        Assert.Equal("GitHub sharing isn't set up in this build.", vm.GitHubNote);
    }

    [AvaloniaFact]
    public async Task Connecting_shows_the_code_and_copy_and_open_use_it_until_approved()
    {
        var account = new FakeAccount();
        var opener = new NoopUrlOpener();
        var clipboard = new NoopClipboardService();
        var vm = Card(account, opener, clipboard);
        Assert.Equal(0, vm.CodeOpacity);

        vm.ConnectGitHubCommand.Execute(null);
        await Waiting(vm);

        Assert.Equal("ABCD-1234", vm.UserCode);
        Assert.Equal(1, vm.CodeOpacity);
        Assert.False(vm.ConnectGitHubCommand.CanExecute(null));
        await vm.CopyCodeCommand.ExecuteAsync(null);
        vm.OpenGitHubCommand.Execute(null);
        Assert.Equal("ABCD-1234", clipboard.Last);
        Assert.Equal(new Uri("https://github.com/login/device"), opener.Last);

        account.Approved.SetResult();
        await vm.PendingGitHub;

        Assert.Equal("Connected as @octo", vm.GitHubLine);
        Assert.Equal("", vm.UserCode);
        Assert.Equal(0, vm.CodeOpacity);
        Assert.False(vm.CopyCodeCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Cancel_stops_waiting_and_connects_nothing()
    {
        var account = new FakeAccount();
        var vm = Card(account);
        vm.ConnectGitHubCommand.Execute(null);
        await Waiting(vm);

        vm.CancelGitHubCommand.Execute(null);
        await vm.PendingGitHub;

        Assert.False(vm.IsGitHubWaiting);
        Assert.False(account.IsConnected);
        Assert.Equal(0, vm.CodeOpacity);
        Assert.True(vm.ConnectGitHubCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task A_sign_in_that_fails_says_so_in_product_words()
    {
        var account = new FakeAccount { Fail = new GitHubAuthException(ShareRules.SignInExpired) };
        var vm = Card(account);
        vm.ConnectGitHubCommand.Execute(null);
        await Waiting(vm);

        account.Approved.SetResult();
        await vm.PendingGitHub;

        Assert.Equal(ShareRules.SignInExpired, vm.GitHubNote);
        Assert.False(vm.IsGitHubConnected);
    }

    [AvaloniaFact]
    public void Disconnect_forgets_the_account()
    {
        var account = new FakeAccount { Login = "octo" };
        var vm = Card(account);
        vm.DisconnectGitHubCommand.Execute(null);
        Assert.Equal(1, account.Disconnects);
        Assert.Equal("not connected", vm.GitHubLine);
    }

    private static async Task Waiting(SettingsDialogViewModel vm)
    {
        for (int i = 0; i < 200 && !vm.IsGitHubWaiting; i++) await Task.Delay(5);
        Assert.True(vm.IsGitHubWaiting);
    }

    // ------------------------------------------------------------------ layout

    [AvaloniaFact]
    public void Settings_page_still_fits_its_reserved_height()
    {
        var account = new FakeAccount { Login = null };
        var store = StateStore.InMemory();
        var vm = new SettingsDialogViewModel(new FakeDialogs(), new ParallelismSettingService(store, () => "2"),
            new AppSettingsService(store), null,
            new PublishingSettingsService(store, new UnavailableSecretStore(
                "Secure storage is unavailable: secret-tool was not found. Install libsecret-tools."),
                (_, _, _) => new ExportUploadTests.FakePinning(false)),
            github: account) { Tab = SettingsTab.Publishing };
        var view = new Views.SettingsDialogView { DataContext = vm };
        double modal = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale;
        var window = new Window { Content = view, Width = ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale, Height = modal };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var pages = view.GetVisualDescendants().OfType<Panel>().First(p => p.Classes.Contains("settingspages"));
            var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
            double idle = card.Bounds.Height;

            // The waiting state: the code line shows, the note changes. Nothing may move.
            account.UnavailableReason = null;
            vm.UserCode = "ABCD-1234";
            vm.IsGitHubWaiting = true;
            Dispatcher.UIThread.RunJobs();

            var page = pages.Children.OfType<Control>().Single(c => c.IsVisible);
            page.Measure(new Size(pages.Bounds.Width, double.PositiveInfinity));
            Assert.Equal(idle, card.Bounds.Height, 0.5);
            Assert.True(page.DesiredSize.Height <= pages.Bounds.Height + 0.5,
                $"PUBLISHING wants {page.DesiredSize.Height:0} of {pages.Bounds.Height:0}");
            Assert.True(card.Bounds.Height <= modal, $"card {card.Bounds.Height:0} of modal area {modal:0}");
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<ScrollBar>(), s => s.IsVisible);
            // The reserved height is the page's own, to the pixel: the constant in Styles.axaml names it.
            Assert.Equal(Reserved, pages.Bounds.Height, 0.5);
            Assert.Equal(Reserved, page.DesiredSize.Height, 1.5);
        }
        finally { window.Close(); }
    }

    /// <summary>The PUBLISHING page's height, which <c>Panel.settingspages</c> reserves for every page.</summary>
    private const double Reserved = 336;
}
