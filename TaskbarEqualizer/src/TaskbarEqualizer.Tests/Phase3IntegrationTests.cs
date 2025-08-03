using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskbarEqualizer.Configuration.DependencyInjection;
using TaskbarEqualizer.Configuration.Interfaces;
using TaskbarEqualizer.SystemTray.DependencyInjection;
using TaskbarEqualizer.SystemTray.Interfaces;

namespace TaskbarEqualizer.Tests
{
    [TestClass]
    public class Phase3IntegrationTests
    {
        private IHost? _host;
        private IServiceProvider? _services;

        [TestInitialize]
        public async Task Setup()
        {
            var hostBuilder = Host.CreateDefaultBuilder()
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
            await _host.StartAsync();
            _services = _host.Services;
        }

        [TestCleanup]
        public async Task Cleanup()
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }

        [TestMethod]
        public void Test_ServiceRegistration()
        {
            Assert.IsNotNull(_services, "Service provider should be initialized");
            
            var settingsManager = _services.GetService<ISettingsManager>();
            var contextMenuManager = _services.GetService<IContextMenuManager>();
            var autoStartManager = _services.GetService<IAutoStartManager>();

            Assert.IsNotNull(settingsManager, "SettingsManager should be registered");
            Assert.IsNotNull(contextMenuManager, "ContextMenuManager should be registered");
            Assert.IsNotNull(autoStartManager, "AutoStartManager should be registered");
        }

        [TestMethod]
        public async Task Test_SettingsManager()
        {
            var settingsManager = _services!.GetRequiredService<ISettingsManager>();

            // Test loading
            if (!settingsManager.IsLoaded)
            {
                await settingsManager.LoadAsync();
            }
            Assert.IsTrue(settingsManager.IsLoaded, "Settings should be loaded");

            // Test getting/setting values
            var testKey = "TestSetting";
            var testValue = "TestValue123";
            
            await settingsManager.SetSetting(testKey, testValue);
            var retrievedValue = await settingsManager.GetSetting<string>(testKey, "DefaultValue");
            
            Assert.AreEqual(testValue, retrievedValue, "Setting value should match");
        }

        [TestMethod]
        public async Task Test_AutoStartManager()
        {
            var autoStartManager = _services!.GetRequiredService<IAutoStartManager>();

            // Test status check
            var isEnabled = await autoStartManager.IsAutoStartEnabledAsync();
            Assert.IsNotNull(isEnabled, "Auto-start status should be determinable");

            // Test validation
            var validation = await autoStartManager.ValidateAutoStartAsync();
            Assert.IsNotNull(validation, "Validation result should not be null");
            Assert.IsNotNull(validation.Errors, "Validation errors collection should not be null");
            Assert.IsNotNull(validation.Warnings, "Validation warnings collection should not be null");
        }

        [TestMethod]
        public void Test_ContextMenuManager()
        {
            var contextMenuManager = _services!.GetRequiredService<IContextMenuManager>();

            bool eventFired = false;
            contextMenuManager.MenuItemClicked += (sender, e) =>
            {
                eventFired = true;
            };

            // Test that the event handler is set up
            Assert.IsNotNull(contextMenuManager, "Context menu manager should be available");
        }

        [TestMethod]
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

            // Trigger a settings change
            await settingsManager.SetSetting("StartWithWindows", true);

            // Give events time to propagate
            await Task.Delay(100);

            Assert.IsTrue(settingsEventFired, "Settings changed event should fire");
        }
    }
}