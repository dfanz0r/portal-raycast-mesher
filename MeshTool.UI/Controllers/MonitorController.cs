using System;
using System.Threading;
using System.Threading.Tasks;
using MeshTool.Core.IO;

namespace MeshTool.UI.Controllers
{
    /// <summary>
    /// Manages the monitor lifecycle and state.
    /// Handles starting and stopping the portal log monitor.
    /// </summary>
    public class MonitorController
    {
        private CancellationTokenSource? _cts;
        private Task? _monitorTask;
        private bool _isRunning;
        private bool _stopNotified;

        /// <summary>
        /// Gets whether the monitor is currently running.
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Raised when the monitor starts.
        /// </summary>
        public event Action? Started;

        /// <summary>
        /// Raised when the monitor stops.
        /// </summary>
        public event Action? Stopped;

        /// <summary>
        /// Raised when a monitor update is received.
        /// </summary>
        public event Action<MonitorUpdate>? Updated;

        /// <summary>
        /// Raised when an error occurs.
        /// </summary>
        public event Action<string>? Error;

        /// <summary>
        /// Starts the monitor.
        /// </summary>
        public Task StartAsync(string logPath, string dbPath, MonitorRunOptions options)
        {
            if (_isRunning)
            {
                Error?.Invoke("Monitor is already running.");
                return Task.CompletedTask;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _isRunning = true;
            _stopNotified = false;
            Started?.Invoke();

            var effectiveOptions = new MonitorRunOptions
            {
                StartAtEnd = options.StartAtEnd,
                ChannelCapacity = options.ChannelCapacity,
                FlushInterval = options.FlushInterval,
                FlushThreshold = options.FlushThreshold,
                SaveDebounce = options.SaveDebounce,
                SaveMinInterval = options.SaveMinInterval,
                SaveMaxInterval = options.SaveMaxInterval,
                IncludeSnapshots = options.IncludeSnapshots,
                SnapshotMinInterval = options.SnapshotMinInterval,
                Log = options.Log,
                OnUpdate = update =>
                {
                    options.OnUpdate?.Invoke(update);
                    Updated?.Invoke(update);
                }
            };

            _monitorTask = MonitorRunner.RunAsync(logPath, dbPath, token, effectiveOptions);

            _ = _monitorTask.ContinueWith(task =>
            {
                _isRunning = false;
                if (!_stopNotified)
                {
                    _stopNotified = true;
                    Stopped?.Invoke();
                }

                if (task.IsFaulted && task.Exception != null)
                {
                    Error?.Invoke(task.Exception.GetBaseException().Message);
                }

                _monitorTask = null;
                _cts?.Dispose();
                _cts = null;
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops the monitor.
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning)
                return;

            _cts?.Cancel();

            if (_monitorTask != null)
            {
                try
                {
                    await _monitorTask;
                }
                catch (OperationCanceledException)
                {
                    // Ignore cancellation exceptions
                }
            }

            _monitorTask = null;
            _cts?.Dispose();
            _cts = null;
            _isRunning = false;
            if (!_stopNotified)
            {
                _stopNotified = true;
                Stopped?.Invoke();
            }
        }

        /// <summary>
        /// Toggles the monitor state.
        /// </summary>
        public async Task ToggleAsync(string logPath, string dbPath, MonitorRunOptions options)
        {
            if (_isRunning)
            {
                await StopAsync();
            }
            else
            {
                await StartAsync(logPath, dbPath, options);
            }
        }
    }
}
