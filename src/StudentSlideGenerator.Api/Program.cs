using Microsoft.AspNetCore.Http.Features;
using StudentSlideGenerator.Api.Services;

var builder = WebApplication.CreateBuilder(args);

const long maxRequestSize = 21 * 1024 * 1024;

builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxRequestSize;
});

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? ["http://localhost:5158", "https://localhost:7158"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("WebClient", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient<IDeckGenerationService, GeminiDeckGenerationService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromMinutes(3);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors("WebClient");
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    service = "StudentSlideGenerator.Api",
    status = "ok"
}));

app.Run();
