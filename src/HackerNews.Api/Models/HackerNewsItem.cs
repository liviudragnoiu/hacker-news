using System.Text.Json.Serialization;

namespace hacker_news.Models;

public sealed class HackerNewsItem
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("by")]
    public string? By { get; set; }

    [JsonPropertyName("time")]
    public long? Time { get; set; }

    [JsonPropertyName("score")]
    public int? Score { get; set; }

    [JsonPropertyName("descendants")]
    public int? Descendants { get; set; }
}