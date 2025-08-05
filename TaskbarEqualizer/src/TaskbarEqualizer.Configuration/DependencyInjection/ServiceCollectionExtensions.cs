using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskbarEqualizer.Configuration.Interfaces;
using TaskbarEqualizer.Configuration.Services;
using TaskbarEqualizer.Core.DependencyInjection;

namespace TaskbarEqualizer.Configuration.DependencyInjection
{
    /// <summary>
    /// Extension methods for registering configuration services in dependency injection container.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds configuration management services to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="settingsFilePath">Optional custom path for settings file.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddConfigurationServices(this IServiceCollection services, string? settingsFilePath = null)
        {
            // Register settings manager as singleton
            if (string.IsNullOrEmpty(settingsFilePath))
            {
                services.TryAddSingleton<ISettingsManager, SettingsManager>();
            }
            else
            {
                services.TryAddSingleton<ISettingsManager>(provider => 
                    new SettingsManager(
                        provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SettingsManager>>(),
                        settingsFilePath
                    ));
            }

            return services;
        }

        /// <summary>
        /// Adds auto-start management services to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddAutoStartServices(this IServiceCollection services)
        {
            services.TryAddSingleton<IAutoStartManager, AutoStartManager>();
            return services;
        }

        /// <summary>
        /// Adds configuration management services with auto-initialization.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="settingsFilePath">Optional custom path for settings file.</param>
        /// <param name="autoLoad">Whether to automatically load settings on startup.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddConfigurationServicesWithAutoLoad(
            this IServiceCollection services, 
            string? settingsFilePath = null, 
            bool autoLoad = true)
        {
            services.AddConfigurationServices(settingsFilePath);
            services.AddAutoStartServices();

            if (autoLoad)
            {
                // Add hosted service to auto-load settings
                services.AddHostedService<SettingsInitializationService>();
            }

            return services;
        }

        /// <summary>
        /// Adds the complete Phase 3 user experience orchestration services.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="settingsFilePath">Optional custom path for settings file.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddPhase3Services(
            this IServiceCollection services,
            string? settingsFilePath = null)
        {
            // Add all configuration services with auto-loading
            services.AddConfigurationServicesWithAutoLoad(settingsFilePath, true);

            // Add core audio services
            services.AddCoreAudioServices();

            // Add application orchestrator as both singleton and hosted service
            services.AddSingleton<ApplicationOrchestrator>();
            services.AddHostedService<ApplicationOrchestrator>(provider => 
                provider.GetRequiredService<ApplicationOrchestrator>());

            return services;
        }
    }
}