using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Core.DependencyInjection;
using TaskbarEqualizer.SystemTray.DependencyInjection;
using TaskbarEqualizer.Configuration.DependencyInjection;
using TaskbarEqualizer.Configuration.Services;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.Main
{
    /// <summary>
    /// Main entry point for the complete TaskbarEqualizer application.
    /// Integrates all phases: Core audio processing, Visualization engine, and User experience.
    /// </summary>
    public class Program
    {
        private static IHost? _host;
        private static ISystemTrayManager? _systemTrayManager;
        private static ApplicationOrchestrator? _orchestrator;

        /// <summary>
        /// Application entry point.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>Exit code.</returns>
        [STAThread]
        public static async Task<int> Main(string[] args)
        {
            // Enable visual styles for Windows Forms
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                // Parse command line arguments
                bool isPortable = Array.Exists(args, arg => 
                    arg.Equals("--portable", StringComparison.OrdinalIgnoreCase));
                bool isMinimized = Array.Exists(args, arg => 
                    arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

                // Create host builder with all services
                var hostBuilder = Host.CreateDefaultBuilder(args)
                    .ConfigureServices((context, services) =>
                    {
                        // Add all TaskbarEqualizer services
                        services.AddCoreAudioServices();
                        services.AddSystemTrayServices();
                        services.AddPhase3Services();

                        // Configure portable mode if specified
                        if (isPortable)
                        {
                            // TODO: Configure portable mode settings
                            // This will be handled by the settings manager
                        }
                    })
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        
                        // Only add console logging in debug mode or if console is available
                        if (System.Diagnostics.Debugger.IsAttached || Console.IsInputRedirected == false)
                        {
                            try
                            {
                                logging.AddConsole();
                            }
                            catch
                            {
                                // Ignore console logging if not available
                            }
                        }
                        
                        // Add file logging for production
                        logging.AddEventLog();
                        logging.SetMinimumLevel(LogLevel.Information);
                    })
                    .UseWindowsService(); // Allow running as Windows service

                // Build the host
                _host = hostBuilder.Build();

                // Get required services
                var logger = _host.Services.GetRequiredService<ILogger<Program>>();
                _systemTrayManager = _host.Services.GetRequiredService<ISystemTrayManager>();
                _orchestrator = _host.Services.GetRequiredService<ApplicationOrchestrator>();

                // Log startup
                logger.LogInformation("TaskbarEqualizer starting...");
                logger.LogInformation("Portable mode: {IsPortable}", isPortable);
                logger.LogInformation("Minimized start: {IsMinimized}", isMinimized);

                // Start the host services
                await _host.StartAsync();

                // Initialize system tray with default icon
                var trayIcon = SystemIcons.Application; // Use default system icon for now
                await _systemTrayManager.InitializeAsync(trayIcon, "TaskbarEqualizer - Professional Audio Visualizer");
                logger.LogInformation("System tray initialized");

                // Start the orchestrator
                await _orchestrator.StartAsync(default);
                logger.LogInformation("Application orchestrator started");

                // Setup application exit handling
                Application.ApplicationExit += OnApplicationExit;
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

                // Show startup notification (unless minimized)
                if (!isMinimized)
                {
                    await _systemTrayManager.ShowBalloonTipAsync(
                        "TaskbarEqualizer Started", 
                        "Professional audio visualizer is now running. Right-click the tray icon for options.",
                        BalloonTipIcon.Info);
                }

                logger.LogInformation("TaskbarEqualizer started successfully");

                // Run the Windows Forms message loop
                Application.Run();

                return 0;
            }
            catch (Exception ex)
            {
                // Log to Windows Event Log if possible
                try
                {
                    using var eventLog = new System.Diagnostics.EventLog("Application");
                    eventLog.Source = "TaskbarEqualizer";
                    eventLog.WriteEntry($"TaskbarEqualizer failed to start: {ex.Message}\n\n{ex}", 
                        System.Diagnostics.EventLogEntryType.Error);
                }
                catch
                {
                    // If event logging fails, try file logging
                    try
                    {
                        var logPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "TaskbarEqualizer", "crash.log");
                        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                        await File.WriteAllTextAsync(logPath, 
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TaskbarEqualizer startup error: {ex}");
                    }
                    catch
                    {
                        // Last resort - ignore if all logging fails
                    }
                }

                return 1;
            }
        }

        /// <summary>
        /// Handles application exit cleanup.
        /// </summary>
        private static void OnApplicationExit(object? sender, EventArgs e)
        {
            CleanupAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Handles process exit cleanup.
        /// </summary>
        private static void OnProcessExit(object? sender, EventArgs e)
        {
            CleanupAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Performs cleanup operations.
        /// </summary>
        private static async Task CleanupAsync()
        {
            try
            {
                // Stop orchestrator
                if (_orchestrator != null)
                {
                    await _orchestrator.StopAsync(default);
                    _orchestrator.Dispose();
                }

                // Cleanup system tray
                _systemTrayManager?.Dispose();

                // Stop and dispose host
                if (_host != null)
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(5));
                    _host.Dispose();
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}