using Apollarr.Common;
using Apollarr.Common.Middleware;
using Apollarr.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System.Net.Http;

// Load environment variables from .env file
DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Add environment variables to configuration
builder.Configuration.AddEnvironmentVariables();

// Configure settings with validation
builder.Services
    .AddOptions<AppSettings>()
    .Bind(builder.Configuration.GetSection(AppSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Override critical secrets/URLs from legacy env var names (SONARR_URL, etc.)
builder.Services.PostConfigure<AppSettings>(options =>
{
    options.Sonarr.Url = builder.Configuration["SONARR_URL"] ?? options.Sonarr.Url;
    options.Sonarr.ApiKey = builder.Configuration["SONARR_API_KEY"] ?? options.Sonarr.ApiKey;
    options.Radarr.Url = builder.Configuration["RADARR_URL"] ?? options.Radarr.Url;
    options.Radarr.ApiKey = builder.Configuration["RADARR_API_KEY"] ?? options.Radarr.ApiKey;
    options.Apollo.Username = builder.Configuration["APOLLO_USERNAME"] ?? options.Apollo.Username;
    options.Apollo.Password = builder.Configuration["APOLLO_PASSWORD"] ?? options.Apollo.Password;
});

// Configure Kestrel to listen on all interfaces (required for Docker)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080);
});

// Add HTTP logging
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestPropertiesAndHeaders
                            | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponsePropertiesAndHeaders;
    logging.RequestBodyLogLimit = 4096;
    logging.ResponseBodyLogLimit = 4096;
});

builder.Services.AddHttpContextAccessor();

// Register HttpClient with timeout and connection configuration
builder.Services.AddHttpClient<SonarrApiClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    // Increase connection pool size to handle more concurrent connections
    MaxConnectionsPerServer = 10,
    // Enable connection pooling
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
    // Enable keep-alive
    KeepAlivePingDelay = TimeSpan.FromSeconds(30),
    KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
    // DNS refresh
    EnableMultipleHttp2Connections = true
});

builder.Services.AddHttpClient<RadarrApiClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    // Increase connection pool size to handle more concurrent connections
    MaxConnectionsPerServer = 10,
    // Enable connection pooling
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
    // Enable keep-alive
    KeepAlivePingDelay = TimeSpan.FromSeconds(30),
    KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
    // DNS refresh
    EnableMultipleHttp2Connections = true
});

// Register application services
builder.Services.AddScoped<ISonarrService, SonarrService>();
builder.Services.AddScoped<IRadarrService, RadarrService>();
builder.Services.AddHttpClient<IStrmFileService, StrmFileService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped<ISonarrWebhookService, SonarrWebhookService>();
builder.Services.AddScoped<IRadarrWebhookService, RadarrWebhookService>();
builder.Services.AddSingleton<IFileSystemService, FileSystemService>();
builder.Services.AddProblemDetails();

// Register background services
builder.Services.AddHostedService<ValidationSchedulerService>();

// Add controllers and API documentation
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressInferBindingSourcesForParameters = true;
    options.InvalidModelStateResponseFactory = context =>
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problemDetails = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState);
        problemDetails.Status = StatusCodes.Status400BadRequest;
        problemDetails.Title = "Request validation failed";
        return new ObjectResult(problemDetails)
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
    };
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Enable HTTP request logging
app.UseHttpLogging();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
