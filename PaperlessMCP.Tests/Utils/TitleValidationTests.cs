using FluentAssertions;
using PaperlessMCP.Utils;
using Xunit;

namespace PaperlessMCP.Tests.Utils;

public class TitleValidationTests
{
    private static string TitleOfLength(int length) => new('A', length);

    [Fact]
    public void MaxTitleLength_Is127_NotTheModels128()
    {
        // Paperless' Django model declares max_length=128, but the consumer stores
        // title[:127]. 127 is the only length that survives every write path intact.
        TitleValidation.MaxTitleLength.Should().Be(127);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(126)]
    [InlineData(127)]
    public void IsValid_AtOrBelowLimit_ReturnsTrue(int length)
    {
        var valid = TitleValidation.IsValid(TitleOfLength(length), out var error);

        valid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(140)]
    [InlineData(500)]
    public void IsValid_AboveLimit_ReturnsFalse(int length)
    {
        var valid = TitleValidation.IsValid(TitleOfLength(length), out var error);

        valid.Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    public void IsValid_At128_IsRejected_BecauseTheConsumerTruncatesTo127()
    {
        // Regression guard for the off-by-one that makes this bug so easy to miss:
        // a 128-character title passes Paperless' own update validator and is still
        // truncated to 127 on upload.
        TitleValidation.IsValid(TitleOfLength(128), out _).Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithNullTitle_ReturnsTrue()
    {
        // null means "no title supplied"; Paperless derives one from the filename.
        var valid = TitleValidation.IsValid(null, out var error);

        valid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void IsValid_WhenRejecting_MessageNamesLimitAndActualLength()
    {
        TitleValidation.IsValid(TitleOfLength(140), out var error);

        error.Should().Contain("140");
        error.Should().Contain("127");
    }

    // --- Non-BMP boundary: lengths are Unicode code points, not UTF-16 code units. ---
    // Paperless truncates with a Python slice (title[:127]) and Python counts code
    // points; astral-plane characters are 2 UTF-16 units but 1 code point.

    private static string EmojiOfCount(int count) => string.Concat(Enumerable.Repeat("\U0001F600", count));

    [Fact]
    public void IsValid_CountsCodePoints_NotUtf16Units()
    {
        // 64 astral emoji: 128 UTF-16 units, 64 code points. Paperless stores this
        // intact ([:127] keeps all 64), so it must validate as intact here.
        var title = EmojiOfCount(64);
        title.Length.Should().Be(128, "precondition: astral emoji take two UTF-16 units each");

        var valid = TitleValidation.IsValid(title, out var error);

        valid.Should().BeTrue("64 code points are well under the 127 limit");
        error.Should().BeNull();
    }

    [Fact]
    public void IsValid_With127AstralRunes_ReturnsTrue()
    {
        TitleValidation.IsValid(EmojiOfCount(127), out var error).Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void IsValid_With128AstralRunes_ReturnsFalse_AndReportsRuneCount()
    {
        var title = EmojiOfCount(128);
        title.Length.Should().Be(256, "precondition: astral emoji take two UTF-16 units each");

        var valid = TitleValidation.IsValid(title, out var error);

        valid.Should().BeFalse("128 code points exceed the 127 limit");
        error.Should().Contain("128", "the message must report the code-point count");
        error.Should().NotContain("256", "the UTF-16 unit count would mislead the caller");
    }

    [Fact]
    public void IsValid_MixedAsciiAndAstral_AtLimit_ReturnsTrue()
    {
        // 126 ASCII + 1 astral emoji = 127 code points (128 UTF-16 units).
        var title = TitleOfLength(126) + "\U0001F600";
        title.Length.Should().Be(128, "precondition");

        TitleValidation.IsValid(title, out var error).Should().BeTrue();
        error.Should().BeNull();
    }

    // --- Filename fallback: when no title is supplied, Paperless stores
    // Path(filename).stem, and pathlib does not agree with .NET about what a stem is. ---

    [Theory]
    [InlineData("report.pdf", "report")]
    [InlineData("archive.tar.gz", "archive.tar")]
    [InlineData("no_extension", "no_extension")]
    [InlineData("scans/2026/report.pdf", "report")]
    [InlineData("scans/2026/", "2026")]
    [InlineData("", "")]
    public void GetFallbackTitle_ForOrdinaryNames_ReturnsTheStem(string fileName, string expected)
    {
        TitleValidation.GetFallbackTitle(fileName).Should().Be(expected);
    }

    [Theory]
    [InlineData(".bashrc", ".bashrc")]
    [InlineData(".hidden", ".hidden")]
    [InlineData("..foo", ".")]
    [InlineData("report.", "report.")]
    [InlineData("scans/.bashrc", ".bashrc")]
    public void GetFallbackTitle_WherePathlibDisagreesWithDotNet_FollowsPathlib(
        string fileName, string expected)
    {
        // Paperless runs Python: a dot is an extension separator only when it is neither
        // the first nor the last character of the name.
        TitleValidation.GetFallbackTitle(fileName).Should().Be(expected);
    }

    [Fact]
    public void GetFallbackTitle_ForDotfile_DiffersFromGetFileNameWithoutExtension()
    {
        // The divergence this helper exists for, pinned so nobody "simplifies" it back.
        Path.GetFileNameWithoutExtension(".bashrc").Should().BeEmpty(
            "this is exactly the .NET behaviour that hid over-long dotfile titles");
        TitleValidation.GetFallbackTitle(".bashrc").Should().Be(".bashrc");
    }

    [Fact]
    public void GetFallbackTitle_OfOverlongDotfile_IsRejectedByIsValid()
    {
        // A dotfile with no second dot: pathlib keeps the whole name as the stem, so
        // Paperless would truncate it to 127 without telling anyone.
        var fileName = "." + TitleOfLength(MaxLength + 1);

        var fallback = TitleValidation.GetFallbackTitle(fileName);

        fallback.Should().Be(fileName, "a dotfile with no other dot has no suffix");
        TitleValidation.IsValid(fallback, out var error).Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    public void GetFallbackTitle_OfNameEndingInDot_KeepsTheDot_AndCrossesTheLimit()
    {
        // 127 characters plus a trailing dot: .NET drops the dot and reports a valid
        // 127-character stem, but pathlib keeps it and Paperless stores 128 -> truncated.
        var fileName = TitleOfLength(MaxLength) + ".";

        Path.GetFileNameWithoutExtension(fileName).Should().HaveLength(MaxLength,
            "precondition: .NET would consider this name safe");

        var fallback = TitleValidation.GetFallbackTitle(fileName);

        fallback.Should().HaveLength(MaxLength + 1);
        TitleValidation.IsValid(fallback, out _).Should().BeFalse();
    }

    [Fact]
    public void GetFallbackTitle_WithNull_ReturnsEmpty()
    {
        TitleValidation.GetFallbackTitle(null).Should().BeEmpty();
    }

    private const int MaxLength = TitleValidation.MaxTitleLength;
}
