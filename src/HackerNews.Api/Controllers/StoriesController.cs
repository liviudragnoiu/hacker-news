using System.ComponentModel.DataAnnotations;
using hacker_news.Models;
using hacker_news.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace hacker_news.Controllers;

[ApiController]
[Route("api/stories")]
public sealed class StoriesController(
    HackerNewsService hackerNewsService,
    IOptions<HackerNewsOptions> options) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StoryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<StoryResponse>>> GetBestStories(
        [FromQuery, Range(1, 500)] int n,
        CancellationToken cancellationToken)
    {
        if (n > options.Value.MaxStories)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid story count",
                Detail = $"The requested count must be between 1 and {options.Value.MaxStories}."
            });
        }

        var stories = await hackerNewsService.GetBestStoriesAsync(n, cancellationToken);
        return Ok(stories);
    }
}