using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSerf.Lighthouse.Data;
using Testcontainers.PostgreSql;

namespace NSerf.Lighthouse.Client.Tests.Integration;

/// <summary>
/// Base class for integration tests that spin up a real Lighthouse server with PostgreSQL
/// and test the client against it
/// </summary>
public class LighthouseIntegrationTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    protected WebApplicationFactory<Program> ServerFactory = null!;
    protected HttpClient ServerHttpClient = null!;
    protected ILighthouseClient LighthouseClient = null!;
    protected LighthouseClientOptions ClientOptions = null!;
    
    protected Guid TestClusterId;
    protected byte[] TestPublicKey = null!;
    protected byte[] TestPrivateKey = null!;
    protected byte[] TestAesKey = null!;

    protected LighthouseIntegrationTestBase()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("lighthouse_integration_test")
            .WithUsername("test")
            .WithPassword("test")
            .WithCleanUp(true)
            .Build();
    }

    public async Task InitializeAsync()
    {
        // Start PostgreSQL container
        await _postgresContainer.StartAsync();
        var connectionString = _postgresContainer.GetConnectionString();

        // Generate test cryptographic keys
        TestClusterId = Guid.NewGuid();
        TestAesKey = new byte[32];
        RandomNumberGenerator.Fill(TestAesKey);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TestPrivateKey = ecdsa.ExportPkcs8PrivateKey();
        TestPublicKey = ecdsa.ExportSubjectPublicKeyInfo();

        // Create server factory with test database
        ServerFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        ["ConnectionStrings:DefaultConnection"] = connectionString,
                        ["SkipMigrations"] = "true",
                        ["RateLimiting:Disabled"] = "true"
                    }!);
                });

                builder.ConfigureTestServices(services =>
                {
                    // Remove existing DbContext registration
                    var descriptor = services.SingleOrDefault(d => 
                        d.ServiceType == typeof(DbContextOptions<LighthouseDbContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Add test database context
                    services.AddDbContext<LighthouseDbContext>(options =>
                        options.UseNpgsql(connectionString));

                    // Build service provider and ensure schema is created
                    var serviceProvider = services.BuildServiceProvider();
                    using var scope = serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<LighthouseDbContext>();
                    dbContext.Database.EnsureCreated();
                });
            });

        ServerHttpClient = ServerFactory.CreateClient();

        // Configure and create Lighthouse client
        var baseUrl = ServerHttpClient.BaseAddress!.ToString().TrimEnd('/');
        
        ClientOptions = new LighthouseClientOptions
        {
            BaseUrl = baseUrl,
            ClusterId = TestClusterId.ToString(),
            PrivateKey = Convert.ToBase64String(TestPrivateKey),
            AesKey = Convert.ToBase64String(TestAesKey),
            TimeoutSeconds = 30
        };

        // Create client manually with test server HttpClient
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<LighthouseClient>();
        
        var options = Microsoft.Extensions.Options.Options.Create(ClientOptions);
        LighthouseClient = new LighthouseClient(ServerHttpClient, options, logger);
    }

    public async Task DisposeAsync()
    {
        ServerHttpClient.Dispose();
        await ServerFactory.DisposeAsync();
        await _postgresContainer.DisposeAsync();
    }

    protected async Task<LighthouseDbContext> GetDbContextAsync()
    {
        var scope = ServerFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LighthouseDbContext>();
        return await Task.FromResult(context);
    }

    protected async Task CleanDatabaseAsync()
    {
        await using var context = await GetDbContextAsync();
        context.Nodes.RemoveRange(context.Nodes);
        context.Clusters.RemoveRange(context.Clusters);
        await context.SaveChangesAsync();
    }
}
