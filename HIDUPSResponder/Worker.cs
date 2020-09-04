using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using HIDUPSResponder.Models;
using HIDUPSResponder.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HIDUPSResponder
{
    public class Worker : BackgroundService
    {
        private readonly IHostEnvironment _hostEnvironment;
        private readonly IServiceProvider _services;
        private readonly ILogger<Worker> _logger;
        private readonly IOptionsMonitor<AppSettings> _optsMonitor;
        private AppSettings _appSettings;
        private string _configFile;
        private byte[] _configHash;
        private ManagementEventWatcher _managementEventWatcher;
        private CancellationTokenSource _runningTasksCancellationTokenSource;

        // System uptime
        [DllImport("kernel32")]
        extern static ulong GetTickCount64();

        public Worker(ILogger<Worker> logger,
                        IOptionsMonitor<AppSettings> optsMonitor,
                        IServiceProvider services,
                        IHostEnvironment hostEnvironment)
        {
            _hostEnvironment = hostEnvironment;
            _optsMonitor = optsMonitor;
            _logger = logger;
            _services = services;

            // Monitor running tasks
            _runningTasksCancellationTokenSource = new CancellationTokenSource();

            // Monitor power events
            InitPowerEvents();

            // Set up configuration file change detection
            ConfigFileChangeSetup();

            _appSettings = _optsMonitor.CurrentValue;
        }

        public void InitPowerEvents()
        {
            // Credit: https://stackoverflow.com/questions/3948884/detect-power-state-change

            var q = new WqlEventQuery();
            var scope = new ManagementScope("root\\CIMV2");

            q.EventClassName = "Win32_PowerManagementEvent";
            _managementEventWatcher = new ManagementEventWatcher(scope, q);
            _managementEventWatcher.EventArrived += PowerEventArrive;
            _managementEventWatcher.Start();
        }

        private void PowerEventArrive(object sender, EventArrivedEventArgs e)
        {
            bool exitLoop = false;
            Dictionary<string, string> powerValues = new Dictionary<string, string>
                        {
                            {"4", "Entering Suspend"},
                            {"7", "Resume from Suspend"},
                            {"10", "Power Status Change"},
                            {"11", "OEM Event"},
                            {"18", "Resume Automatic"}
                        };

            foreach (PropertyData pd in e.NewEvent.Properties)
            {
                if (pd != null && pd.Value != null && !exitLoop)
                {
                    if (pd.Value.ToString() == "10")
                    // Power Status Change event = 10
                    {
                        List<string> executeCommands = null;
                        bool cancellingPowerOff = false;
                        exitLoop = true;
                        int taskDelay = 0;

                        var powerStatus = Win32PowerManager.GetSystemPowerStatus();
                        ACLineStatus newACLineStatus = powerStatus.ACLineStatus;

                        if (newACLineStatus == ACLineStatus.Online)
                        {
                            // Power was off but came back on
                            executeCommands = _appSettings.PowerOnCommands;
                            _logger.LogInformation($"Power back on.");

                            // Cancel any running tasks and refresh cancellation token source

                            // TODO: Code below should only run if there is a currently running power off task
                            _runningTasksCancellationTokenSource.Cancel();
                            cancellingPowerOff = true;

                        }
                        else if (newACLineStatus == ACLineStatus.Offline)
                        {
                            // Power was on but then went off
                            executeCommands = _appSettings.PowerOffCommands;
                            taskDelay = _appSettings.SecondsBeforePowerOffExecution * 1000;

                            _logger.LogInformation($"Power is off. Waiting ({_appSettings.SecondsBeforePowerOffExecution}) seconds to begin...");
                        }

                        // Execute the relevant commands
                        if (!cancellingPowerOff)
                        {
                            Task.Run(() =>
                            {
                                // Only reference this copy of the token inside of here
                                var localCancelToken = _runningTasksCancellationTokenSource;

                                try
                                {
                                    _logger.LogDebug($"Task ID#{Task.CurrentId} started.");
                                    _logger.LogInformation($"Executing command(s): {String.Join(", ", executeCommands)}");

                                    if (taskDelay > 0)
                                    {
                                        var delayTask = Task.Delay(taskDelay, localCancelToken.Token);
                                        _logger.LogDebug($"Task ID#{delayTask.Id} (delay task) started. Waiting ({taskDelay / 1000}) seconds...");

                                        // If the task is cancelled here, it will throw an AggregateException and be caught below.
                                        try
                                        {
                                            delayTask.Wait(localCancelToken.Token);
                                        }
                                        catch (OperationCanceledException)
                                        {
                                            _logger.LogDebug($"Task ID#{delayTask.Id} (delay task) cancelled.");
                                        }

                                        // Cancel the outer task if the inner one was cancelled
                                        localCancelToken.Token.ThrowIfCancellationRequested();

                                        _logger.LogDebug($"Task ID#{delayTask.Id} (delay task) completed.");
                                    }

                                    for (int x = 0; x < executeCommands.Count && !localCancelToken.Token.IsCancellationRequested; x++)
                                    {
                                        string command = executeCommands[x];

                                        using (var process = new Process())
                                        {
                                            bool procKilled = false;

                                            // Create a new process for each command in the list
                                            process.StartInfo.FileName = command;
                                            process.StartInfo.CreateNoWindow = true;
                                            process.StartInfo.UseShellExecute = true;

                                            _logger.LogInformation($"Starting child process ({command})...");

                                            try
                                            {
                                                process.Start();

                                                // Check for task cancellation once a second. If task was cancelled, kill the process
                                                const int PROC_POLL_INTERVAL = 1000;

                                                while (!process.WaitForExit(PROC_POLL_INTERVAL))
                                                {
                                                    if (localCancelToken.Token.IsCancellationRequested)
                                                    {
                                                        procKilled = true;
                                                        process.Kill(true);
                                                    }
                                                }

                                                _logger.LogInformation($"Child process {(procKilled ? "killed" : "ended")} ({command}).");
                                            }
                                            catch (Exception ex)
                                            {
                                                _logger.LogError(ex, "Unable to start process.");
                                            }
                                        }
                                    }
                                }
                                catch (AggregateException ae)
                                {
                                    ae.Handle(ex =>
                                    {
                                        if (ex is TaskCanceledException)
                                            _logger.LogDebug($"Task ID#{(ex as TaskCanceledException).Task.Id} cancelled.");

                                        return ex is TaskCanceledException;

                                    });
                                }
                                catch (OperationCanceledException ex)
                                {
                                    _logger.LogDebug($"Task ID#{Task.CurrentId} cancelled.");
                                }

                            }, _runningTasksCancellationTokenSource.Token)
                            .ContinueWith((t) =>
                            {
                                // If a cancellation is requested, it will most likely be caught in the power off delay wait and caught in the AE handler just above.
                                // Because the exception was handled, the outer task will complete successfully despite being cancelled. Thus we'll need to handle this situation here
                                // by reporting the task as being cancelled and resetting the cancellation token source
                                if (t.IsCanceled)
                                {
                                    _logger.LogDebug($"Task ID#{t.Id} cancelled.");
                                }
                                else
                                    _logger.LogDebug($"Task ID#{t.Id} completed with status ({t.Status})");
                            }, _runningTasksCancellationTokenSource.Token);
                        }
                        else
                            _runningTasksCancellationTokenSource = new CancellationTokenSource();
                    }
                }
                /*
                var name = powerValues.ContainsKey(pd.Value.ToString())
                               ? powerValues[pd.Value.ToString()]
                               : pd.Value.ToString();
                _logger.LogInformation($"POWER EVENT: {name}");
                */
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);

            // Cleanup here
            _managementEventWatcher.Stop();
            _logger.LogInformation($"Worker stopped at: {DateTime.Now}");

            if (cancellationToken.IsCancellationRequested)
                if (_runningTasksCancellationTokenSource != null)
                    _runningTasksCancellationTokenSource.Cancel();
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Executing as {(WindowsServiceHelpers.IsWindowsService() ? "windows service" : "console application")}. Working directory is {Directory.GetCurrentDirectory()}");

            // Get rid of await warning
            await Task.Delay(0);

            _logger.LogInformation($"Waiting for events...");
        }

        private void ConfigFileChangeSetup()
        {
            if (_hostEnvironment.IsDevelopment())
                _configFile = "appSettings.Development.json";
            else
                _configFile = "appSettings.json";

            _configHash = Utilities.ComputeHash(_configFile);

            _optsMonitor.OnChange(OnSettingsChange);
        }

        private void OnSettingsChange(AppSettings settings)
        {
            var configHash = Utilities.ComputeHash(_configFile);

            if (!configHash.SequenceEqual(_configHash))
            {
                _configHash = configHash;
                _logger.LogInformation($"Detected change in {_configFile}, reloading.");

                _appSettings = settings;
            }
        }
    }
}
