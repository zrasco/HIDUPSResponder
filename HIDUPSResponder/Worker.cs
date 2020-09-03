using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using HIDUPSResponder.Models;
using HIDUPSResponder.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        private ulong _statusChangeTick;

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

            _statusChangeTick = ulong.MaxValue;

            // Set up configuration file change detection
            ConfigFileChangeSetup();

            _appSettings = _optsMonitor.CurrentValue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ACLineStatus currentACLineStatus = Win32PowerManager.GetSystemPowerStatus().ACLineStatus;
            Task currentExecutionTask = null;
            CancellationTokenSource currentCancellationTokenSource = new CancellationTokenSource();

            // Use a polling loop for now. There is a way to subscribe to events but unsure of the best way to implement this.
            while (!stoppingToken.IsCancellationRequested)
            {
                var powerStatus = Win32PowerManager.GetSystemPowerStatus();

                // Check if the status has changed
                if (powerStatus.ACLineStatus != currentACLineStatus)
                {
                    bool cancellingPowerOff = false;
                    int taskDelay = 0;
                    ACLineStatus newACLineStatus = powerStatus.ACLineStatus;
                    List<string> executeCommands = null;

                    if (currentACLineStatus == ACLineStatus.Offline &&
                        newACLineStatus == ACLineStatus.Online)
                    {
                        // Power was off but came back on
                        executeCommands = _appSettings.PowerOnCommands;
                        _logger.LogInformation($"Power back on.");

                        if (currentExecutionTask != null &&
                            !currentExecutionTask.IsCompleted)
                        {
                            cancellingPowerOff = true;
                            currentCancellationTokenSource.Cancel();

                        }
                    }
                    else if (currentACLineStatus == ACLineStatus.Online &&
                        newACLineStatus == ACLineStatus.Offline)
                    {
                        // Power was on but then went off
                        executeCommands = _appSettings.PowerOffCommands;
                        taskDelay = _appSettings.SecondsBeforePowerOffExecution * 1000;

                        _logger.LogInformation($"Power is off. Waiting ({_appSettings.SecondsBeforePowerOffExecution}) seconds to begin...");
                    }

                    // Execute the relevant commands
                    if (!cancellingPowerOff)
                    {
                        currentExecutionTask = Task.Run(() =>
                        {
                            try
                            {
                                _logger.LogDebug($"Task ID#{Task.CurrentId} started.");
                                _logger.LogInformation($"Executing command(s): {String.Join(", ", executeCommands)}");

                                if (taskDelay > 0)
                                {
                                    var delayTask = Task.Delay(taskDelay, currentCancellationTokenSource.Token);
                                    _logger.LogDebug($"Task ID#{delayTask.Id} (delay task) started. Waiting ({taskDelay / 1000}) seconds...");

                                    // If the task is cancelled here, it will throw an AggregateException and be caught below.
                                    delayTask.Wait();
                                    
                                    _logger.LogDebug($"Task ID#{delayTask.Id} (delay task) completed.");
                                }

                                for (int x = 0; x < executeCommands.Count && !currentCancellationTokenSource.Token.IsCancellationRequested; x++)
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
                                                if (currentCancellationTokenSource.Token.IsCancellationRequested)
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
                            catch (TaskCanceledException)
                            {
                                _logger.LogDebug($"Task ID#{Task.CurrentId} cancelled.");
                            }

                        }, currentCancellationTokenSource.Token
                        ).ContinueWith((t) =>
                        {
                            // If a cancellation is requested, it will most likely be caught in the power off delay wait and caught in the AE handler just above.
                            // Because the exception was handled, the outer task will complete successfully despite being cancelled. Thus we'll need to handle this situation here
                            // by reporting the task as being cancelled and resetting the cancellation token source
                            if (currentCancellationTokenSource.Token.IsCancellationRequested)
                            {
                                _logger.LogDebug($"Task ID#{t.Id} cancelled.");
                                currentCancellationTokenSource = new CancellationTokenSource();
                            }
                            else
                                _logger.LogDebug($"Task ID#{t.Id} completed with status ({t.Status})");
                        });
                    }
                    

                    currentACLineStatus = newACLineStatus;
                }

                _logger.LogTrace($"Power status at: {DateTimeOffset.Now} is {powerStatus.ACLineStatus}. Tick count: {GetTickCount64()}");

                await Task.Delay(_appSettings.PollIntervalSeconds * 1000, stoppingToken);
            }

            if (stoppingToken.IsCancellationRequested)
                if (currentCancellationTokenSource != null)
                    currentCancellationTokenSource.Cancel();
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
