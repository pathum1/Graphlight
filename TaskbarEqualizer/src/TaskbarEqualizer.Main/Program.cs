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
            // Emergency logging - always available
            void EmergencyLog(string message, Exception? ex = null)
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var logMessage = $"[{timestamp}] TaskbarEqualizer: {message}";
                if (ex != null) logMessage += $"\nException: {ex}";
                
                Console.WriteLine(logMessage);
                System.Diagnostics.Debug.WriteLine(logMessage);
                
                // Also try to write to temp file as backup
                try
                {
                    var tempFile = Path.Combine(Path.GetTempPath(), "TaskbarEqualizer_Emergency.log");
                    File.AppendAllText(tempFile, logMessage + Environment.NewLine);
                }
                catch { /* Ignore temp file errors */ }
            }

            EmergencyLog("=== TaskbarEqualizer Starting ===");
            EmergencyLog($"Args: [{string.Join(", ", args)}]");
            EmergencyLog($"OS: {Environment.OSVersion}");
            EmergencyLog($".NET Version: {Environment.Version}");

            // Enable visual styles for Windows Forms
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            EmergencyLog("Windows Forms initialized");

            try
            {
                // Parse command line arguments
                bool isPortable = Array.Exists(args, arg => 
                    arg.Equals("--portable", StringComparison.OrdinalIgnoreCase));
                bool isMinimized = Array.Exists(args, arg => 
                    arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

                EmergencyLog($"Parsed args - Portable: {isPortable}, Minimized: {isMinimized}");

                // Create host builder with all services - REMOVED UseWindowsService()
                EmergencyLog("Creating host builder...");
                var hostBuilder = Host.CreateDefaultBuilder(args)
                    .ConfigureServices((context, services) =>
                    {
                        EmergencyLog("Configuring services...");
                        // Add all TaskbarEqualizer services
                        EmergencyLog("Adding CoreAudioServices...");
                        services.AddCoreAudioServices();
                        EmergencyLog("Adding SystemTrayServices...");
                        services.AddSystemTrayServices();
                        EmergencyLog("Adding Phase3Services...");
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
                        
                        // Always add console logging for debugging
                        logging.AddConsole();
                        
                        // Add file logging for production
                        logging.AddFilter("TaskbarEqualizer", LogLevel.Information);
                        logging.SetMinimumLevel(LogLevel.Debug);
                    
                        // Add event log for production
                        try 
                        {
                            logging.AddEventLog();
                        }
                        catch 
                        {
                            // Event log might not be available
                        }
                    });

                // Build the host
                EmergencyLog("Building host...");
                _host = hostBuilder.Build();
                EmergencyLog("Host built successfully");

                // Get required services
                EmergencyLog("Getting required services...");
                var logger = _host.Services.GetRequiredService<ILogger<Program>>();
                EmergencyLog("Logger service obtained");
                _systemTrayManager = _host.Services.GetRequiredService<ISystemTrayManager>();
                EmergencyLog("SystemTray service obtained");
                _orchestrator = _host.Services.GetRequiredService<ApplicationOrchestrator>();
                EmergencyLog("Orchestrator service obtained");
                
                // Test context menu manager
                try 
                {
                    var contextMenu = _host.Services.GetRequiredService<IContextMenuManager>();
                    EmergencyLog("ContextMenu service obtained successfully");
                } 
                catch (Exception ex) 
                {
                    EmergencyLog($"ContextMenu service FAILED: {ex.Message}");
                }

                // Log startup
                logger.LogInformation("TaskbarEqualizer starting...");
                logger.LogInformation("Portable mode: {IsPortable}", isPortable);
                logger.LogInformation("Minimized start: {IsMinimized}", isMinimized);

                // Start the host services
                EmergencyLog("Starting host services...");
                await _host.StartAsync();
                EmergencyLog("Host services started");

                // Setup application exit handling
                Application.ApplicationExit += OnApplicationExit;
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

                logger.LogInformation("TaskbarEqualizer started successfully");
                EmergencyLog("=== TaskbarEqualizer Started Successfully ===");

                // Run the Windows Forms message loop with custom application context
                EmergencyLog("Starting Windows Forms message loop...");
                Application.Run(new TaskbarEqualizerApplicationContext(_systemTrayManager, _orchestrator, logger, isMinimized));
                EmergencyLog("Windows Forms message loop ended");

                return 0;
            }
            catch (Exception ex)
            {
                EmergencyLog("=== CRITICAL ERROR ===", ex);
                
                // Log to Windows Event Log if possible
                try
                {
                    using var eventLog = new System.Diagnostics.EventLog("Application");
                    eventLog.Source = "TaskbarEqualizer";
                    eventLog.WriteEntry($"TaskbarEqualizer failed to start: {ex.Message}\n\n{ex}", 
                        System.Diagnostics.EventLogEntryType.Error);
                    EmergencyLog("Error logged to Windows Event Log");
                }
                catch (Exception eventLogEx)
                {
                    EmergencyLog("Failed to log to Windows Event Log", eventLogEx);
                    
                    // If event logging fails, try file logging
                    try
                    {
                        var logPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "TaskbarEqualizer", "crash.log");
                        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                        await File.WriteAllTextAsync(logPath, 
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TaskbarEqualizer startup error: {ex}");
                        EmergencyLog($"Error logged to file: {logPath}");
                    }
                    catch (Exception fileLogEx)
                    {
                        EmergencyLog("Failed to log to file", fileLogEx);
                        // Emergency logging should have captured everything to console/temp file
                    }
                }

                EmergencyLog("=== APPLICATION EXITING WITH ERROR CODE 1 ===");
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
