using System.Collections.Concurrent;
using System.Net;
using System.Text;
using hacker_news.Models;
using hacker_news.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HackerNews.Tests;

public sealed class HackerNewsServiceTests
{
    [Fact]
    public async Task GetBestStoriesAsync_RanksStoriesSkipsDeletedItemsAndReusesSnapshot()
    {
        var itemJson = new Dictionary<int, string>
        {
            [101] = """{"title":"Low score","url":"https://example.com/low","by":"user1","time":1700000001,"score":10,"descendants":1}""",
            [102] = """{"title":"High score","url":"https://example.com/high","by":"user2","time":1700000002,"score":50,"descendants":5}""",
            [103] = "null",
            [104] = """{"title":"Older middle score","url":"https://example.com/middle","by":"user3","time":1700000003,"score":30,"descendants":3}""",
            [105] = """{"title":"Newer middle score","url":"https://example.com/newer","by":"user4","time":1700000004,"score":30,"descendants":4}"""
        };
        var requestCounts = new ConcurrentDictionary<string, int>();
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requestCounts.AddOrUpdate(path, 1, (_, count) => count + 1);

            var json = path.EndsWith("beststories.json", StringComparison.Ordinal)
                ? "[101,102,103,104,105]"
                : itemJson[int.Parse(Path.GetFileNameWithoutExtension(path))];

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://hacker-news.firebaseio.com/v0/")
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new HackerNewsService(
            new StubHttpClientFactory(httpClient),
            cache,
            Options.Create(new HackerNewsOptions()));

        var topTwo = await service.GetBestStoriesAsync(2, CancellationToken.None);
        var topThree = await service.GetBestStoriesAsync(3, CancellationToken.None);

        Assert.Equal(new[] { "High score", "Newer middle score" }, topTwo.Select(story => story.Title));
        Assert.Equal(
            new[] { "High score", "Newer middle score", "Older middle score" },
            topThree.Select(story => story.Title));
        Assert.Equal(1, requestCounts["/v0/beststories.json"]);
        Assert.Equal(5, requestCounts.Where(request => request.Key.Contains("/item/"))
            .Sum(request => request.Value));
    }

    [Fact]
    public async Task RefreshSnapshotAsync_UpdatesTheCachedStoryList()
    {
        var currentStoryId = 201;
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path.EndsWith("beststories.json", StringComparison.Ordinal)
                ? $"[{currentStoryId}]"
                : $"{{\"title\":\"Story {currentStoryId}\",\"by\":\"user\",\"time\":1700000000,\"score\":10}}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://hacker-news.firebaseio.com/v0/")
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new HackerNewsService(
            new StubHttpClientFactory(httpClient),
            cache,
            Options.Create(new HackerNewsOptions()));

        var original = await service.GetBestStoriesAsync(1, CancellationToken.None);
        currentStoryId = 202;
        await service.RefreshSnapshotAsync(CancellationToken.None);
        var refreshed = await service.GetBestStoriesAsync(1, CancellationToken.None);

        Assert.Equal("Story 201", original[0].Title);
        Assert.Equal("Story 202", refreshed[0].Title);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.OK, "{")]
    public async Task GetBestStoriesAsync_WhenUpstreamFails_ReturnsServiceUnavailableProblemDetails(
        HttpStatusCode statusCode,
        string body)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://hacker-news.firebaseio.com/v0/")
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new HackerNewsService(
            new StubHttpClientFactory(httpClient),
            cache,
            Options.Create(new HackerNewsOptions()));
        var controller = new hacker_news.Controllers.StoriesController(
            service,
            Options.Create(new HackerNewsOptions()));

        var action = await controller.GetBestStories(1, CancellationToken.None);

        var response = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(action.Result);
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(response.Value);
        Assert.Equal("Hacker News is temporarily unavailable.", problem.Title);
    }

    [Fact]
    public async Task GetBestStoriesAsync_WhenCallerCancels_PropagatesCancellation()
    {
        using var httpClient = new HttpClient(new CancellationHttpMessageHandler())
        {
            BaseAddress = new Uri("https://hacker-news.firebaseio.com/v0/")
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new HackerNewsService(
            new StubHttpClientFactory(httpClient),
            cache,
            Options.Create(new HackerNewsOptions()));
        using var cancellationTokenSource = new CancellationTokenSource();

        var task = service.GetBestStoriesAsync(1, cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task GetBestStories_WhenCountIsOutOfRange_ReturnsBadRequest(int count)
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException(
            "The upstream must not be called for an invalid count."));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://hacker-news.firebaseio.com/v0/")
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new HackerNewsService(
            new StubHttpClientFactory(httpClient),
            cache,
            Options.Create(new HackerNewsOptions()));
        var controller = new hacker_news.Controllers.StoriesController(
            service,
            Options.Create(new HackerNewsOptions()));

        var action = await controller.GetBestStories(count, CancellationToken.None);

        Assert.Equal((int)HttpStatusCode.BadRequest,
            Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ObjectResult>(action.Result).StatusCode);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class CancellationHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The request should have been cancelled.");
        }
    }
}