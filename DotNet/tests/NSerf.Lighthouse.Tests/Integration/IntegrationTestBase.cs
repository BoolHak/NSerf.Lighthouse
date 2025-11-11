using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSerf.Lighthouse.Data;
using NSerf.Lighthouse.DTOs;
using Testcontainers.PostgreSql;

namespace NSerf.Lighthouse.Tests.Integration;

/// <summary>
/// Base class for integration tests using Testcontainers PostgreSQL
/// </summary>
public class IntegrationTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    protected WebApplicationFactory<Program> Factory = null!;
    protected HttpClient Client = null!;
    private string _connectionString = null!;

    protected IntegrationTestBase()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("lighthouse_test")
            .WithUsername("test")
            .WithPassword("test")
            .WithCleanUp(true)
            .Build();
    }

    public async ValueTask InitializeAsync()
    {
        // Start PostgreSQL container
        await _postgresContainer.StartAsync();
        _connectionString = _postgresContainer.GetConnectionString();

        // Create factory with a test database
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    // Override configuration with test settings
                    config.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _connectionString,
                        ["SkipMigrations"] = "true", // Skip automatic migrations in Program.cs
                        ["RateLimiting:Disabled"] = "true" // Disable rate limiting for tests
                    }!);
                });

                builder.ConfigureTestServices(services =>
                {
                    // Remove existing DbContext registration
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<LighthouseDbContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Add test database context
                    services.AddDbContext<LighthouseDbContext>(options =>
                        options.UseNpgsql(_connectionString));

                    // Build service provider and ensure schema is created
                    var serviceProvider = services.BuildServiceProvider();
                    using var scope = serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<LighthouseDbContext>();
                    dbContext.Database.EnsureCreated();
                });
            });

        Client = Factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await _postgresContainer.DisposeAsync();
    }

    protected async Task<LighthouseDbContext> GetDbContextAsync()
    {
        var scope = Factory.Services.CreateScope();
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

    protected async Task RegisterClusterAsync(Guid clusterId, byte[] publicKey)
    {
        var request = new RegisterClusterRequest
        {
            ClusterId = clusterId.ToString(),
            PublicKey = Convert.ToBase64String(publicKey)
        };

        var response = await Client.PostAsJsonAsync("/clusters", request);
        response.EnsureSuccessStatusCode();
    }
}
