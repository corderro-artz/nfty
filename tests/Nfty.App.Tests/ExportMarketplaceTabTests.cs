using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The export card's MARKETPLACE tab: the values map onto <see cref="ExportOptions"/>, each field
/// says why it is wrong, the manifest comes from the plan, and the page fits like its siblings.
/// </summary>
public class ExportMarketplaceTabTests
{
    private static string DemoSet()
    {
        string root = Directory.CreateTempSubdirectory("nfty-mkt-").FullName;
        using var book = CookBookArchive.Read(DemoCookBook.WriteTo(root));
        string dir = Path.Combine(root, "Chest Demo");
        using var set = Generator.Generate(book, new GenerateOptions(30, "market"));
        SetWriter.Write(set, dir, pack: false);
        return dir;
    }

    private static ExportDialogViewModel Dialog() =>
        new(DemoSet(), new FilePickerService(), new NoopFolderRevealer(), new FakeDialogs());

    [Fact]
    public void The_values_become_options_only_where_something_would_read_them()
    {
        var vm = Dialog();
        vm.ImageBaseUri = " ipfs://CID/ ";
        vm.TokenIdNames = true;
        vm.StartAtZero = true;
        vm.ExternalUrlBase = "https://x.example/t/";
        vm.IncludeCollectionMetadata = true;
        vm.ExternalLink = "https://x.example/";
        vm.Royalty = "2.5";
        vm.FeeRecipient = "0x1234567890abcdef1234567890abcdef12345678";

        var o = vm.Options;
        Assert.Equal("ipfs://CID/", o.ImageBaseUri);
        Assert.Equal(TokenNaming.TokenId, o.TokenNaming);
        Assert.Equal(0, o.TokenStart);
        Assert.Equal("https://x.example/t/", o.ExternalUrlBase);
        Assert.Equal("https://x.example/", o.ExternalLink);
        Assert.Equal(2.5, o.RoyaltyPercent);
        Assert.True(o.IncludeCollectionMetadata);

        // Turn collection.json off: its fields stop being sent, whatever is left typed in them.
        vm.IncludeCollectionMetadata = false;
        Assert.Null(vm.Options.ExternalLink);
        Assert.Null(vm.Options.RoyaltyPercent);
        Assert.Null(vm.Options.FeeRecipient);
        Assert.False(vm.CollectionFieldsEnabled);

        // And with no metadata and no collection.json, a base URI has nothing to rewrite.
        vm.OpenSeaMetadata = false;
        Assert.Null(vm.Options.ImageBaseUri);
        Assert.Null(vm.Options.ExternalUrlBase);
    }

    [Fact]
    public void Each_field_says_why_it_is_wrong_in_the_cores_own_words()
    {
        var vm = Dialog();
        vm.IncludeCollectionMetadata = true;
        vm.ImageBaseUri = "ipfs://CID";
        vm.ExternalUrlBase = "site";
        vm.ExternalLink = "site";
        vm.Royalty = "2.555";
        vm.FeeRecipient = "0x12";

        Assert.Equal(MarketplaceRules.BaseUriProblem("ipfs://CID", "Image base URI"), vm.ImageBaseUriProblem);
        Assert.Contains("must end with '/'", vm.ImageBaseUriProblem);
        Assert.Contains("External URL", vm.ExternalUrlProblem);
        Assert.Contains("External link", vm.ExternalLinkProblem);
        Assert.Contains("2 decimals", vm.RoyaltyProblem);
        Assert.Contains("40 hexadecimal", vm.FeeRecipientProblem);

        // The footer states the first of them, without the framework's "(Parameter ...)" tail, and
        // Export is refused.
        Assert.Equal(vm.ImageBaseUriProblem, vm.Problem);
        Assert.False(vm.ExportCommand.CanExecute(null));

        vm.ImageBaseUri = "ipfs://CID/";
        vm.ExternalUrlBase = "https://x.example/";
        vm.ExternalLink = "https://x.example/";
        vm.Royalty = "5";
        vm.FeeRecipient = "0x1234567890abcdef1234567890abcdef12345678";
        Assert.Equal("", vm.Problem);
        Assert.True(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void A_royalty_without_a_recipient_is_refused_by_the_core_and_shown()
    {
        var vm = Dialog();
        vm.IncludeCollectionMetadata = true;
        vm.Royalty = "5";
        Assert.Contains("fee recipient", vm.Problem);
    }

    [Fact]
    public void A_value_in_a_field_nothing_reads_does_not_block_the_export()
    {
        var vm = Dialog();
        vm.IncludeCollectionMetadata = false;
        vm.Royalty = "banana";
        vm.FeeRecipient = "nope";
        Assert.Equal("", vm.RoyaltyProblem);
        Assert.Equal("", vm.Problem);
    }

    [Fact]
    public void The_marketplace_preset_turns_collection_json_on_and_set_json_off_and_keeps_the_typed_values()
    {
        var vm = Dialog();
        vm.ImageBaseUri = "ipfs://CID/";
        vm.ApplyPresetCommand.Execute(vm.Presets.Single(t => t.Preset == ExportPreset.Marketplace));

        Assert.True(vm.IncludeCollectionMetadata);
        Assert.False(vm.IncludeSetManifest);
        Assert.Equal("ipfs://CID/", vm.ImageBaseUri);
        Assert.True(vm.Presets.Single(t => t.Preset == ExportPreset.Marketplace).IsSelected);

        vm.ApplyPresetCommand.Execute(vm.Presets.Single(t => t.Preset == ExportPreset.AssetPack));
        Assert.False(vm.IncludeCollectionMetadata);
        Assert.True(vm.IncludeSetManifest);
    }

    [Fact]
    public void The_manifest_lists_what_the_marketplace_options_add_and_leave_out()
    {
        var vm = Dialog();
        vm.ApplyPresetCommand.Execute(vm.Presets.Single(t => t.Preset == ExportPreset.Marketplace));

        Assert.Contains("collection.json", vm.Parts);
        Assert.DoesNotContain("set.json", vm.Parts);

        // The plan's own warning, in the pinned block; gone once a base URI is set.
        Assert.Contains(vm.Consequences, c => c.Contains("relative", StringComparison.Ordinal));
        vm.ImageBaseUri = "ipfs://CID/";
        Assert.DoesNotContain(vm.Consequences, c => c.Contains("relative", StringComparison.Ordinal));
    }

    [Fact]
    public void Art_only_is_step_one_and_the_model_address_follows_the_models()
    {
        var vm = Dialog();
        vm.ApplyPresetCommand.Execute(vm.Presets.Single(t => t.Preset == ExportPreset.Marketplace));
        vm.ImageBaseUri = "ipfs://CID/";
        vm.ModelBaseUri = "ipfs://MODELS";

        // The demo Set has no models: that address has nothing to name, so it is neither sent nor judged.
        Assert.False(vm.ModelBaseUriEnabled);
        Assert.Equal("", vm.ModelBaseUriProblem);
        Assert.Null(vm.Options.ModelBaseUri);

        vm.ArtOnlyCommand.Execute(null);
        var o = vm.Options;
        Assert.True(o.Images);
        Assert.False(o.OpenSeaMetadata);
        Assert.False(o.NftyMetadata);
        Assert.False(o.IncludeCollectionMetadata);
        Assert.False(o.IncludeSetManifest);
        Assert.Null(o.ImageBaseUri);   // nothing carries an address yet: this is the export that gets one
        Assert.Contains(vm.Parts, p => p.StartsWith("images/", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.Parts, p => p.StartsWith("metadata/", StringComparison.Ordinal));
    }

    [Fact]
    public void Dropping_the_art_warns_that_the_links_dangle()
    {
        var vm = Dialog();
        vm.ImageBaseUri = "ipfs://CID/";
        vm.Images = false;
        Assert.Contains(vm.Consequences, c => c.Contains("dangle", StringComparison.Ordinal));
    }

    [Fact]
    public void Sealing_without_the_set_manifest_is_refused_in_the_cores_words()
    {
        var vm = Dialog();
        vm.IncludeSetManifest = false;
        vm.IsSealed = true;
        Assert.Contains("set.json", vm.Problem);
    }

    [Fact]
    public void The_tab_is_a_real_page()
    {
        var vm = Dialog();
        vm.ShowTabCommand.Execute(ExportTab.Marketplace);
        Assert.True(vm.IsMarketplaceTab);
        Assert.False(vm.IsContentsTab);
    }

    [AvaloniaFact]
    public void The_page_fits_the_smallest_window_without_scrolling_and_shows_each_reason()
    {
        var vm = Dialog();
        vm.Tab = ExportTab.Marketplace;
        vm.IncludeCollectionMetadata = true;
        vm.ImageBaseUri = "ipfs://CID";
        vm.Royalty = "x";
        var view = new Views.ExportDialogView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroller.Extent.Height <= scroller.Viewport.Height + 0.5,
                $"the marketplace page needs {scroller.Extent.Height:0} of {scroller.Viewport.Height:0}");

            // The reasons are drawn: a TextBlock carrying each message is on screen.
            var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(texts, t => t is not null && t.Contains("must end with '/'", StringComparison.Ordinal));
            Assert.Contains(texts, t => t is not null && t.Contains("is not a number", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_set_browser_rail_shows_color_traits_and_None()
    {
        string dir = DemoSet();
        using var set = SetReader.Read(dir);
        var browser = new SetBrowserViewModel(set);
        browser.SelectedItem = browser.Items[0];

        var rows = browser.ShownRarity;
        Assert.Contains(rows, r => r.Trait_type.EndsWith(" Color", StringComparison.Ordinal));

        // An item of the demo whose optional glow layer was left out reads "None" in the rail.
        var none = browser.Items.First(i => i.Item.Rarity.Any(r => r.Trait_type == "Glow" && r.Value == MetadataTraits.Absent));
        browser.SelectedItem = none;
        Assert.Contains(browser.ShownRarity, r => r.Trait_type == "Glow" && r.Value == MetadataTraits.Absent);
    }
}
