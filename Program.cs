using Apollarr.Common;
using Apollarr.Services;

// Load environment variables from .env file
DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Add environment variables to configuration
builder.Configuration.AddEnvironmentVariables();

// Configure settings
builder.Services.Configure<AppSettings>(options =>
{
    options.Sonarr.Url = builder.Configuration["SONARR_URL"] ?? string.Empty;
    options.Sonarr.ApiKey = builder.Configuration["SONARR_API_KEY"] ?? string.Empty;
    options.Apollo.Username = builder.Configuration["APOLLO_USERNAME"] ?? string.Empty;
    options.Apollo.Password = builder.Configuration["APOLLO_PASSWORD"] ?? string.Empty;
});

// Configure Kestrel to listen on all interfaces (required for Docker)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080);
});

// Add HTTP logging
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
    logging.RequestBodyLogLimit = 4096;
    logging.ResponseBodyLogLimit = 4096;
});

// Register HttpClient with timeout configuration
builder.Services.AddHttpClient<SonarrService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<StrmFileService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Register application services
builder.Services.AddScoped<SonarrService>();
builder.Services.AddScoped<StrmFileService>();
builder.Services.AddSingleton<IFileSystemService, FileSystemService>();

// Add controllers and API documentation
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Enable HTTP request logging
app.UseHttpLogging();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
