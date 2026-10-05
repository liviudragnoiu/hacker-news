using hacker_news.Models;
using Microsoft.Extensions.Options;

namespace hacker_news.Services;

public sealed class BestStoriesCacheRefreshService(
    HackerNewsService hackerNewsService,
    IOptions<HackerNewsOptions> options,
    ILogger<BestStoriesCacheRefreshService> logger) : BackgroundService
{
    private readonly int _refreshIntervalSeconds = Math.Clamp(
        options.Value.SnapshotRefreshIntervalSeconds,
        1,
        Math.Max(1, options.Value.SnapshotCacheSeconds - 1));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_refreshIntervalSeconds));

        try
        {
            do
            {
                try
                {
                    await hackerNewsService.RefreshSnapshotAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (HackerNewsUnavailableException exception)
                {
                    logger.LogWarning(exception, "Could not refresh the Hacker News stories cache.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}