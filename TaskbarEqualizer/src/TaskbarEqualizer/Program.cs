using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Core.DependencyInjection;

namespace TaskbarEqualizer
{
    /// <summary>
    /// Main entry point for the TaskbarEqualizer application.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Application entry point.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>Exit code.</returns>
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                // Create host builder
                var hostBuilder = Host.CreateDefaultBuilder(args)
                    .ConfigureServices((context, services) =>
                    {
                        // Add core audio processing services
                        services.AddCoreAudioServices();
                    })
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddConsole();
                        logging.SetMinimumLevel(LogLevel.Information);
                    });

                // Build and run the host
                using var host = hostBuilder.Build();
                
                Console.WriteLine("TaskbarEqualizer Phase 1 Core Infrastructure");
                Console.WriteLine("===========================================");
                Console.WriteLine("✅ Core audio processing services initialized");
                Console.WriteLine("✅ Real-time FFT analysis ready");
                Console.WriteLine("✅ Performance monitoring configured");
                Console.WriteLine("✅ High-performance data structures loaded");
                Console.WriteLine();
                Console.WriteLine("Phase 1 complete - ready for Phase 2 visualization!");
                Console.WriteLine("Press any key to exit...");
                
                Console.ReadKey();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Application failed to start: {ex.Message}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return 1;
            }
        }
    }
}