using System.Text;
using System.Text.Json;
using Nfty.Core;
using Nfty.Core.Output;
using Nfty.Model;

namespace Nfty.App.ViewModels;

/// <summary>The dialog's destination policy; Core can still cook into a populated Kitchen.</summary>
internal static class CookOutputDirectory
{
    internal static string Slug(string name)
    {
        var result = new StringBuilder();
        bool separator = false;
        foreach (char c in WizardViewModelBase.DeriveId(name))
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (separator && result.Length > 0) result.Append('-');
                result.Append(c);
                separator = false;
            }
            else separator = true;
        }
        string slug = result.Length == 0 ? "cookbook" : result.ToString();
        // The model writer already owns the portable Windows-device-name rule. Prefixing rather
        // than taking its underscore suffix keeps this folder's spelling lowercase and hyphenated.
        return ModelBatch.SafeBaseName(slug) == slug ? slug : "set-" + slug;
    }

    internal static async Task ValidateAsync(string directory, CancellationToken ct, Parallelism parallelism)
    {
        ct.ThrowIfCancellationRequested();
        var attributes = Attributes(directory);
        if (attributes is null) return; // The writer creates it only after generation succeeds.
        RefuseLink(directory, attributes);
        if ((attributes & FileAttributes.Directory) == 0)
            throw new InvalidOperationException($"A file already uses the output path '{directory}'. Choose another parent folder or rename that file.");
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) return;
        if (!SetReader.IsSetFolder(directory)) throw Occupied(directory);

        // These paths are written during extend. Do not follow a linked child out of the selected
        // destination, even when its manifest otherwise describes a readable Set.
        foreach (string leaf in new[] { SetLayout.ManifestFile, SetLayout.ImagesDir,
                     SetLayout.MetadataDir, SetLayout.NftyDir, SetLayout.ModelsDir })
        {
            string path = Path.Combine(directory, leaf);
            RefuseLink(path, Attributes(path));
            if (Directory.Exists(path))
                foreach (string entry in Directory.EnumerateFileSystemEntries(path))
                {
                    ct.ThrowIfCancellationRequested();
                    RefuseLink(entry, Attributes(entry));
                }
        }
        try
        {
            // IsSetFolder is only a dispatch hint (set.json exists). Use the actual reader to
            // establish that this is a Set; do not invent a second manifest parser here.
            using var existing = await SetReader.ReadAsync(directory, ct, parallelism);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw Occupied(directory, ex);
        }
    }

    private static InvalidOperationException Occupied(string directory, Exception? inner = null) => new(
        $"The folder '{directory}' is not empty and is not a readable cooked Set. Choose an empty folder, "
        + "or turn on 'Name after CookBook' and choose its parent. To extend a Set, choose its existing folder.", inner);

    private static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static void RefuseLink(string path, FileAttributes? attributes)
    {
        if (attributes is { } actual && (actual & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"The output path '{path}' is a link. Choose a real output folder so the cook cannot change files elsewhere.");
    }
}
