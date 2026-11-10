using System.Globalization;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.Core.Tests.Community;

public class ShareRulesTests
{
    [Fact] public void Canvas_sentence_names_both_sizes() =>
        Assert.Equal("The canvas is 640 x 640; the gallery takes up to 512 x 512.", ShareRules.CanvasTooLarge(640, 640));

    [Fact] public void Size_sentence_names_the_models_fix() =>
        Assert.Equal("214 MB — the gallery takes up to 200 MB. Leave out 3D models (−160 MB) or share fewer assets.",
            ShareRules.SetTooLarge(224_395_264, modelsBytes: 167_772_160));

    [Fact] public void Size_sentence_has_no_models_clause_without_models() =>
        Assert.DoesNotContain("3D models", ShareRules.SetTooLarge(224_395_264, 0));

    [Fact] public void Asset_count_is_thousands_separated() =>
        Assert.Equal("This Set has 1,200 assets; the gallery takes up to 1,000.", ShareRules.TooManyAssets(1200));

    [Fact] public void Pinned_sentences_read_verbatim()
    {
        Assert.Equal("Sealed Sets can't be shared or come from the gallery.", ShareRules.Sealed);
        Assert.Equal("This file isn't a Set nfty can open.", ShareRules.NotASet);
        Assert.Equal("This Set's archive is unsafe to open: too many files.", ShareRules.UnsafeArchive(ShareRules.ReasonTooManyFiles));
        Assert.Equal("'a/b' isn't part of a Set.", ShareRules.UnexpectedFile("a/b"));
        Assert.Equal("This Set is already in the gallery, shared by @ann.", ShareRules.AlreadyListed("ann"));
        Assert.Equal("You already share this Set as \"Chests\".", ShareRules.AlreadySharedAs("Chests"));
        Assert.Equal("Reconnect GitHub to share.", ShareRules.Reconnect);
        Assert.Equal("The community gallery needs a newer nfty. Update to see the latest list.", ShareRules.IndexTooNew);
        Assert.Equal("You share 3 collections, the most the gallery takes. Pick one to replace.", ShareRules.AtShareLimit);
        Assert.Equal("Your nfty-public holds things the gallery doesn't allow. Remove them to share.", ShareRules.RepoExtras);
    }

    [Fact]
    public void Gallery_checker_sentences_read_verbatim()
    {
        Assert.Equal("A share can only add, update or remove your own entries in the gallery.", ShareRules.EntryFilesNotAllowed);
        Assert.Equal("The share's entry file isn't one the gallery can read.", ShareRules.EntryUnreadable);
        Assert.Equal("The share's name doesn't match its file name in the gallery.", ShareRules.EntryNameMismatch);
        Assert.Equal("Your GitHub account is under 30 days old; the gallery takes shares from older accounts.", ShareRules.AccountTooYoung);
        Assert.Equal("The gallery can't list this share.", ShareRules.Blocked);
        Assert.Equal("The gallery takes one new share a day. Try again tomorrow.", ShareRules.OneNewShareADay);
        Assert.Equal("The share's repository isn't your nfty-public.", ShareRules.RepoNotYours);
        Assert.Equal("Your nfty-public.json doesn't list this share as it was submitted.", ShareRules.ManifestMismatch);
        Assert.Equal("'cats.set' is missing from the share's release.", ShareRules.ReleaseFileMissing("cats.set"));
        Assert.Equal("The share's details don't match its Set.", ShareRules.EntryDetailsMismatch);
    }

    [Fact] public void History_sentence_names_megabytes() =>
        Assert.Contains("about 48 MB of deleted files", ShareRules.HistoryTooLarge(49_152));

    [Fact]
    public void Dates_and_times_are_invariant()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Nothing changed since you shared \"Chests\" on 9 Nov 2026.",
                ShareRules.NothingChanged("Chests", new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero)));
            Assert.Equal("GitHub can't be reached. Showing the list from 07:05.",
                ShareRules.Offline(new DateTime(2026, 11, 9, 7, 5, 0)));
            Assert.Equal("This Set has 1,200 assets; the gallery takes up to 1,000.", ShareRules.TooManyAssets(1200));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact] public void Limits_match_the_spec()
    {
        Assert.Equal(200L * 1024 * 1024, ShareRules.MaxSetBytes);
        Assert.Equal(1024L * 1024 * 1024, ShareRules.MaxUncompressedBytes);
        Assert.Equal(2L * 1024 * 1024, ShareRules.MaxPreviewBytes);
        Assert.Equal(5 * 1024, ShareRules.MaxHistoryKb);
    }
}
