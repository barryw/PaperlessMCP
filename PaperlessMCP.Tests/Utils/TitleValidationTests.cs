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
}
