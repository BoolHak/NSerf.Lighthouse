using AspNetCoreRateLimit;
using Microsoft.EntityFrameworkCore;
using NSerf.Lighthouse.Data;
using NSerf.Lighthouse.Repositories;
using NSerf.Lighthouse.Services;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("NSerf.Lighthouse.Tests")]

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "NSerf Lighthouse API",
        Version = "v1",
        Description = "Lighthouse server for NSerf cluster node discovery and coordination"
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<LighthouseDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IClusterRepository, PostgresClusterRepository>();
builder.Services.AddScoped<INodeRepository, PostgresNodeRepository>();

builder.Services.AddScoped<IClusterService, ClusterService>();
builder.Services.AddScoped<INodeDiscoveryService, NodeDiscoveryService>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<INonceValidationService, NonceValidationService>();
builder.Services.Configure<NonceValidationOptions>(
    builder.Configuration.GetSection(NonceValidationOptions.SectionName));

builder.Services.AddSingleton<NodeEvictionService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NodeEvictionService>());

builder.Services.Configure<NodeEvictionOptions>(options =>
{
    options.MaxNodesPerClusterVersion = builder.Configuration.GetValue("NodeEviction:MaxNodesPerClusterVersion", 5);
});

var rateLimitingEnabled = !builder.Configuration.GetValue("RateLimiting:Disabled", false);
if (rateLimitingEnabled)
{
    builder.Services.AddMemoryCache();
    builder.Services.Configure<IpRateLimitOptions>(options =>
    {
        options.EnableEndpointRateLimiting = true;
        options.StackBlockedRequests = false;
        options.HttpStatusCode = 429;
        options.RealIpHeader = "X-Real-IP";
        options.ClientIdHeader = "X-ClientId";
        options.GeneralRules = [
            new RateLimitRule
            {
                Endpoint = "*",
                Period = "1s",
                Limit = 100
            }
        ];
    });

    builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
    builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
    builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
    builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
}

var app = builder.Build();

if (!builder.Configuration.GetValue("SkipMigrations", false))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<LighthouseDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "NSerf Lighthouse API v1");
    options.RoutePrefix = string.Empty;
    options.DocumentTitle = "NSerf Lighthouse API";
});

if (rateLimitingEnabled)
{
    app.UseIpRateLimiting();
}

app.MapControllers();

app.Run();

public abstract partial class Program { }
