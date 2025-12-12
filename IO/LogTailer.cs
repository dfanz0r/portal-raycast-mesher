using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TerrainTool.IO
{
    public sealed class LogTailer
    {
        public enum ResetReason
        {
            NewFile,
            Rotation,
            Truncation,
            Deleted
        }

        public readonly record struct ResetEvent(ResetReason Reason, long NewFileLengthBytes, bool StartAtEnd);

        private readonly string _filePath;
        private readonly bool _startAtEnd;
        private readonly ChannelWriter<string> _writer;

        private long _lastPos;
        private ulong _lastId;
        private bool _fileActive;
        private readonly StringBuilder _partial = new();
        private readonly SemaphoreSlim _signal = new(0);

        public event Action<ResetEvent>? Reset;

        public long CurrentByteOffset => Interlocked.Read(ref _lastPos);

        public LogTailer(string filePath, ChannelWriter<string> writer, bool startAtEnd = true)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _startAtEnd = startAtEnd;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (string.IsNullOrEmpty(dir)) dir = AppDomain.CurrentDomain.BaseDirectory;
            string file = Path.GetFileName(_filePath);

            using var watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.CreationTime
            };

            FileSystemEventHandler onEvent = (s, e) =>
            {
                if (_signal.CurrentCount == 0) _signal.Release();
            };

            watcher.Changed += onEvent;
            watcher.Created += onEvent;
            watcher.Deleted += onEvent;
            watcher.Renamed += (s, e) => { if (_signal.CurrentCount == 0) _signal.Release(); };
            watcher.EnableRaisingEvents = true;

            // initial wakeup
            _signal.Release();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await _signal.WaitAsync(1000, cancellationToken);

                    if (File.Exists(_filePath))
                    {
                        var fileInfo = new FileInfo(_filePath);
                        long currentLength = fileInfo.Length;
                        ulong currentId = TryGetFileId(_filePath);

                        if (!_fileActive)
                        {
                            _fileActive = true;
                            _lastId = currentId;
                            _partial.Clear();

                            // Avoid duplicating an existing log backlog into an existing DB by default.
                            _lastPos = _startAtEnd ? currentLength : 0;

                            Reset?.Invoke(new ResetEvent(ResetReason.NewFile, currentLength, _startAtEnd));

                            if (!_startAtEnd)
                                await ReadNewContentAsync(cancellationToken);
                        }
                        else if (_lastId != 0 && currentId != 0 && currentId != _lastId)
                        {
                            await FlushPartialAsFragmentAsync(cancellationToken);
                            _lastId = currentId;
                            _lastPos = 0;

                            Reset?.Invoke(new ResetEvent(ResetReason.Rotation, currentLength, _startAtEnd));
                            await ReadNewContentAsync(cancellationToken);
                        }
                        else if (currentLength < _lastPos)
                        {
                            await FlushPartialAsFragmentAsync(cancellationToken);
                            _lastPos = 0;

                            Reset?.Invoke(new ResetEvent(ResetReason.Truncation, currentLength, _startAtEnd));
                            await ReadNewContentAsync(cancellationToken);
                        }
                        else if (currentLength > _lastPos)
                        {
                            await ReadNewContentAsync(cancellationToken);
                        }
                    }
                    else if (_fileActive)
                    {
                        await FlushPartialAsFragmentAsync(cancellationToken);
                        _fileActive = false;
                        _lastPos = 0;
                        _lastId = 0;

                        Reset?.Invoke(new ResetEvent(ResetReason.Deleted, 0, _startAtEnd));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            finally
            {
                await FlushPartialAsFragmentAsync(CancellationToken.None);
                _writer.TryComplete();
            }
        }

        private async Task FlushPartialAsFragmentAsync(CancellationToken cancellationToken)
        {
            if (_partial.Length <= 0) return;

            string content = _partial.ToString();
            _partial.Clear();

            // Keep the raw fragment, but mark it as such.
            await _writer.WriteAsync($"[FRAGMENT] {content}", cancellationToken);
        }

        private async Task ReadNewContentAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                if (fs.Length <= _lastPos) return;

                fs.Seek(_lastPos, SeekOrigin.Begin);

                using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
                string newChunk = await sr.ReadToEndAsync(cancellationToken);
                if (string.IsNullOrEmpty(newChunk))
                {
                    _lastPos = fs.Position;
                    return;
                }

                _partial.Append(newChunk);

                string currentBuffer = _partial.ToString();
                int lastNewlineIndex = currentBuffer.LastIndexOf('\n');
                if (lastNewlineIndex < 0)
                {
                    _lastPos = fs.Position;
                    return;
                }

                string validLines = currentBuffer.Substring(0, lastNewlineIndex + 1);
                using var reader = new StringReader(validLines);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    await _writer.WriteAsync(line, cancellationToken);
                }

                _partial.Remove(0, lastNewlineIndex + 1);
                _lastPos = fs.Position;
            }
            catch (IOException)
            {
                // transient lock; next signal/heartbeat retries
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(IntPtr hFile, out BY_HANDLE_FILE_INFORMATION info);

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        private static ulong TryGetFileId(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (GetFileInformationByHandle(fs.SafeFileHandle.DangerousGetHandle(), out var info))
                    return ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
            }
            catch
            {
                // ignore
            }
            return 0;
        }
    }
}
