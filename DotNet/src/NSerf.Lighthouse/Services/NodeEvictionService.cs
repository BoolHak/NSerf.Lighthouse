using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSerf.Lighthouse.Data;

namespace NSerf.Lighthouse.Services;

public class NodeEvictionService(
    IServiceProvider serviceProvider,
    ILogger<NodeEvictionService> logger,
    IOptions<NodeEvictionOptions> options)
    : BackgroundService
{
    private readonly Channel<EvictionRequest> _evictionChannel = Channel.CreateUnbounded<EvictionRequest>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly NodeEvictionOptions _options = options.Value;

    public ValueTask QueueEvictionAsync(Guid clusterId, string versionName, long versionNumber)
    {
        var request = new EvictionRequest(clusterId, versionName, versionNumber);
        return _evictionChannel.Writer.WriteAsync(request);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Node Eviction Service started");

        await foreach (var request in _evictionChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessEvictionAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, 
                    "Error processing eviction for cluster {ClusterId}, version {VersionName}/{VersionNumber}",
                    request.ClusterId, request.VersionName, request.VersionNumber);
            }
        }

        logger.LogInformation("Node Eviction Service stopped");
    }

    private async Task ProcessEvictionAsync(EvictionRequest request, CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LighthouseDbContext>();

        const string sql = """

                                       WITH ranked_nodes AS (
                                           SELECT id, 
                                                  ROW_NUMBER() OVER (ORDER BY server_timestamp ASC) as rn,
                                                  COUNT(*) OVER () as total_count
                                           FROM nodes
                                           WHERE cluster_id = {0}
                                             AND version_name = {1}
                                             AND version_number = {2}
                                       )
                                       DELETE FROM nodes
                                       WHERE id IN (
                                           SELECT id 
                                           FROM ranked_nodes 
                                           WHERE total_count > {3} AND rn <= (total_count - {3})
                                       )
                           """;

        var deletedCount = await context.Database.ExecuteSqlRawAsync(
            sql,
            [request.ClusterId, request.VersionName, request.VersionNumber, _options.MaxNodesPerClusterVersion],
            cancellationToken);

        if (deletedCount > 0)
        {
            logger.LogDebug(
                "Evicted {Count} nodes for cluster {ClusterId}, version {VersionName}/{VersionNumber}",
                deletedCount, request.ClusterId, request.VersionName, request.VersionNumber);
        }
    }

    private record EvictionRequest(Guid ClusterId, string VersionName, long VersionNumber);
}

public class NodeEvictionOptions
{
    public int MaxNodesPerClusterVersion { get; set; } = 5;
}
