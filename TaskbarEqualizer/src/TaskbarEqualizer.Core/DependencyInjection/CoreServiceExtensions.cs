using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Core.Audio;
using TaskbarEqualizer.Core.Interfaces;
using TaskbarEqualizer.Core.Performance;

namespace TaskbarEqualizer.Core.DependencyInjection
{
    /// <summary>
    /// Extension methods for registering core audio processing services with dependency injection.
    /// </summary>
    public static class CoreServiceExtensions
    {
        /// <summary>
        /// Registers all core audio processing services with the dependency injection container.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddCoreAudioServices(this IServiceCollection services)
        {
            // Register core audio services
            services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
            services.AddSingleton<IFrequencyAnalyzer, FrequencyAnalyzer>();
            services.AddSingleton<IPerformanceMonitor, PerformanceMonitor>();

            // Register logging if not already registered
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            return services;
        }

        /// <summary>
        /// Registers core audio processing services with custom configuration.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="configureLogging">Optional action to configure logging.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddCoreAudioServices(this IServiceCollection services, 
            Action<ILoggingBuilder>? configureLogging = null)
        {
            // Register core audio services
            services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
            services.AddSingleton<IFrequencyAnalyzer, FrequencyAnalyzer>();
            services.AddSingleton<IPerformanceMonitor, PerformanceMonitor>();

            // Configure logging
            if (configureLogging != null)
            {
                services.AddLogging(configureLogging);
            }
            else
            {
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.AddDebug();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
            }

            return services;
        }

        /// <summary>
        /// Registers performance monitoring services specifically for production environments.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddProductionPerformanceMonitoring(this IServiceCollection services)
        {
            services.AddSingleton<IPerformanceMonitor, PerformanceMonitor>();
            
            // Configure performance-optimized logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Warning); // Reduce logging overhead in production
            });

            return services;
        }

        /// <summary>
        /// Registers development-specific services with enhanced debugging capabilities.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <returns>The service collection for method chaining.</returns>
        public static IServiceCollection AddDevelopmentAudioServices(this IServiceCollection services)
        {
            // Register core services
            services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
            services.AddSingleton<IFrequencyAnalyzer, FrequencyAnalyzer>();
            services.AddSingleton<IPerformanceMonitor, PerformanceMonitor>();

            // Enhanced logging for development
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            return services;
        }
    }
}