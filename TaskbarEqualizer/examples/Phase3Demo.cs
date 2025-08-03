using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskbarEqualizer.Configuration.DependencyInjection;
using TaskbarEqualizer.Configuration.Interfaces;
using TaskbarEqualizer.SystemTray.DependencyInjection;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.Examples
{
    /// <summary>
    /// Phase 3 demonstration showing integrated user experience features:
    /// - Settings persistence with JSON configuration
    /// - Context menu system with Windows 11 styling
    /// - Auto-start functionality with Windows registry integration
    /// - Application orchestration tying all components together
    /// </summary>
    public static class Phase3Demo
    {
        /// <summary>
        /// Main entry point for Phase 3 demonstration.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== TaskbarEqualizer Phase 3 User Experience Demo ===");
            Console.WriteLine("Demonstrating integrated settings, context menu, and auto-start functionality");
            Console.WriteLine();

            // Create cancellation token for graceful shutdown
            using var cancellationTokenSource = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancellationTokenSource.Cancel();
                Console.WriteLine("\nShutdown requested...");
            };

            try
            {
                await RunPhase3DemoAsync(cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Demo cancelled by user.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Demo failed with error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("\nPhase 3 demo completed. Press any key to exit...");
            Console.ReadKey();
        }

        /// <summary>
        /// Runs the Phase 3 integrated demonstration.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        private static async Task RunPhase3DemoAsync(CancellationToken cancellationToken)
        {
            // Create host builder with all Phase 3 services
            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Add logging
                    services.AddLogging(builder =>
                    {
                        builder.AddConsole();
                        builder.SetMinimumLevel(LogLevel.Information);
                        builder.AddFilter("TaskbarEqualizer", LogLevel.Debug);
                    });

                    // Add Phase 3 services (includes orchestrator)
                    services.AddPhase3Services();

                    // Add system tray services
                    services.AddSystemTrayServices();
                });

            Console.WriteLine("Building Phase 3 service host...");
            using var host = hostBuilder.Build();

            Console.WriteLine("Starting Phase 3 services...");
            await host.StartAsync(cancellationToken);

            // Get service instances for demonstration
            var settingsManager = host.Services.GetRequiredService<ISettingsManager>();
            var contextMenuManager = host.Services.GetRequiredService<IContextMenuManager>();
            var autoStartManager = host.Services.GetRequiredService<IAutoStartManager>();

            Console.WriteLine("✓ All Phase 3 services started successfully!");
            Console.WriteLine();

            // Demonstrate Phase 3 functionality
            await DemonstrateSettingsManagement(settingsManager, cancellationToken);
            await DemonstrateAutoStartFunctionality(autoStartManager, cancellationToken);
            await DemonstrateContextMenuIntegration(contextMenuManager, cancellationToken);
            await DemonstrateCrossComponentCommunication(settingsManager, autoStartManager, cancellationToken);

            Console.WriteLine("Phase 3 demonstration completed successfully!");
            Console.WriteLine("Services will remain running. Press Ctrl+C to stop...");

            // Keep services running until cancellation
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Stopping Phase 3 services...");
            }

            await host.StopAsync(CancellationToken.None);
            Console.WriteLine("✓ Phase 3 services stopped cleanly");
        }

        /// <summary>
        /// Demonstrates settings management functionality.
        /// </summary>
        private static async Task DemonstrateSettingsManagement(ISettingsManager settingsManager, CancellationToken cancellationToken)
        {
            Console.WriteLine("1. Settings Management Demonstration:");
            Console.WriteLine("   • Loading application settings...");
            
            if (!settingsManager.IsLoaded)
            {
                await settingsManager.LoadAsync(cancellationToken);
            }
            Console.WriteLine($"   ✓ Settings loaded: {settingsManager.IsLoaded}");

            Console.WriteLine("   • Testing setting modification...");
            var originalAutoStart = await settingsManager.GetSetting<bool>("StartWithWindows", false);
            await settingsManager.SetSetting("StartWithWindows", !originalAutoStart);
            
            var newValue = await settingsManager.GetSetting<bool>("StartWithWindows", false);
            Console.WriteLine($"   ✓ Start with Windows changed: {originalAutoStart} → {newValue}");

            Console.WriteLine("   • Saving settings...");
            if (settingsManager.IsDirty)
            {
                await settingsManager.SaveAsync(cancellationToken);
                Console.WriteLine("   ✓ Settings saved successfully");
            }

            Console.WriteLine("   ✓ Settings management demonstration completed");
            Console.WriteLine();
        }

        /// <summary>
        /// Demonstrates auto-start functionality.
        /// </summary>
        private static async Task DemonstrateAutoStartFunctionality(IAutoStartManager autoStartManager, CancellationToken cancellationToken)
        {
            Console.WriteLine("2. Auto-Start Functionality Demonstration:");
            Console.WriteLine("   • Checking current auto-start status...");
            
            var currentStatus = await autoStartManager.IsAutoStartEnabledAsync(cancellationToken);
            Console.WriteLine($"   ✓ Auto-start currently enabled: {currentStatus}");

            Console.WriteLine("   • Validating auto-start configuration...");
            var validationResult = await autoStartManager.ValidateAutoStartAsync(cancellationToken);
            Console.WriteLine($"   ✓ Configuration valid: {validationResult.IsValid}");
            
            if (validationResult.Errors.Count > 0)
            {
                Console.WriteLine($"   ⚠ Validation errors: {string.Join(", ", validationResult.Errors)}");
            }

            if (validationResult.Warnings.Count > 0)
            {
                Console.WriteLine($"   ⚠ Validation warnings: {string.Join(", ", validationResult.Warnings)}");
            }

            Console.WriteLine("   • Getting registry entry information...");
            var registryEntry = await autoStartManager.GetRegistryEntryAsync(cancellationToken);
            if (registryEntry != null)
            {
                Console.WriteLine($"   ✓ Registry entry found: {registryEntry.Name}");
                Console.WriteLine($"     - Command line: {registryEntry.CommandLine}");
                Console.WriteLine($"     - Location: {(registryEntry.IsCurrentUser ? "HKCU" : "HKLM")}");
                Console.WriteLine($"     - Executable exists: {registryEntry.ExecutableExists}");
            }
            else
            {
                Console.WriteLine("   • No registry entry found");
            }

            Console.WriteLine("   ✓ Auto-start functionality demonstration completed");
            Console.WriteLine();
        }

        /// <summary>
        /// Demonstrates context menu integration.
        /// </summary>
        private static async Task DemonstrateContextMenuIntegration(IContextMenuManager contextMenuManager, CancellationToken cancellationToken)
        {
            Console.WriteLine("3. Context Menu Integration Demonstration:");
            Console.WriteLine("   • Setting up context menu event handling...");
            
            contextMenuManager.MenuItemClicked += (sender, e) =>
            {
                Console.WriteLine($"   ✓ Menu item clicked: {e.MenuItem.Id} - {e.MenuItem.Text}");
            };

            Console.WriteLine("   ✓ Context menu manager ready for user interaction");
            Console.WriteLine("   ✓ Menu event handler registered successfully");
            Console.WriteLine("   • Context menu integration demonstration completed");
            Console.WriteLine();

            await Task.Delay(100, cancellationToken); // Brief delay for demonstration
        }

        /// <summary>
        /// Demonstrates cross-component communication and orchestration.
        /// </summary>
        private static async Task DemonstrateCrossComponentCommunication(
            ISettingsManager settingsManager, 
            IAutoStartManager autoStartManager, 
            CancellationToken cancellationToken)
        {
            Console.WriteLine("4. Cross-Component Communication Demonstration:");
            Console.WriteLine("   • Testing settings and auto-start synchronization...");

            // Get current states
            var settingsAutoStart = await settingsManager.GetSetting<bool>("StartWithWindows", false);
            var registryAutoStart = await autoStartManager.IsAutoStartEnabledAsync(cancellationToken);

            Console.WriteLine($"   • Settings StartWithWindows: {settingsAutoStart}");
            Console.WriteLine($"   • Registry auto-start enabled: {registryAutoStart}");

            if (settingsAutoStart != registryAutoStart)
            {
                Console.WriteLine("   ⚠ States are out of sync - orchestrator should handle this automatically");
            }
            else
            {
                Console.WriteLine("   ✓ Settings and registry are synchronized");
            }

            Console.WriteLine("   • Setting up event listeners for cross-component communication...");
            
            settingsManager.SettingsChanged += (sender, e) =>
            {
                Console.WriteLine($"   ✓ Settings changed event: {string.Join(", ", e.ChangedKeys)}");
            };

            autoStartManager.AutoStartChanged += (sender, e) =>
            {
                Console.WriteLine($"   ✓ Auto-start changed event: Enabled={e.IsEnabled}, Reason={e.Reason}");
            };

            Console.WriteLine("   ✓ Event listeners configured successfully");
            Console.WriteLine("   ✓ Cross-component communication demonstration completed");
            Console.WriteLine();

            // Allow time for any event propagation
            await Task.Delay(500, cancellationToken);
        }
    }
}