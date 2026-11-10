using System.Globalization;

namespace Nfty.Core.Publish.Community;

/// <summary>The community gallery's limits and every sentence that refuses a share or a download, so each surface says the same thing.</summary>
public static class ShareRules
{
    /// <summary>Largest canvas side, in pixels.</summary>
    public const int MaxCanvas = 512;
    /// <summary>Most assets in a shared Set.</summary>
    public const int MaxAssets = 1000;
    /// <summary>Most collections one account shares.</summary>
    public const int MaxShares = 3;
    /// <summary>Longest share name, in characters.</summary>
    public const int MaxNameLength = 64;
    /// <summary>Longest description, in characters.</summary>
    public const int MaxDescriptionLength = 500;
    /// <summary>Most tags on a share.</summary>
    public const int MaxTags = 5;
    /// <summary>Most entries in a Set's archive.</summary>
    public const int MaxZipEntries = 6000;
    /// <summary>Largest allowed expansion of one archive entry (uncompressed to compressed).</summary>
    public const int MaxEntryRatio = 100;
    /// <summary>Longest preview picture side, in pixels.</summary>
    public const int MaxPreviewSide = 2048;
    /// <summary>Largest repository history, in KB as GitHub reports it.</summary>
    public const int MaxHistoryKb = 5120;
    /// <summary>Youngest GitHub account that may share, in days.</summary>
    public const int MinAccountAgeDays = 30;
    /// <summary>Largest packed <c>.set</c>, in bytes (200 MB).</summary>
    public const long MaxSetBytes = 209_715_200;
    /// <summary>Most a Set's archive may expand to, in bytes (1 GiB).</summary>
    public const long MaxUncompressedBytes = 1_073_741_824;
    /// <summary>Largest preview picture, in bytes (2 MB).</summary>
    public const long MaxPreviewBytes = 2_097_152;

    private static string N(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

    // Megabytes as SizeText computes them, but a whole number prints whole ("214 MB", not "214.0 MB").
    private static string Size(long bytes) => bytes >= 1024L * 1024
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / 1024:0.#} MB")
        : SizeText.Of(bytes);

    /// <summary>The canvas is past the limit.</summary>
    public static string CanvasTooLarge(int width, int height) =>
        $"The canvas is {N(width)} x {N(height)}; the gallery takes up to {MaxCanvas} x {MaxCanvas}.";

    /// <summary>The Set has too many assets.</summary>
    public static string TooManyAssets(int count) =>
        $"This Set has {N(count)} assets; the gallery takes up to {N(MaxAssets)}.";

    /// <summary>The packed Set is too big; names the 3D models' share when leaving them out would help.</summary>
    public static string SetTooLarge(long bytes, long modelsBytes)
    {
        var head = $"{Size(bytes)} — the gallery takes up to {Size(MaxSetBytes)}.";
        return modelsBytes > 0
            ? $"{head} Leave out 3D models (−{Size(modelsBytes)}) or share fewer assets."
            : $"{head} Share fewer assets.";
    }

    /// <summary>A sealed Set.</summary>
    public static string Sealed => "Sealed Sets can't be shared or come from the gallery.";

    /// <summary>The file is not a Set.</summary>
    public static string NotASet => "This file isn't a Set nfty can open.";

    /// <summary>The archive is unsafe to open.</summary>
    public static string UnsafeArchive(string reason) => $"This Set's archive is unsafe to open: {reason}.";

    /// <summary>Reason: too many entries.</summary>
    public const string ReasonTooManyFiles = "too many files";
    /// <summary>Reason: expands past the limits.</summary>
    public const string ReasonExpandsTooFar = "it expands too far";
    /// <summary>Reason: an entry name escapes the archive.</summary>
    public const string ReasonNameLeaves = "a file name leaves the archive";
    /// <summary>Reason: two entries share one name.</summary>
    public const string ReasonDuplicateName = "a file name appears twice";
    /// <summary>Reason: a link entry.</summary>
    public const string ReasonHoldsLink = "it holds a link";

    /// <summary>The file was modified while it was being checked.</summary>
    public static string FileChanged => "The file changed while it was being checked.";

    /// <summary>An entry no Set holds.</summary>
    public static string UnexpectedFile(string path) => $"'{path}' isn't part of a Set.";

    /// <summary>The CookBook inside is not the one that made the Set.</summary>
    public static string ForeignBook => "The CookBook inside isn't the one that made this Set.";

    /// <summary>A marketplace export with renamed files.</summary>
    public static string RenamedExport => "This is a marketplace export with renamed files, not a Set.";

    /// <summary>An asset picture can't be read.</summary>
    public static string ImageUnreadable(string file) => $"The picture '{file}' is missing or can't be read.";

    /// <summary>An asset picture is not the canvas size.</summary>
    public static string ImageWrongSize(string file) => $"The picture '{file}' isn't the size of the canvas.";

    /// <summary>The Set's asset count disagrees with its files.</summary>
    public static string CountMismatch => "The number of assets doesn't match the pictures and metadata in this Set.";

    /// <summary>The canvas disagrees with the pictures.</summary>
    public static string CanvasMismatch => "The canvas size doesn't match the size of the pictures in this Set.";

    /// <summary>The name is empty or too long.</summary>
    public static string NameLength => $"A name takes 1 to {MaxNameLength} characters.";

    /// <summary>The description is too long.</summary>
    public static string DescriptionLength => $"A description takes up to {MaxDescriptionLength} characters.";

    /// <summary>A link in a name or description.</summary>
    public static string NoLinks => "Names and descriptions can't contain links.";

    /// <summary>Too many tags.</summary>
    public static string TooManyTags => $"A share takes up to {MaxTags} tags.";

    /// <summary>A tag outside the list.</summary>
    public static string UnknownTag(string tag) => $"'{tag}' isn't one of the gallery's tags.";

    /// <summary>The preview picture is unusable.</summary>
    public static string PreviewBad => "The preview picture is missing, too big or unreadable.";

    /// <summary>A download that doesn't match its listed hash.</summary>
    public static string HashMismatch => "The download doesn't match what the gallery lists, so it was not opened.";

    /// <summary>Someone already shares this Set.</summary>
    public static string AlreadyListed(string login) => $"This Set is already in the gallery, shared by @{login}.";

    /// <summary>The user already shares this Set.</summary>
    public static string AlreadySharedAs(string name) => $"You already share this Set as \"{name}\".";

    /// <summary>The share would change nothing.</summary>
    public static string NothingChanged(string name, DateTimeOffset date) =>
        $"Nothing changed since you shared \"{name}\" on {date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}.";

    /// <summary>An update needs the replace box ticked.</summary>
    public static string ReplaceNotChosen => "Tick Replace the shared version to update it.";

    /// <summary>The user is at the share limit.</summary>
    public static string AtShareLimit => "You share 3 collections, the most the gallery takes. Pick one to replace.";

    /// <summary>The slug is taken by a different collection of the user's.</summary>
    public static string SlugTaken(string name) =>
        $"You already share a different collection as \"{name}\". Rename this one to share it too.";

    /// <summary>A repository named nfty-public that nfty did not make.</summary>
    public static string RepoNotOurs =>
        "You already have a repository called nfty-public that nfty didn't make. Rename or remove it on GitHub, then share again.";

    /// <summary>The nfty-public repository is private.</summary>
    public static string RepoPrivate => "Your nfty-public repository is private; the gallery reads only public ones.";

    /// <summary>The repository holds things the gallery doesn't allow.</summary>
    public static string RepoExtras => "Your nfty-public holds things the gallery doesn't allow. Remove them to share.";

    /// <summary>A submodule sits in nfty-public; nfty can't delete it.</summary>
    public static string RepoSubmodule(string path) => $"'{path}' is a submodule; remove it on GitHub, then share again.";

    /// <summary>Two releases carry one tag the repository lists.</summary>
    public static string RepoDuplicateRelease(string tag) =>
        $"Your nfty-public has two releases for \"{tag}\". Remove one on GitHub, then share again.";

    /// <summary>The repository changed between the scan and the removal.</summary>
    public static string RepoChanged => "Your nfty-public changed. Check it again.";

    /// <summary>nfty-public.json was written by a newer nfty.</summary>
    public static string RepoNewerNfty => "Your nfty-public was written by a newer nfty. Update nfty to share.";

    /// <summary>The repository history is past the limit.</summary>
    public static string HistoryTooLarge(long kb) =>
        $"Your nfty-public's history holds about {N((kb + 512) / 1024)} MB of deleted files. Delete the repository on GitHub and share again; nfty will make a fresh one.";

    /// <summary>The GitHub token is gone or refused.</summary>
    public static string Reconnect => "Reconnect GitHub to share.";

    /// <summary>GitHub can't be reached; a cached list is shown.</summary>
    public static string Offline(DateTime time) =>
        $"GitHub can't be reached. Showing the list from {time.ToString("HH:mm", CultureInfo.InvariantCulture)}.";

    /// <summary>GitHub is limiting requests; says how long to wait.</summary>
    public static string RateLimited(int minutes) =>
        $"GitHub is limiting requests. Try again in {minutes} {(minutes == 1 ? "minute" : "minutes")}.";

    /// <summary>GitHub answered no to a request.</summary>
    public static string GitHubRefused => "GitHub refused the request.";

    /// <summary>GitHub can't be reached.</summary>
    public static string Unreachable => "GitHub can't be reached. Check your connection and try again.";

    /// <summary>A gallery entry's link is not an https address nfty will fetch.</summary>
    public static string LinkNotOpened => "This gallery entry's link isn't one nfty opens.";

    /// <summary>The download arrived but could not be written to this machine.</summary>
    public static string SaveFailed => "nfty couldn't save the download. Check free disk space and try again.";

    /// <summary>GitHub sent a bigger answer than any real one.</summary>
    public static string GitHubTooMuch => "GitHub sent more than expected.";

    /// <summary>The file being uploaded could not be read.</summary>
    public static string UploadSourceFailed => "The file couldn't be read while uploading.";

    /// <summary>GitHub did not answer in time.</summary>
    public static string TimedOut => "GitHub didn't answer in time. Try again.";

    /// <summary>GitHub's answer was not what nfty expects.</summary>
    public static string GitHubUnexpected => "GitHub's answer wasn't what nfty expected. Try again.";

    /// <summary>The user refused the GitHub sign-in.</summary>
    public static string SignInCanceled => "GitHub sign-in was canceled.";

    /// <summary>The sign-in code ran out before it was used.</summary>
    public static string SignInExpired => "The sign-in code expired. Try again.";

    /// <summary>GitHub answered the sign-in in a way nfty doesn't expect.</summary>
    public static string SignInFailed => "GitHub sign-in failed. Try again.";

    /// <summary>The index is newer than this build understands.</summary>
    public static string IndexTooNew => "The community gallery needs a newer nfty. Update to see the latest list.";

    /// <summary>The entry's dates disagree with its history.</summary>
    public static string DatesMismatch => "The share's dates don't match its history.";

    /// <summary>A pull request touches files other than its author's own entries.</summary>
    public static string EntryFilesNotAllowed => "A share can only add, update or remove your own entries in the gallery.";

    /// <summary>An entry file the gallery's checker can't read.</summary>
    public static string EntryUnreadable => "The share's entry file isn't one the gallery can read.";

    /// <summary>An entry's slug disagrees with its file name.</summary>
    public static string EntryNameMismatch => "The share's name doesn't match its file name in the gallery.";

    /// <summary>The GitHub account is too young to share.</summary>
    public static string AccountTooYoung =>
        $"Your GitHub account is under {MinAccountAgeDays} days old; the gallery takes shares from older accounts.";

    /// <summary>The account or Set is on the gallery's block list.</summary>
    public static string Blocked => "The gallery can't list this share.";

    /// <summary>A second new share inside a day.</summary>
    public static string OneNewShareADay => "The gallery takes one new share a day. Try again tomorrow.";

    /// <summary>The entry's repository is not the author's nfty-public.</summary>
    public static string RepoNotYours => "The share's repository isn't your nfty-public.";

    /// <summary>The repository's nfty-public.json does not list the share as submitted.</summary>
    public static string ManifestMismatch => "Your nfty-public.json doesn't list this share as it was submitted.";

    /// <summary>A file the entry points at is not attached to the release.</summary>
    public static string ReleaseFileMissing(string file) => $"'{file}' is missing from the share's release.";

    /// <summary>The entry's facts differ from what the Set says about itself.</summary>
    public static string EntryDetailsMismatch => "The share's details don't match its Set.";

    /// <summary>The list or file the gallery fetched is not readable.</summary>
    public static string ListDamaged => "The community gallery list is damaged.";

    /// <summary>The list is older than this nfty reads.</summary>
    public static string ListTooOld(int version) =>
        $"The community gallery list is in an old format (version {version}) this nfty no longer reads.";
}
