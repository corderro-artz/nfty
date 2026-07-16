using Nfty.Core.Formats;

namespace Nfty.Core.Tests;

public class ArchiveKindTests
{
    [Theory]
    [InlineData("VaporPets.cbk", ArchiveKind.CookBook)]
    [InlineData("cat.rcp", ArchiveKind.Recipe)]
    [InlineData("bg.igt", ArchiveKind.Ingredient)]
    public void Kind_comes_from_the_extension(string path, ArchiveKind expected) =>
        Assert.Equal(expected, Archives.KindOf(path));

    [Fact]
    public void Extension_match_is_case_insensitive() =>
        Assert.Equal(ArchiveKind.CookBook, Archives.KindOf("/tmp/VaporPets.CBK"));

    [Fact]
    public void Unknown_extension_is_rejected_never_guessed()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Archives.KindOf("mystery.zip"));
        Assert.Contains(".cbk", ex.Message);
    }

    [Fact]
    public void Missing_extension_is_rejected() =>
        Assert.Throws<NotSupportedException>(() => Archives.KindOf("noextension"));
}
