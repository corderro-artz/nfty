using System.Threading.Tasks;
using System.IO;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Desktop;
using Xunit;

namespace Nfty.App.Tests;

public partial class VisualCapture
{
    /// <summary>A marketplace upload bundle (token-id names, collection.json, no set.json) open in the
    /// viewer window's gallery, both themes: the "upload bundle, not a Set" mark, the collection
    /// facts, and a grid of the real demo art.</summary>
    [AvaloniaFact]
    public async Task Capture_marketplace_bundle_gallery()
    {
        if (Dir is null) return;

        string work = Directory.CreateTempSubdirectory("nfty-capture-bundle-").FullName;
        string book = DemoCookBook.WriteTo(work);
        string setDir = Path.Combine(work, "Chest Demo");
        using (var loaded = CookBookArchive.Read(book))
        {
            using var set = Generator.Generate(loaded, new GenerateOptions(24, "launch"));
            SetWriter.Write(set, setDir, pack: false);
        }
        string bundle = SetExporter.Export(setDir, Path.Combine(work, "upload"),
            ExportOptions.For(ExportPreset.Marketplace) with
            {
                Shape = ExportShape.Folder, BaseUri = "ipfs://bafybeigdyrzt5sfp7udm7hu76uh7y26nf3efuylqabf3oclgtqy55fbzdi/",
                TokenNaming = TokenNaming.TokenId, RoyaltyPercent = 5,
                FeeRecipient = "0x1234567890abcdef1234567890ABCDEF12345678",
                ExternalLink = "https://chests.example/",
            }).Path;

        foreach (var variant in Themes)
        {
            var subject = await ViewerOpener.OpenAsync(bundle, new ArchiveGalleryTests.Scripted());
            Assert.NotNull(subject);
            foreach (var tile in ((GallerySubject)subject!).Gallery.Items) tile.DecodeNow();

            var host = new DesktopViewerHost(new WindowPicker(), new NoopClipboardService(),
                new ViewerWindowStateService(StateStore.InMemory()), new ImageBridge(), () => null);
            ViewerWindow? window = null;
            host.Opened += opened => { window = opened; opened.RequestedThemeVariant = variant; opened.Width = 1280; opened.Height = 720; };
            host.Open(subject);
            try
            {
                Dispatcher.UIThread.RunJobs();
                window!.Width = 1280; window.Height = 720;
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()!.Save(Path.Combine(Dir!, $"bundle-gallery-{KeyOf(variant)}.png"),
                    PngBitmapEncoderOptions.Default);
            }
            finally { window?.Close(); }
        }
    }
}
