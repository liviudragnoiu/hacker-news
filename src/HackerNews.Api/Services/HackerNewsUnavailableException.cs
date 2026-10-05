namespace hacker_news.Services;

public sealed class HackerNewsUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);