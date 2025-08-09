using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using TaskbarEqualizer.Core.DependencyInjection;
using TaskbarEqualizer.SystemTray.DependencyInjection;
using TaskbarEqualizer.Configuration.DependencyInjection;
using TaskbarEqualizer.Configuration.Services;
using TaskbarEqualizer.SystemTray.Interfaces;
using TaskbarEqualizer.SystemTray;

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

            // Enable visual styles and DPI awareness for Windows Forms
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
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
                
                // Configure Serilog for file logging
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TaskbarEqualizer", "logs", "TaskbarEqualizer.log");
                
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .MinimumLevel.Override("TaskbarEqualizer", LogEventLevel.Debug)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                    .WriteTo.File(
                        logPath,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                        shared: true)
                    .CreateLogger();

                var hostBuilder = Host.CreateDefaultBuilder(args)
                    .UseSerilog()
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
                
                // Initialize TrayMenuIntegration as per guide2.pdf
                EmergencyLog("Initializing TrayMenuIntegration...");
                var trayMenuIntegration = _host.Services.GetRequiredService<TrayMenuIntegration>();
                await trayMenuIntegration.InitializeAsync();
                EmergencyLog("TrayMenuIntegration initialized successfully");
                
                // Initialize TaskbarOverlayManager as per guide2.pdf
                EmergencyLog("Initializing TaskbarOverlayManager...");
                var overlayManager = _host.Services.GetRequiredService<ITaskbarOverlayManager>();
                var overlayConfig = new OverlayConfiguration 
                { 
                    Enabled = true, 
                    Width = 400, 
                    Height = 60, 
                    Opacity = 0.9f,
                    Position = OverlayPosition.Center,
                    UpdateFrequency = 60
                };
                await overlayManager.InitializeAsync(overlayConfig);
                EmergencyLog("TaskbarOverlayManager initialized successfully");
                
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

                // No main window - using floating taskbar overlay only
                EmergencyLog("Using floating taskbar overlay for spectrum visualization");

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
                
                // Ensure Serilog flushes any remaining logs
                Log.CloseAndFlush();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
