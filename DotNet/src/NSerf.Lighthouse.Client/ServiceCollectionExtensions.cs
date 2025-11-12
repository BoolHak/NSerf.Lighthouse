using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace NSerf.Lighthouse.Client;

/// <summary>
/// Extension methods for registering the Lighthouse client in a DI container
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Lighthouse client to the service collection with resilience policies
    /// </summary>
    public static IServiceCollection AddLighthouseClient(
        this IServiceCollection services,
        Action<LighthouseClientOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.Configure(configureOptions);

        services.AddHttpClient<ILighthouseClient, LighthouseClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<LighthouseClientOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        })
        .AddStandardResilienceHandler(options =>
        {
            // Configure retry with exponential backoff
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;
            options.Retry.Delay = TimeSpan.FromSeconds(2);
            
            // Configure circuit breaker
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.MinimumThroughput = 5;
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            
            // Configure attempt timeout
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
        });

        return services;
    }
}
