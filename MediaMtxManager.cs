using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace VideoStreamServer;

public class MediaMtxManager : IHostedService, IDisposable
{
    private readonly ILogger<MediaMtxManager> _logger;
    private readonly IConfiguration _configuration;
    private Process? _mediaMtxProcess;
    private string _executablePath = string.Empty;
    private int _rtspPort = 8554;
    private readonly string _mediaMtxDir;

    public MediaMtxManager(ILogger<MediaMtxManager> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _mediaMtxDir = Path.Combine(Directory.GetCurrentDirectory(), "mediamtx");
    }

    public bool IsRunning => _mediaMtxProcess != null && !_mediaMtxProcess.HasExited;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _rtspPort = _configuration.GetValue<int>("RtspBasePort", 8554);
        
        _logger.LogInformation("Starting MediaMTX RTSP Server Manager");

        try
        {
            // Ensure MediaMTX is available
            await EnsureMediaMtxAvailableAsync(cancellationToken);

            if (string.IsNullOrEmpty(_executablePath))
            {
                _logger.LogError("MediaMTX executable not found. Cannot start RTSP server.");
                return;
            }

            // Create MediaMTX configuration
            CreateMediaMtxConfig();

            // Start MediaMTX process
            var processStartInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                WorkingDirectory = _mediaMtxDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _mediaMtxProcess = new Process { StartInfo = processStartInfo };
            
            _mediaMtxProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogInformation("[MediaMTX] {Output}", e.Data);
            };

            _mediaMtxProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogWarning("[MediaMTX] {Error}", e.Data);
            };

            _mediaMtxProcess.Start();
            _mediaMtxProcess.BeginOutputReadLine();
            _mediaMtxProcess.BeginErrorReadLine();

            _logger.LogInformation("MediaMTX RTSP Server started on port {Port}", _rtspPort);
            
            // Give MediaMTX time to start
            await Task.Delay(2000, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start MediaMTX");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MediaMTX RTSP Server...");
        
        try
        {
            if (_mediaMtxProcess != null && !_mediaMtxProcess.HasExited)
            {
                _mediaMtxProcess.Kill(true);
                await _mediaMtxProcess.WaitForExitAsync(cancellationToken);
                _mediaMtxProcess.Dispose();
            }
            
            _logger.LogInformation("MediaMTX stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping MediaMTX");
        }
    }

    private async Task EnsureMediaMtxAvailableAsync(CancellationToken cancellationToken)
    {
        // Determine OS and architecture
        var os = GetOperatingSystem();
        var arch = GetArchitecture();
        var exeName = os == "windows" ? "mediamtx.exe" : "mediamtx";
        
        _executablePath = Path.Combine(_mediaMtxDir, exeName);

        // Check if already downloaded
        if (File.Exists(_executablePath))
        {
            _logger.LogInformation("MediaMTX already available at {Path}", _executablePath);
            
            // Make executable on Unix systems
            if (os != "windows")
            {
                MakeExecutable(_executablePath);
            }
            
            return;
        }

        // Download MediaMTX
        _logger.LogInformation("MediaMTX not found. Downloading...");
        Directory.CreateDirectory(_mediaMtxDir);

        try
        {
            // Get the latest version or use a known stable version
            var version = await GetLatestMediaMtxVersionAsync(cancellationToken) ?? "v1.16.1";
            
            var fileName = $"mediamtx_{version}_{os}_{arch}.tar.gz";
            if (os == "windows")
            {
                fileName = $"mediamtx_{version}_{os}_{arch}.zip";
            }

            var downloadUrl = $"https://github.com/bluenviron/mediamtx/releases/download/{version}/{fileName}";
            var downloadPath = Path.Combine(_mediaMtxDir, fileName);

            _logger.LogInformation("Downloading from {Url}", downloadUrl);

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            
            var response = await httpClient.GetAsync(downloadUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var fileStream = File.Create(downloadPath);
            await response.Content.CopyToAsync(fileStream, cancellationToken);
            await fileStream.FlushAsync(cancellationToken);
            fileStream.Close();

            _logger.LogInformation("Download complete. Extracting...");

            // Extract archive
            if (os == "windows")
            {
                ZipFile.ExtractToDirectory(downloadPath, _mediaMtxDir, true);
            }
            else
            {
                // Extract tar.gz on Unix
                await ExtractTarGzAsync(downloadPath, _mediaMtxDir);
            }

            // Clean up archive
            File.Delete(downloadPath);

            // Make executable on Unix
            if (os != "windows" && File.Exists(_executablePath))
            {
                MakeExecutable(_executablePath);
            }

            _logger.LogInformation("MediaMTX extracted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download MediaMTX");
            _executablePath = string.Empty;
        }
    }

    private void CreateMediaMtxConfig()
    {
        var configPath = Path.Combine(_mediaMtxDir, "mediamtx.yml");

        // Create a basic configuration that allows publishing and reading
        var config = $@"# MediaMTX Configuration
# RTSP Server
rtspAddress: :{_rtspPort}
protocols: [tcp]
paths:
  all:
    source: publisher
    sourceOnDemand: no
";

        File.WriteAllText(configPath, config);
        _logger.LogInformation("MediaMTX configuration created at {Path}", configPath);
    }

    private static string GetOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "darwin";
        
        throw new PlatformNotSupportedException("Unsupported operating system");
    }

    private static string GetArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64v8",
            Architecture.Arm => "armv7",
            _ => "amd64"
        };
    }

    private static void MakeExecutable(string filePath)
    {
        try
        {
            // On Unix, use chmod to make file executable
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{filePath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit();
        }
        catch
        {
            // Ignore errors on Windows
        }
    }

    private static async Task<string?> GetLatestMediaMtxVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "VideoStreamServer");
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var apiUrl = "https://api.github.com/repos/bluenviron/mediamtx/releases/latest";
            var response = await httpClient.GetStringAsync(apiUrl, cancellationToken);
            
            // Simple JSON parsing to extract tag_name
            var tagIndex = response.IndexOf("\"tag_name\":", StringComparison.Ordinal);
            if (tagIndex > 0)
            {
                var startIndex = response.IndexOf("\"", tagIndex + 11, StringComparison.Ordinal) + 1;
                var endIndex = response.IndexOf("\"", startIndex, StringComparison.Ordinal);
                return response.Substring(startIndex, endIndex - startIndex);
            }
        }
        catch
        {
            // Fallback to hardcoded version if API call fails
        }

        return null;
    }

    private static async Task ExtractTarGzAsync(string archivePath, string destinationDir)
    {
        // Use tar command on Unix systems
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{destinationDir}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();
    }

    public void Dispose()
    {
        _mediaMtxProcess?.Dispose();
    }
}
