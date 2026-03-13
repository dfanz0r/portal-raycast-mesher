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
        /// Raised when an error occurs.
        /// </summary>
        public event Action<string>? Error;

        /// <summary>
        /// Starts the monitor.
        /// </summary>
        public async Task StartAsync(string logPath, string dbPath, MonitorRunOptions options)
        {
            if (_isRunning)
            {
                Error?.Invoke("Monitor is already running.");
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _isRunning = true;
            Started?.Invoke();

            _monitorTask = MonitorRunner.RunAsync(logPath, dbPath, token, options);

            _ = _monitorTask.ContinueWith(_ =>
            {
                _isRunning = false;
                Stopped?.Invoke();
                _monitorTask = null;
                _cts?.Dispose();
                _cts = null;
            });
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
                catch
                {
                    // Ignore cancellation exceptions
                }
            }

            _monitorTask = null;
            _cts?.Dispose();
            _cts = null;
            _isRunning = false;
            Stopped?.Invoke();
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
