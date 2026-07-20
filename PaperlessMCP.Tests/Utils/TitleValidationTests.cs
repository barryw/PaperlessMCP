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
}
