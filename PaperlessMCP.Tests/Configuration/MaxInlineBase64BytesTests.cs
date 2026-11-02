using FluentAssertions;
using PaperlessMCP.Configuration;
using PaperlessMCP.Tests.Fixtures;
using Xunit;

namespace PaperlessMCP.Tests.Configuration;

public class MaxInlineBase64BytesTests
{
    [Fact]
    public void Default_IsTwelveKilobytes()
    {
        PaperlessOptions.DefaultMaxInlineBase64Bytes.Should().Be(12 * 1024);
    }

    [Fact]
    public void Option_DefaultsToTwelveKilobytes_WhenNotSet()
    {
        new PaperlessOptions().MaxInlineBase64Bytes
            .Should().Be(PaperlessOptions.DefaultMaxInlineBase64Bytes);
    }

    [Fact]
    public void Client_ReturnsConfiguredValue_WhenPositive()
    {
        using var factory = new MockHttpClientFactory();
        factory.Options.MaxInlineBase64Bytes = 2 * 1024 * 1024;
        factory.Client.MaxInlineBase64Bytes.Should().Be(2 * 1024 * 1024);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Client_FallsBackToDefault_WhenNotPositive(int configured)
    {
        using var factory = new MockHttpClientFactory();
        factory.Options.MaxInlineBase64Bytes = configured;
        factory.Client.MaxInlineBase64Bytes.Should().Be(PaperlessOptions.DefaultMaxInlineBase64Bytes);
    }
}
