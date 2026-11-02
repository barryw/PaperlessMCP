using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PaperlessMCP.Client;
using PaperlessMCP.Configuration;
using RichardSzalay.MockHttp;
using Xunit;

namespace PaperlessMCP.Tests.Configuration;

public class MaxInlineBase64BytesTests
{
    private static PaperlessClient BuildClient(int maxInlineBase64Bytes)
    {
        var options = new PaperlessOptions
        {
            BaseUrl = "https://paperless.example.com",
            ApiToken = "test-token",
            MaxInlineBase64Bytes = maxInlineBase64Bytes
        };

        using var handler = new MockHttpMessageHandler();
        var httpClient = handler.ToHttpClient();
        httpClient.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");

        var optionsMock = Substitute.For<IOptions<PaperlessOptions>>();
        optionsMock.Value.Returns(options);

        var logger = Substitute.For<ILogger<PaperlessClient>>();
        return new PaperlessClient(httpClient, optionsMock, logger);
    }

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
        var client = BuildClient(2 * 1024 * 1024);
        client.MaxInlineBase64Bytes.Should().Be(2 * 1024 * 1024);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Client_FallsBackToDefault_WhenNotPositive(int configured)
    {
        var client = BuildClient(configured);
        client.MaxInlineBase64Bytes.Should().Be(PaperlessOptions.DefaultMaxInlineBase64Bytes);
    }
}
