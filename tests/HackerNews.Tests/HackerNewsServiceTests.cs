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
            [104] = """{"title":"Middle score","url":"https://example.com/middle","by":"user3","time":1700000003,"score":30,"descendants":3}"""
        };
        var requestCounts = new ConcurrentDictionary<string, int>();
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requestCounts.AddOrUpdate(path, 1, (_, count) => count + 1);

            var json = path.EndsWith("beststories.json", StringComparison.Ordinal)
                ? "[101,102,103,104]"
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

        Assert.Equal(new[] { "High score", "Middle score" }, topTwo.Select(story => story.Title));
        Assert.Equal(new[] { "High score", "Middle score", "Low score" }, topThree.Select(story => story.Title));
        Assert.Equal(1, requestCounts["/v0/beststories.json"]);
        Assert.Equal(4, requestCounts.Where(request => request.Key.Contains("/item/"))
            .Sum(request => request.Value));
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
}