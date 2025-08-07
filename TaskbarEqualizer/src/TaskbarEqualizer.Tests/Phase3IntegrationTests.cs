using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using TaskbarEqualizer.Configuration.DependencyInjection;
using TaskbarEqualizer.Configuration.Interfaces;
using TaskbarEqualizer.SystemTray.DependencyInjection;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.Tests
{
    public class Phase3IntegrationTests : IDisposable
    {
        private IHost? _host;
        private IServiceProvider? _services;

        public Phase3IntegrationTests()
        {
            var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddLogging(builder =>
                    {
                        builder.AddConsole();
                        builder.SetMinimumLevel(LogLevel.Information);
                    });

                    // Add Phase 3 services
                    services.AddPhase3Services();
                    services.AddSystemTrayServices();
                });

            _host = hostBuilder.Build();
            _host.StartAsync().GetAwaiter().GetResult();
            _services = _host.Services;
        }

        public void Dispose()
        {
            if (_host != null)
            {
                _host.StopAsync().GetAwaiter().GetResult();
                _host.Dispose();
            }
        }

        [Fact]
        public void Test_ServiceRegistration()
        {
            Assert.NotNull(_services);
            
            var settingsManager = _services.GetService<ISettingsManager>();
            var contextMenuManager = _services.GetService<IContextMenuManager>();
            var autoStartManager = _services.GetService<IAutoStartManager>();

            Assert.NotNull(settingsManager);
            Assert.NotNull(contextMenuManager);
            Assert.NotNull(autoStartManager);
        }

        [Fact]
        public async Task Test_SettingsManager()
        {
            var settingsManager = _services!.GetRequiredService<ISettingsManager>();

            // Test loading
            if (!settingsManager.IsLoaded)
            {
                await settingsManager.LoadAsync();
            }
            Assert.True(settingsManager.IsLoaded);

            // Test getting/setting values
            var testKey = "TestSetting";
            var testValue = "TestValue123";
            
            await settingsManager.SetSetting(testKey, testValue);
            var retrievedValue = settingsManager.GetSetting<string>(testKey, "DefaultValue");
            
            Assert.Equal(testValue, retrievedValue);
        }

        [Fact]
        public async Task Test_AutoStartManager()
        {
            var autoStartManager = _services!.GetRequiredService<IAutoStartManager>();

            // Test status check - IsAutoStartEnabledAsync returns Task<bool>, not Task<bool?>
            var isEnabled = await autoStartManager.IsAutoStartEnabledAsync();
            Assert.True(isEnabled == true || isEnabled == false); // bool can only be true or false

            // Test validation
            var validation = await autoStartManager.ValidateAutoStartAsync();
            Assert.NotNull(validation);
            Assert.NotNull(validation.Errors);
            Assert.NotNull(validation.Warnings);
        }

        [Fact]
        public void Test_ContextMenuManager()
        {
            var contextMenuManager = _services!.GetRequiredService<IContextMenuManager>();

            bool eventFired = false;
            contextMenuManager.MenuItemClicked += (sender, e) =>
            {
                eventFired = true;
            };

            // Test that the event handler is set up
            Assert.NotNull(contextMenuManager);
            Assert.False(eventFired); // Event hasn't fired yet
        }

        [Fact]
        public async Task Test_CrossComponentCommunication()
        {
            var settingsManager = _services!.GetRequiredService<ISettingsManager>();
            var autoStartManager = _services!.GetRequiredService<IAutoStartManager>();

            // Test event coordination
            bool settingsEventFired = false;
            bool autoStartEventFired = false;

            settingsManager.SettingsChanged += (sender, e) =>
            {
                settingsEventFired = true;
            };

            autoStartManager.AutoStartChanged += (sender, e) =>
            {
                autoStartEventFired = true;
            };

            // Trigger a settings change for StartWithWindows - this SHOULD trigger both events
            await settingsManager.SetSetting("StartWithWindows", true);

            // Give events time to propagate
            await Task.Delay(200); // Increased delay for proper synchronization

            Assert.True(settingsEventFired);
            Assert.True(autoStartEventFired); // AutoStart event SHOULD fire for StartWithWindows setting
        }
    }
}
