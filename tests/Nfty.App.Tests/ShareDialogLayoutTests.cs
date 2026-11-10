using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The share card's geometry: it fits the area a modal really gets, and its one reserved block never changes size.</summary>
public class ShareDialogLayoutTests
{
    private static readonly string[] States = ["none", "extras", "duplicate", "already", "replace"];

    internal static async Task<(ShareDialogViewModel Vm, ShareDialogTests.Rig Rig)> StateAsync(string state, Action<ShareDialogTests.Rig>? edit = null)
    {
        var rig = new ShareDialogTests.Rig();
        switch (state)
        {
            case "none":
                rig.Repo.Snapshot = ShareFixtures.Repo([ShareFixtures.Mine("a")]);
                break;
            case "extras":
                rig.Repo.Snapshot = ShareFixtures.Repo([], (tree, branches, _, _) =>
                {
                    for (int i = 0; i < 9; i++) tree.Add(new GitHubTreeItem($"stray-{i}.bin", "blob", "s" + i, 1_000_000));
                    tree.Add(new GitHubTreeItem("vendor", "commit", "sv", null));
                    branches.Add("feature/old");
                });
                break;
            case "duplicate":
                rig.Cache.Cached = CommunityFixtures.Listing([
                    CommunityFixtures.Entry("theirs", "Theirs", "bob", ownerId: 99) with { Fingerprint = "fp-mine" }]);
                break;
            case "already":
                rig.Repo.Snapshot = ShareFixtures.Repo([ShareFixtures.Mine(ShareFixtures.Slug, "Cats", ShareFixtures.Collection)]);
                break;
            case "replace":
                rig.Repo.Snapshot = ShareFixtures.Repo([ShareFixtures.Mine("a", "Alpha"), ShareFixtures.Mine("b", "Beta"), ShareFixtures.Mine("c", "Gamma")]);
                break;
        }
        edit?.Invoke(rig);
        return (await rig.OpenAsync(), rig);
    }

    private static Window Show(ShareDialogViewModel vm, out Views.ShareDialogView view)
    {
        view = new Views.ShareDialogView { DataContext = vm };
        // The area a modal is really given at the smallest window, not the 1180 a mockup was drawn at.
        var window = new Window
        {
            Content = view,
            Width = ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaTheory]
    [InlineData("none")]
    [InlineData("extras")]
    [InlineData("duplicate")]
    [InlineData("already")]
    [InlineData("replace")]
    public async Task The_card_fits_the_modal_area_unscrolled(string state)
    {
        var (vm, rig) = await StateAsync(state);
        using var _ = rig; using var __ = vm;
        var window = Show(vm, out var view);
        try
        {
            var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
            Assert.True(card.Bounds.Height <= view.Bounds.Height + 0.5,
                $"the share card is {card.Bounds.Height:0} tall in {view.Bounds.Height:0} of modal area ({state})");
            Assert.True(card.Bounds.Width <= view.Bounds.Width + 0.5);

            // Its own content must fit too: a card clamped to the host while its body overflows is a card cut off.
            var body = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal-body"));
            var inner = (Control)body.Child!;
            Assert.True(inner.DesiredSize.Height <= body.Bounds.Height - body.Padding.Top - body.Padding.Bottom + 0.5,
                $"the share card's body wants {inner.DesiredSize.Height:0} of {body.Bounds.Height:0} ({state})");

            // Only the extras list may scroll: nothing else has more content than room.
            Assert.All(view.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.Name != "ExtrasScroll"),
                s => Assert.True(s.Extent.Height <= s.Viewport.Height + 0.5, $"a scroller holds {s.Extent.Height:0} in {s.Viewport.Height:0} ({state})"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Lower_block_height_is_the_same_in_every_state()
    {
        var blocks = new List<(string, double, double)>();
        foreach (var state in States)
        {
            var (vm, rig) = await StateAsync(state);
            using var _ = rig; using var __ = vm;
            var window = Show(vm, out var view);
            try
            {
                var block = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("shareblock"));
                var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
                blocks.Add((state, block.Bounds.Height, card.Bounds.Height));
            }
            finally { window.Close(); }
        }
        Assert.All(blocks, b => Assert.Equal(blocks[0].Item2, b.Item2));
        Assert.All(blocks, b => Assert.Equal(blocks[0].Item3, b.Item3));
    }

    [AvaloniaFact]
    public async Task The_extras_list_scrolls_rows_with_its_button_outside()
    {
        var (vm, rig) = await StateAsync("extras");
        using var _ = rig; using var __ = vm;
        var window = Show(vm, out var view);
        try
        {
            var scroll = view.FindControl<ScrollViewer>("ExtrasScroll")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "ten extras should not fit the block");
            var remove = view.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Remove these…");
            Assert.Null(remove.FindAncestorOfType<ScrollViewer>());
            Assert.True(remove.Bounds.Width > 0 && remove.Opacity > 0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Connect_and_the_chip_share_one_slot_and_swap_ink()
    {
        var (vm, rig) = await StateAsync("none");
        using var _ = rig; using var __ = vm;
        var window = Show(vm, out var view);
        try
        {
            var connect = view.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Connect GitHub…");
            Assert.True(connect.Opacity < 0.01 && !connect.IsHitTestVisible, "connected: the button is ink off");
            rig.Account.Login = null;
            rig.Account.Raise();
            Dispatcher.UIThread.RunJobs();
            Assert.True(connect.Opacity > 0.99 && connect.IsHitTestVisible, "signed out: the button is there, not hidden");
        }
        finally { window.Close(); }
    }
}
