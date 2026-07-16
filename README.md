# RTSP Video Streaming Server

A C# application for streaming all video files from a folder as RTSP streams using ASP.NET Core, FFmpeg, and MediaMTX.

## Features

- 🎥 **Automatic RTSP Streaming** - All videos in the folder are automatically streamed as RTSP
- 📁 **Multiple Videos** - Each video file gets its own RTSP stream path
- 🔄 **Continuous Playback** - Videos loop continuously for uninterrupted streaming
- 🌐 **Web API** - REST endpoints to list and query available streams
- 📊 **Stream Monitoring** - Real-time status of all active streams
- 🎬 **Multiple Format Support** - MP4, MKV, AVI, MOV, WMV, FLV, WebM, TS, and more
- 🤖 **Auto-Setup** - Automatically downloads and configures MediaMTX RTSP server

## How It Works

1. **MediaMTX** runs as the RTSP server (automatically downloaded on first run)
2. **FFmpeg** processes read video files and push streams to MediaMTX
3. **Clients** connect to MediaMTX to watch the streams
4. Videos loop infinitely for continuous streaming

```
Video Files → FFmpeg → MediaMTX RTSP Server → Clients (VLC, OBS, etc.)
```

## Prerequisites

**FFmpeg must be installed and available in your system PATH.**

### Installing FFmpeg

#### Windows
1. Download FFmpeg from [ffmpeg.org/download.html](https://ffmpeg.org/download.html)
2. Extract the archive (e.g., to `C:\ffmpeg`)
3. Add FFmpeg to PATH:
   - Open System Properties → Environment Variables
   - Edit PATH variable and add `C:\ffmpeg\bin`
4. Verify installation: Open new terminal and run `ffmpeg -version`

## Quick Start

1. **Ensure FFmpeg is installed** (see Prerequisites above)

2. **Place video files** in the `videos` directory (created automatically if it doesn't exist)

3. **Run the server:**
   ```bash
   dotnet run
   ```
   On first run, MediaMTX will be automatically downloaded (~10MB).

4. **View available streams:**
   - Web interface: `http://localhost:5000/`
   - JSON API: `http://localhost:5000/streams`

5. **Play RTSP streams** using VLC, FFplay, or OBS (see Usage Examples below)

## Architecture

The server consists of three main components:

1. **MediaMtxManager.cs**: Hosted service that:
   - Auto-downloads MediaMTX RTSP server on first run
   - Configures and manages the MediaMTX process
   - Provides a central RTSP server on port 8554

2. **RtspStreamManager.cs**: Hosted service that:
   - Scans video directory on startup
   - Spawns FFmpeg processes to push each video to MediaMTX
   - Manages FFmpeg process lifecycle
   - Provides stream information

3. **Program.cs**: ASP.NET Core web API that:
   - Exposes REST endpoints for stream information
   - Serves web dashboard
   - Coordinates the services

## API Endpoints

### `GET /`
Returns an HTML dashboard showing all active RTSP streams and usage instructions

### `GET /streams`
Lists all active RTSP streams with metadata

**Response Example:**
```json
{
  "totalStreams": 2,
  "streams": [
    {
      "fileName": "sample.mp4",
      "rtspUrl": "rtsp://localhost:8554/sample",
      "port": 8554,
      "isRunning": true,
      "startTime": "2024-01-15T10:30:00Z",
      "uptime": "00:15:32"
    },
    {
      "fileName": "movie.mkv",
      "rtspUrl": "rtsp://localhost:8554/movie",
      "port": 8554,
      "isRunning": true,
      "startTime": "2024-01-15T10:30:01Z",
      "uptime": "00:15:31"
    }
  ]
}
```

### `GET /stream/{filename}`
Get information about a specific stream

**Example:** `GET /stream/sample.mp4`

**Response:**
```json
{
  "fileName": "sample.mp4",
  "rtspUrl": "rtsp://localhost:8554/sample",
  "port": 8554,
  "isRunning": true,
  "startTime": "2024-01-15T10:30:00Z",
  "uptime": "00:15:32"
}
```

## Configuration

Edit `appsettings.json` to customize:

```json
{
  "VideoDirectory": "videos",
  "RtspBasePort": 8554,
  "Urls": "http://localhost:5000"
}
```

Or set via environment variables:
```bash
# Windows PowerShell
$env:VideoDirectory = "D:\MyVideos"
$env:RtspBasePort = "9000"
dotnet run
```

## Usage Examples

### VLC Media Player

1. Open VLC
2. Go to **Media → Open Network Stream** (Ctrl+N)
3. Enter the RTSP URL: `rtsp://localhost:8554/sample`
4. Click **Play**

### FFplay (Command Line)

```bash
ffplay rtsp://localhost:8554/sample
```

### OBS Studio

1. Add a new **Media Source**
2. Uncheck "Local File"
3. Enter the RTSP URL: `rtsp://localhost:8554/sample`
4. Click OK

### Python with OpenCV

```python
import cv2

stream = cv2.VideoCapture('rtsp://localhost:8554/sample')

while True:
    ret, frame = stream.read()
    if not ret:
        break
    cv2.imshow('RTSP Stream', frame)
    if cv2.waitKey(1) & 0xFF == ord('q'):
        break

stream.release()
cv2.destroyAllWindows()
```

### Node.js with node-rtsp-stream

```javascript
const Stream = require('node-rtsp-stream');

const stream = new Stream({
  name: 'sample',
  streamUrl: 'rtsp://localhost:8554/sample',
  wsPort: 9999
});
```

## Supported Video Formats

- MP4 (.mp4, .m4v)
- MKV (.mkv)
- AVI (.avi)
- MOV (.mov)
- WMV (.wmv)
- FLV (.flv)
- WebM (.webm)
- MPEG (.mpg, .mpeg)
- Transport Stream (.ts)

## FFmpeg Stream Configuration

The server uses the following FFmpeg parameters for optimal RTSP streaming:

- `-re`: Read input at native frame rate (real-time)
- `-stream_loop -1`: Loop the video infinitely
- `-c:v copy`: Copy video codec without re-encoding (best performance)
- `-c:a copy`: Copy audio codec without re-encoding
- `-f rtsp`: Output format as RTSP
- `-rtsp_transport tcp`: Use TCP for reliable transmission

## Requirements

- .NET 9.0 SDK or later
- FFmpeg installed and in system PATH
- Windows

## Build & Publish

```bash
# Development run
dotnet run

# Release build
dotnet build -c Release

# Publish self-contained (Windows)
dotnet publish -c Release -r win-x64 --self-contained

# Publish framework-dependent
dotnet publish -c Release
```

## Troubleshooting

**"FFmpeg not found" error:**
- Verify FFmpeg is installed: `ffmpeg -version`
- Ensure FFmpeg is in your system PATH
- Restart your terminal after adding FFmpeg to PATH

**"MediaMTX download failed":**
- Check your internet connection
- Verify firewall isn't blocking the download
- MediaMTX is downloaded from GitHub releases automatically

**RTSP stream won't play in VLC:**
- Wait 5-10 seconds after server starts for streams to initialize
- Check server logs for errors
- Verify the RTSP URL format: `rtsp://localhost:8554/streamname`
- Try using TCP transport in VLC: Tools → Preferences → Input/Codecs → Network → RTSP TCP
- Ensure port 8554 isn't blocked by firewall

**"Connection refused" error:**
- Ensure the server is running (`dotnet run`)
- Check if MediaMTX started successfully in the logs
- Verify port 8554 is not in use: `netstat -ano | findstr 8554` (Windows) or `lsof -i :8554` (Linux/macOS)

**Videos don't loop:**
- This is normal behavior with the `-stream_loop -1` flag
- If stream stops, check FFmpeg process status via the API `/streams`
- Check video file isn't corrupted

**High CPU usage:**
- The server uses codec copying (`-c copy`) to avoid transcoding
- If CPU is still high, check if videos require transcoding (unsupported codecs)
- Consider using pre-encoded H.264/AAC videos

**Port already in use:**
- Change `RtspBasePort` in `appsettings.json`
- Stop any other RTSP servers running on port 8554
- Kill processes using the port: `taskkill /F /PID <pid>` (Windows)

**Streams show as "running" but won't play:**
- Check FFmpeg logs (set logging level to Debug in appsettings.Development.json)
- Ensure video codecs are compatible (H.264 video recommended)
- Try with a different video file to rule out file issues
- Restart the server

## Security Considerations

- This server is designed for **local development/testing**
- Do not expose RTSP streams directly to the internet without proper security
- Consider using authentication if deploying in production (MediaMTX supports authentication)
- Use firewall rules to restrict access to specific IPs
- RTSP over TCP is used for reliability (more secure than UDP)
- MediaMTX configuration can be customized in `mediamtx/mediamtx.yml`

## Advanced Configuration

### Custom FFmpeg Parameters

Edit [RtspStreamManager.cs](RtspStreamManager.cs#L95) to customize FFmpeg arguments:

```csharp
var arguments = $"-re -stream_loop -1 -i \"{filePath}\" " +
              $"-c:v libx264 -preset ultrafast -tune zerolatency " +  // Transcode if needed
              $"-c:a aac " +                                           // Audio codec
              $"-f rtsp " +
              $"-rtsp_transport tcp " +
              $"\"{rtspUrl}\"";
```

### MediaMTX Configuration

After first run, customize `mediamtx/mediamtx.yml` for advanced features:
- Authentication: Add username/password requirements
- Recording: Enable automatic stream recording
- Multiple protocols: Add HLS, WebRTC support
- Custom paths: Configure specific stream paths

Refer to [MediaMTX documentation](https://github.com/bluenviron/mediamtx) for detailed configuration options.

## License

This is a sample project for demonstration purposes.
