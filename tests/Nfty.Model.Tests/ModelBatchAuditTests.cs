using Nfty.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Model.Tests;

/// <summary>Portable naming and whole-model folder replacement boundaries.</summary>
public class ModelBatchAuditTests
{
    [Theory]
    [InlineData("COM\u00b9", "COM\u00b9_")]
    [InlineData("LPT\u00b2", "LPT\u00b2_")]
    [InlineData("COM\u00b3.txt", "COM\u00b3_.txt")]
    public void Superscript_device_names_are_neutralized_without_device_io(string name, string expected) =>
        Assert.Equal(expected, ModelBatch.SafeBaseName(name));

    private static Image<Rgba32> Art(int width) => new(width, 1, new Rgba32(40, 80, 120, 255));
    private static RiseOptions Options => new() { Format = ModelFormat.GltfWithBin };

    private static void WriteOriginal(string folder)
    {
        using var art = Art(1);
        ModelBatch.WriteFolder(folder, ModelBatch.Build([new ModelInput("a", art, null)], Options).Files);
    }

    private static void Export(string folder, bool parallel, Action<string>? written = null)
    {
        ModelInput Open(int _) => new("a", Art(2), null);
        void Release(ModelInput input) => input.Art.Dispose();
        if (parallel)
            ModelBatch.Export(["a"], Open, Release, Options, ModelDestination.Folder(folder),
                new Parallelism(2), onWritten: written);
        else
            ModelBatch.Export(["a"], Open, Release, Options, ModelDestination.Folder(folder), onWritten: written);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_locked_existing_companion_preserves_the_entire_previous_model(bool parallel)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows sharing-violation regression.");
        string root = Directory.CreateTempSubdirectory("nfty-model-pair-audit-").FullName;
        WriteOriginal(root);
        string json = Path.Combine(root, "a.gltf"), bin = Path.Combine(root, "a.bin");
        byte[] beforeJson = File.ReadAllBytes(json), beforeBin = File.ReadAllBytes(bin);
        using (var held = new FileStream(bin, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => Export(root, parallel));
        Assert.Equal(beforeJson, File.ReadAllBytes(json));
        Assert.Equal(beforeBin, File.ReadAllBytes(bin));
        Assert.NotNull(GltfReader.Read(json));
        Assert.Equal(2, Directory.GetFileSystemEntries(root).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_directory_blocking_a_companion_does_not_replace_the_old_header(bool parallel)
    {
        string root = Directory.CreateTempSubdirectory("nfty-model-block-audit-").FullName;
        WriteOriginal(root);
        string json = Path.Combine(root, "a.gltf"), bin = Path.Combine(root, "a.bin");
        byte[] beforeJson = File.ReadAllBytes(json);
        File.Delete(bin);
        Directory.CreateDirectory(bin);
        string marker = Path.Combine(bin, "unrelated.txt");
        File.WriteAllText(marker, "keep");
        var error = Record.Exception(() => Export(root, parallel));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        Assert.Equal(beforeJson, File.ReadAllBytes(json));
        Assert.Equal("keep", File.ReadAllText(marker));
        Assert.Equal(2, Directory.GetFileSystemEntries(root).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void File_notifications_observe_a_complete_model_even_if_the_handler_throws(bool parallel)
    {
        string root = Directory.CreateTempSubdirectory("nfty-model-callback-audit-").FullName;
        WriteOriginal(root);
        var failure = new InvalidOperationException("notification failed");
        var actual = Assert.Throws<InvalidOperationException>(() => Export(root, parallel, _ => throw failure));
        Assert.Same(failure, actual);
        var model = GltfReader.Read(Path.Combine(root, "a.gltf"));
        Assert.Equal(2f, model.Mesh.Max.X - model.Mesh.Min.X);
        Assert.Equal(2, Directory.GetFileSystemEntries(root).Length);
    }

    [Fact]
    public void A_secondary_rollback_failure_retains_original_bytes_and_names_the_recovery_directory()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows sharing-violation regression.");
        string root = Directory.CreateTempSubdirectory("nfty-model-recovery-audit-").FullName;
        WriteOriginal(root);
        string json = Path.Combine(root, "a.gltf"), bin = Path.Combine(root, "a.bin");
        byte[] oldJson = File.ReadAllBytes(json), oldBin = File.ReadAllBytes(bin);
        using var files = new InterruptedFiles(json, bin,
            [new ModelFile("a.gltf", [7, 8, 9]), new ModelFile("a.bin", [1, 2, 3, 4])]);
        using var sink = ModelDestination.Folder(root).Open(new List<string>());
        var failure = Assert.Throws<IOException>(() => sink.Add(files));
        Assert.IsType<AggregateException>(failure.InnerException);
        string recovery = Assert.Single(Directory.GetDirectories(root), p => Path.GetFileName(p).StartsWith(".nfty-model-", StringComparison.Ordinal));
        Assert.Contains(recovery, failure.Message, StringComparison.Ordinal);
        var retained = Directory.GetFiles(recovery).Select(File.ReadAllBytes).ToList();
        Assert.Contains(retained, bytes => bytes.SequenceEqual(oldJson));
        Assert.Contains(retained, bytes => bytes.SequenceEqual(oldBin));
    }

    // Simulate a competing filesystem change precisely between the two installs. Unlike a watcher
    // or a timed thread, this does not depend on scheduling, and needs no production test hook.
    private sealed class InterruptedFiles(string json, string bin, IReadOnlyList<ModelFile> files)
        : IReadOnlyList<ModelFile>, IDisposable
    {
        private FileStream? held;
        public int Count => files.Count;
        public ModelFile this[int index]
        {
            get
            {
                if (index == 1 && held is null && File.Exists(json) && !File.Exists(bin))
                {
                    held = new FileStream(json, FileMode.Open, FileAccess.Read, FileShare.None);
                    Directory.CreateDirectory(bin);
                }
                return files[index];
            }
        }
        public IEnumerator<ModelFile> GetEnumerator() => files.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() => held?.Dispose();
    }
}
