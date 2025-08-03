using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskbarEqualizer.SystemTray.ContextMenu;
using TaskbarEqualizer.SystemTray.Interfaces;
using TaskbarEqualizer.SystemTray.Rendering;
using TaskbarEqualizer.SystemTray.Theming;

namespace TaskbarEqualizer.SystemTray.DependencyInjection
{
    /// <summary>
    /// Extension methods for registering system tray services in dependency injection container.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds all system tray visualization services to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddSystemTrayServices(this IServiceCollection services)
        {
            // Core rendering services
            services.TryAddSingleton<IIconRenderer, IconRenderer>();
            
            // Theme management
            services.TryAddSingleton<IThemeManager, ThemeManager>();
            
            // Context menu services
            services.TryAddSingleton<IContextMenuManager, ContextMenuManager>();
            
            // System tray integration
            services.TryAddSingleton<ISystemTrayManager, SystemTrayManager>();

            return services;
        }

        /// <summary>
        /// Adds rendering services to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddRenderingServices(this IServiceCollection services)
        {
            services.TryAddSingleton<IIconRenderer, IconRenderer>();
            services.TryAddTransient<GraphicsResourcePool>();
            services.TryAddTransient<RenderCache>();
            services.TryAddTransient<PerformanceTracker>();

            return services;
        }

        /// <summary>
        /// Adds theme management services to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddThemeServices(this IServiceCollection services)
        {
            services.TryAddSingleton<IThemeManager, ThemeManager>();

            return services;
        }

        /// <summary>
        /// Adds system tray integration services to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddTrayIntegrationServices(this IServiceCollection services)
        {
            services.TryAddSingleton<ISystemTrayManager, SystemTrayManager>();

            return services;
        }
    }
}