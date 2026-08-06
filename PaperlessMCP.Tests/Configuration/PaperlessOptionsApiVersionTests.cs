using FluentAssertions;
using PaperlessMCP.Configuration;
using PaperlessMCP.Tests.Fixtures;
using Xunit;

namespace PaperlessMCP.Tests.Configuration;

public class PaperlessOptionsApiVersionTests
{
    [Fact]
    public void ApiVersion_IsPaperlessNgxDefaultApiVersion10()
    {
        PaperlessOptions.ApiVersion.Should().Be("10");
        PaperlessOptions.ApiAcceptHeader.Should().Be("application/json; version=10");
    }

    [Fact]
    public void MockHttpClient_UsesApiAcceptHeaderVersion10()
    {
        using var factory = new MockHttpClientFactory();
        factory.HttpClient.DefaultRequestHeaders.Accept.ToString()
            .Should().Be(PaperlessOptions.ApiAcceptHeader);
    }
}
