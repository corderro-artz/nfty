namespace Nfty.Model.Tests;

public class ModelWriteFolderAuditTests
{
    [Theory]
    [InlineData("a.gltf")]
    [InlineData("a.bin")]
    public void A_failed_group_replacement_restores_every_existing_file(string lockedName)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows sharing-violation regression.");
        string root = Directory.CreateTempSubdirectory("nfty-write-model-pair-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.gltf"), [1, 2]);
            File.WriteAllBytes(Path.Combine(root, "a.bin"), [3, 4]);
            using (var held = new FileStream(Path.Combine(root, lockedName), FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.ThrowsAny<IOException>(() => ModelBatch.WriteFolder(root,
                    [new ModelFile("a.gltf", [5, 6]), new ModelFile("a.bin", [7, 8])]));
            Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(root, "a.gltf")));
            Assert.Equal(new byte[] { 3, 4 }, File.ReadAllBytes(Path.Combine(root, "a.bin")));
            Assert.Empty(Directory.GetDirectories(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_failed_install_removes_new_files_and_restores_old_files()
    {
        string root = Directory.CreateTempSubdirectory("nfty-write-model-block-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(root, "old.gltf"), [1, 2]);
            Directory.CreateDirectory(Path.Combine(root, "blocked.bin"));
            var error = Record.Exception(() => ModelBatch.WriteFolder(root,
                [new ModelFile("old.gltf", [5, 6]), new ModelFile("new.glb", [7, 8]), new ModelFile("blocked.bin", [9])]));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(root, "old.gltf")));
            Assert.False(File.Exists(Path.Combine(root, "new.glb")));
            Assert.Equal(["blocked.bin"], Directory.GetDirectories(root).Select(Path.GetFileName));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Secondary_restoration_failure_keeps_recoverable_original_payloads()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows sharing-violation regression.");
        string root = Directory.CreateTempSubdirectory("nfty-write-model-recovery-").FullName;
        try
        {
            string json = Path.Combine(root, "a.gltf"), bin = Path.Combine(root, "a.bin");
            File.WriteAllBytes(json, [1, 2]);
            File.WriteAllBytes(bin, [3, 4]);
            using (var files = new InterruptedFiles(json, bin))
            {
                var failure = Assert.Throws<IOException>(() => ModelBatch.WriteFolder(root, files));
                Assert.IsType<AggregateException>(failure.InnerException);
                string recovery = Assert.Single(Directory.GetDirectories(root),
                    p => Path.GetFileName(p).StartsWith(".nfty-model-", StringComparison.Ordinal));
                Assert.Contains(recovery, failure.Message, StringComparison.Ordinal);
                var originals = Directory.GetFiles(recovery).Select(File.ReadAllBytes).ToArray();
                Assert.Contains(originals, b => b.SequenceEqual(new byte[] { 1, 2 }));
                Assert.Contains(originals, b => b.SequenceEqual(new byte[] { 3, 4 }));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Folder_convenience_keeps_empty_nested_and_last_duplicate_behavior()
    {
        string root = Directory.CreateTempSubdirectory("nfty-write-model-inputs-").FullName;
        try
        {
            string output = Path.Combine(root, "empty");
            ModelBatch.WriteFolder(output, []);
            Assert.True(Directory.Exists(output));
            ModelBatch.WriteFolder(output,
                [new ModelFile("nested/a.glb", [1]), new ModelFile("nested/a.glb", [2])]);
            Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(output, "nested", "a.glb")));
            if (OperatingSystem.IsWindows())
            {
                ModelBatch.WriteFolder(output,
                    [new ModelFile("Capital.glb", [3]), new ModelFile("capital.glb", [4])]);
                Assert.Contains("Capital.glb", Directory.GetFiles(output).Select(Path.GetFileName));
                Assert.Equal(new byte[] { 4 }, File.ReadAllBytes(Path.Combine(output, "capital.glb")));
            }
            Assert.Empty(Directory.GetDirectories(output, ".nfty-model-*"));
            Assert.Throws<ArgumentException>(() => ModelBatch.WriteFolder(Path.Combine(root, "refused"),
                [new ModelFile("a.glb", [1]), new ModelFile("../escape.glb", [2])]));
            Assert.False(Directory.Exists(Path.Combine(root, "refused")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // Cause a competing filesystem change between installs without a timed watcher or production hook.
    private sealed class InterruptedFiles(string json, string bin) : IReadOnlyList<ModelFile>, IDisposable
    {
        private readonly ModelFile[] files = [new("a.gltf", [5, 6]), new("a.bin", [7, 8])];
        private FileStream? held;
        public int Count => files.Length;
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
        public IEnumerator<ModelFile> GetEnumerator() => ((IEnumerable<ModelFile>)files).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public void Dispose() => held?.Dispose();
    }
}
