using System.Collections.Concurrent;
using System.Net.Http.Json;
using hacker_news.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace hacker_news.Services;

public sealed class HackerNewsService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IOptions<HackerNewsOptions> options)
{
    private const string BestStoriesCacheKey = "hacker-news:best-stories";
    private readonly HackerNewsOptions _options = options.Value;
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("HackerNews");
    private readonly SemaphoreSlim _snapshotLock = new(1, 1);
    private readonly SemaphoreSlim _upstreamGate = new(Math.Max(1, options.Value.MaxConcurrentRequests));

    public async Task<IReadOnlyList<StoryResponse>> GetBestStoriesAsync(
        int count,
        CancellationToken cancellationToken)
    {
        var stories = await GetSnapshotAsync(cancellationToken);
        return stories.Take(count).ToArray();
    }

    private async Task<IReadOnlyList<StoryResponse>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(BestStoriesCacheKey, out IReadOnlyList<StoryResponse>? cachedStories))
        {
            return cachedStories!;
        }

        await _snapshotLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(BestStoriesCacheKey, out cachedStories))
            {
                return cachedStories!;
            }

            var storyIds = await GetBestStoryIdsAsync(cancellationToken);
            var stories = new ConcurrentBag<StoryResponse>();

            await Parallel.ForEachAsync(
                storyIds.Take(Math.Max(0, _options.MaxStories)),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, _options.MaxConcurrentRequests),
                    CancellationToken = cancellationToken
                },
                async (storyId, token) =>
                {
                    var item = await GetStoryAsync(storyId, token);
                    if (item?.Title is not null && item.By is not null && item.Time is not null)
                    {
                        stories.Add(new StoryResponse(
                            item.Title,
                            item.Url,
                            item.By,
                            DateTimeOffset.FromUnixTimeSeconds(item.Time.Value),
                            item.Score ?? 0,
                            item.Descendants ?? 0));
                    }
                });

            var rankedStories = stories
                .OrderByDescending(story => story.Score)
                .ThenByDescending(story => story.Time)
                .ToArray();

            cache.Set(
                BestStoriesCacheKey,
                rankedStories,
                TimeSpan.FromSeconds(Math.Max(1, _options.SnapshotCacheSeconds)));

            return rankedStories;
        }
        finally
        {
            _snapshotLock.Release();
        }
    }

    private async Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken)
    {
        var storyIds = await _httpClient.GetFromJsonAsync<List<int>>(
            "beststories.json",
            cancellationToken);

        return storyIds ?? [];
    }

    private async Task<HackerNewsItem?> GetStoryAsync(int storyId, CancellationToken cancellationToken)
    {
        var cacheKey = $"hacker-news:item:{storyId}";
        if (cache.TryGetValue(cacheKey, out HackerNewsItem? cachedStory))
        {
            return cachedStory;
        }

        await _upstreamGate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(cacheKey, out cachedStory))
            {
                return cachedStory;
            }

            var story = await _httpClient.GetFromJsonAsync<HackerNewsItem>(
                $"item/{storyId}.json",
                cancellationToken);

            if (story is not null)
            {
                cache.Set(
                    cacheKey,
                    story,
                    TimeSpan.FromSeconds(Math.Max(1, _options.StoryCacheSeconds)));
            }

            return story;
        }
        finally
        {
            _upstreamGate.Release();
        }
    }
}