using hacker_news.Models;
using hacker_news.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache();
builder.Services.AddOptions<HackerNewsOptions>()
    .BindConfiguration("HackerNews");
builder.Services.AddHttpClient("HackerNews", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["HackerNews:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<HackerNewsService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
