using Pottmayer.UrlShortener.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddUrlShortenerObservability();

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseUrlShortenerObservability();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "gateway" }));

app.MapReverseProxy();

app.Run();
