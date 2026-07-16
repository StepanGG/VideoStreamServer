using System.Collections.Concurrent;
using System.Diagnostics;

namespace VideoStreamServer;

public class RtspStreamManager : IHostedService, IDisposable
{
    private readonly ILogger<RtspStreamManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly MediaMtxManager _mediaMtxManager;
    private readonly ConcurrentDictionary<string, StreamInfo> _streams = new();
    private readonly ConcurrentDictionary<string, bool> _baselineOverrides = new();
    private string _videoDirectory = string.Empty;
    private int _baseRtspPort = 8554;

    public RtspStreamManager(
        ILogger<RtspStreamManager> logger,
        IConfiguration configuration,
        MediaMtxManager mediaMtxManager)
    {
        _logger = logger;
        _configuration = configuration;
        _mediaMtxManager = mediaMtxManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _videoDirectory = _configuration["VideoDirectory"] ?? Path.Combine(Directory.GetCurrentDirectory(), "videos");
        _baseRtspPort = _configuration.GetValue<int>("RtspBasePort", 8554);

        if (!Directory.Exists(_videoDirectory))
        {
            Directory.CreateDirectory(_videoDirectory);
            _logger.LogInformation("Created video directory: {Directory}", _videoDirectory);
        }

        _logger.LogInformation("Starting RTSP Stream Manager");
        _logger.LogInformation("Video directory: {Directory}", _videoDirectory);
        _logger.LogInformation("RTSP server port: {Port}", _baseRtspPort);

        // Check if FFmpeg is available
        if (!CheckFFmpegAvailable())
        {
            _logger.LogError("FFmpeg not found! Please install FFmpeg and ensure it's in your PATH.");
            _logger.LogError("Download from: https://ffmpeg.org/download.html");
            return;
        }

        // Wait for MediaMTX to be ready
        _logger.LogInformation("Waiting for MediaMTX RTSP server to be ready...");
        var retries = 0;
        while (!_mediaMtxManager.IsRunning && retries < 10)
        {
            await Task.Delay(500, cancellationToken);
            retries++;
        }

        if (!_mediaMtxManager.IsRunning)
        {
            _logger.LogError("MediaMTX RTSP server is not running. Cannot start streams.");
            return;
        }

        _logger.LogInformation("MediaMTX is ready. Starting video streams...");

        // Start streaming all video files
        await StartAllStreamsAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping all RTSP streams...");

        foreach (var (fileName, streamInfo) in _streams)
        {
            StopStream(fileName);
        }

        _streams.Clear();
        await Task.CompletedTask;
    }

    private async Task StartAllStreamsAsync()
    {
        var videoFiles = Directory.GetFiles(_videoDirectory)
            .Where(f => IsVideoFile(f))
            .ToArray();

        if (videoFiles.Length == 0)
        {
            _logger.LogWarning("No video files found in {Directory}", _videoDirectory);
            return;
        }

        _logger.LogInformation("Found {Count} video file(s)", videoFiles.Length);

        foreach (var filePath in videoFiles)
        {
            var fileName = Path.GetFileName(filePath);
            await StartStreamAsync(fileName, filePath);
        }
    }

    private async Task StartStreamAsync(string fileName, string filePath)
    {
        try
        {
            // RTSP stream name - clean filename for URL
            var streamName = Path.GetFileNameWithoutExtension(fileName)
                .Replace(" ", "_")
                .Replace(".", "_");

            // MediaMTX RTSP URL format: rtsp://localhost:port/streamname
            var rtspUrl = $"rtsp://localhost:{_baseRtspPort}/{streamName}";

            // FFmpeg command to push video file to MediaMTX as RTSP stream
            // -re: real-time streaming
            // -stream_loop -1: loop infinitely
            // -c copy: copy codecs (no transcoding for performance)
            // -f rtsp: output format RTSP
            // -rtsp_transport tcp: use TCP for reliability
            var isBaseline = _baselineOverrides.GetValueOrDefault(fileName, false);
            var videoArgs = isBaseline
                ? "-c:v libx264 -profile:v baseline -level:v 3.1 -preset ultrafast -pix_fmt yuv420p"
                : "-c:v copy";

            var arguments = $"-re -stream_loop -1 -i \"{filePath}\" " +
                          $"{videoArgs} -c:a copy " +
                          $"-f rtsp " +
                          $"-rtsp_transport tcp " +
                          $"\"{rtspUrl}\"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = processStartInfo };

            // Log FFmpeg output for debugging
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogDebug("[FFmpeg-{FileName}] {Output}", fileName, e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    // Only log non-routine messages
                    if (e.Data.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        e.Data.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("[FFmpeg-{FileName}] {Error}", fileName, e.Data);
                    }
                    else
                    {
                        _logger.LogDebug("[FFmpeg-{FileName}] {Error}", fileName, e.Data);
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var streamInfo = new StreamInfo
            {
                FileName = fileName,
                FilePath = filePath,
                RtspUrl = rtspUrl,
                Port = _baseRtspPort,
                Process = process,
                StartTime = DateTime.UtcNow
            };

            _streams[fileName] = streamInfo;

            _logger.LogInformation("Started RTSP stream: '{FileName}' → {Url}", fileName, rtspUrl);

            // Give FFmpeg time to connect to MediaMTX
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start stream for {FileName}", fileName);
        }
    }

    private void StopStream(string fileName)
    {
        if (_streams.TryRemove(fileName, out var streamInfo))
        {
            try
            {
                if (streamInfo.Process != null && !streamInfo.Process.HasExited)
                {
                    streamInfo.Process.Kill(true);
                    streamInfo.Process.Dispose();
                }

                _logger.LogInformation("Stopped RTSP stream for '{FileName}'", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping stream for {FileName}", fileName);
            }
        }
    }

    public IEnumerable<StreamInfo> GetAllStreams()
    {
        return _streams.Values.ToList();
    }

    public StreamInfo? GetStream(string fileName)
    {
        _streams.TryGetValue(fileName, out var streamInfo);
        return streamInfo;
    }

    public async Task<bool> StartStreamByNameAsync(string fileName)
    {
        // Check if stream is already running
        if (_streams.ContainsKey(fileName))
        {
            _logger.LogWarning("Stream '{FileName}' is already running", fileName);
            return false;
        }

        // Find the video file
        var filePath = Path.Combine(_videoDirectory, fileName);
        if (!File.Exists(filePath))
        {
            _logger.LogError("Video file '{FileName}' not found", fileName);
            return false;
        }

        if (!IsVideoFile(filePath))
        {
            _logger.LogError("File '{FileName}' is not a supported video format", fileName);
            return false;
        }

        await StartStreamAsync(fileName, filePath);
        return true;
    }

    public bool StopStreamByName(string fileName)
    {
        if (!_streams.ContainsKey(fileName))
        {
            _logger.LogWarning("Stream '{FileName}' is not running", fileName);
            return false;
        }

        StopStream(fileName);
        return true;
    }

    public async Task<bool> RestartStreamAsync(string fileName)
    {
        if (!_streams.ContainsKey(fileName))
        {
            _logger.LogWarning("Stream '{FileName}' is not running, starting it instead", fileName);
            return await StartStreamByNameAsync(fileName);
        }

        StopStream(fileName);
        await Task.Delay(500); // Brief pause before restarting
        return await StartStreamByNameAsync(fileName);
    }

    public bool IsBaseline(string fileName) =>
        _baselineOverrides.GetValueOrDefault(fileName, false);

    public async Task SetBaselineAsync(string fileName, bool enable)
    {
        _baselineOverrides[fileName] = enable;
        _logger.LogInformation("Baseline {Action} for '{FileName}'. Restarting stream if running...",
            enable ? "enabled" : "disabled", fileName);

        if (_streams.ContainsKey(fileName))
            await RestartStreamAsync(fileName);
    }

    public void StopAllStreams()
    {
        var fileNames = _streams.Keys.ToList();
        foreach (var fileName in fileNames)
        {
            StopStream(fileName);
        }
        _logger.LogInformation("Stopped all RTSP streams");
    }

    public async Task StartAllAvailableStreamsAsync()
    {
        var videos = GetAvailableVideoFiles().Where(v => !v.IsStreaming).ToList();

        foreach (var video in videos)
        {
            await StartStreamByNameAsync(video.FileName);
        }

        _logger.LogInformation("Started {Count} stream(s)", videos.Count);
    }

    public IEnumerable<VideoFileInfo> GetAvailableVideoFiles()
    {
        if (!Directory.Exists(_videoDirectory))
        {
            return Enumerable.Empty<VideoFileInfo>();
        }

        return Directory.GetFiles(_videoDirectory)
            .Where(f => IsVideoFile(f))
            .Select(f => new VideoFileInfo
            {
                FileName = Path.GetFileName(f),
                FilePath = f,
                Size = new FileInfo(f).Length,
                IsStreaming = _streams.ContainsKey(Path.GetFileName(f))
            })
            .ToList();
    }

    private bool CheckFFmpegAvailable()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsVideoFile(string path)
    {
        var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts" };
        return videoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    }

    public void Dispose()
    {
        foreach (var (_, streamInfo) in _streams)
        {
            streamInfo.Process?.Dispose();
        }
        _streams.Clear();
    }
}

public class StreamInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string RtspUrl { get; set; } = string.Empty;
    public int Port { get; set; }
    public Process? Process { get; set; }
    public DateTime StartTime { get; set; }
    public bool IsRunning => Process != null && !Process.HasExited;
}

public class VideoFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool IsStreaming { get; set; }
}
