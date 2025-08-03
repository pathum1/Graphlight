using System;
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

            return services;
        }

    }
}