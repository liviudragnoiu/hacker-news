namespace hacker_news.Models;

public sealed class HackerNewsOptions
{
    public string BaseUrl { get; set; } = "https://hacker-news.firebaseio.com/v0/";
    public int SnapshotCacheSeconds { get; set; } = 60;
    public int StoryCacheSeconds { get; set; } = 300;
    public int MaxConcurrentRequests { get; set; } = 8;
    public int MaxStories { get; set; } = 500;
}