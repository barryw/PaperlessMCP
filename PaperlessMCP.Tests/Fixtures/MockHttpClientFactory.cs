using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PaperlessMCP.Client;
using PaperlessMCP.Configuration;
using RichardSzalay.MockHttp;

namespace PaperlessMCP.Tests.Fixtures;

public class MockHttpClientFactory : IDisposable
{
    public MockHttpMessageHandler MockHandler { get; }
    public HttpClient HttpClient { get; }
    public PaperlessClient Client { get; }
    public PaperlessOptions Options { get; }

    public MockHttpClientFactory(string baseUrl = "https://paperless.example.com")
    {
        Options = new PaperlessOptions
        {
            BaseUrl = baseUrl,
            ApiToken = "test-token",
            MaxPageSize = 100
        };

        MockHandler = new MockHttpMessageHandler();
        HttpClient = MockHandler.ToHttpClient();
        HttpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        HttpClient.DefaultRequestHeaders.Add("Accept", PaperlessOptions.ApiAcceptHeader);

        var optionsMock = Substitute.For<IOptions<PaperlessOptions>>();
        optionsMock.Value.Returns(Options);

        var logger = Substitute.For<ILogger<PaperlessClient>>();

        Client = new PaperlessClient(HttpClient, optionsMock, logger);
    }

    public MockedRequest SetupGet(string url, string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return MockHandler
            .When(HttpMethod.Get, $"{Options.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}")
            .Respond("application/json", responseJson);
    }

    public MockedRequest SetupGetWithStatus(string url, HttpStatusCode statusCode)
    {
        return MockHandler
            .When(HttpMethod.Get, $"{Options.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}")
            .Respond(statusCode);
    }

    public MockedRequest SetupPost(string url, string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return MockHandler
            .When(HttpMethod.Post, $"{Options.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}")
            .Respond("application/json", responseJson);
    }

    public MockedRequest SetupPostWithStatus(string url, HttpStatusCode statusCode)
    {
        return MockHandler
            .When(HttpMethod.Post, $"{Options.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}")
            .Respond(statusCode);
    }

    public MockedRequest SetupPatch(string url, string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return MockHandler
            .When(HttpMethod.Patch, $"{Options.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}")
            .Respond("application/json", responseJson);
    }

    public MockedRequest SetupPatchWithStatus(string url, HttpStatusCode statusCode)
    {
        return MockHandler
            .When(HttpMethod.Patch, $"{Options.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}")
            .Respond(statusCode);
    }

    public MockedRequest SetupPatchWithError(string url, HttpStatusCode statusCode, string responseBody)
    {
        return MockHandler
            .When(HttpMethod.Patch, $"{Options.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}")
            .Respond(statusCode, "application/json", responseBody);
    }

    public MockedRequest SetupPostWithError(string url, HttpStatusCode statusCode, string responseBody)
    {
        return MockHandler
            .When(HttpMethod.Post, $"{Options.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}")
            .Respond(statusCode, "application/json", responseBody);
    }

    public MockedRequest SetupDelete(string url, HttpStatusCode statusCode = HttpStatusCode.NoContent)
    {
        return MockHandler
            .When(HttpMethod.Delete, $"{Options.BaseUrl.TrimEnd('/')}/{url.TrimStart('/')}")
            .Respond(statusCode);
    }

    public void Dispose()
    {
        HttpClient.Dispose();
        MockHandler.Dispose();
    }
}

public static class MockHttpExtensions
{
    public static MockedRequest RespondWithJson(this MockedRequest request, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return request.Respond(statusCode, "application/json", json);
    }
}
