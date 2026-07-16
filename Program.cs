using VideoStreamServer;

var builder = WebApplication.CreateBuilder(args);

// Add MediaMTX RTSP Server Manager (must start first)
builder.Services.AddSingleton<MediaMtxManager>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<MediaMtxManager>());

// Add RTSP Stream Manager (depends on MediaMTX)
builder.Services.AddSingleton<RtspStreamManager>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<RtspStreamManager>());

var app = builder.Build();

Console.WriteLine("===========================================");
Console.WriteLine("       RTSP Video Streaming Server");
Console.WriteLine("===========================================");

// List all RTSP streams endpoint
app.MapGet("/streams", (RtspStreamManager streamManager) =>
{
    var streams = streamManager.GetAllStreams()
        .Select(s => new
        {
            fileName = s.FileName,
            rtspUrl = s.RtspUrl,
            port = s.Port,
            isRunning = s.IsRunning,
            startTime = s.StartTime,
            uptime = DateTime.UtcNow - s.StartTime
        })
        .ToList();

    return Results.Ok(new
    {
        totalStreams = streams.Count,
        streams
    });
});

// Get specific stream info
app.MapGet("/stream/{filename}", (string filename, RtspStreamManager streamManager) =>
{
    var stream = streamManager.GetStream(filename);

    if (stream == null)
    {
        return Results.NotFound(new { error = "Stream not found" });
    }

    return Results.Ok(new
    {
        fileName = stream.FileName,
        rtspUrl = stream.RtspUrl,
        port = stream.Port,
        isRunning = stream.IsRunning,
        startTime = stream.StartTime,
        uptime = DateTime.UtcNow - stream.StartTime
    });
});

// List all available video files
app.MapGet("/videos", (RtspStreamManager streamManager) =>
{
    var videos = streamManager.GetAvailableVideoFiles()
        .Select(v => new
        {
            fileName = v.FileName,
            size = v.Size,
            isStreaming = v.IsStreaming,
            rtspUrl = v.IsStreaming ? $"rtsp://localhost:8554/{Path.GetFileNameWithoutExtension(v.FileName).Replace(" ", "_").Replace(".", "_")}" : null
        })
        .ToList();

    return Results.Ok(new
    {
        totalVideos = videos.Count,
        videos
    });
});

// Start a specific stream
app.MapPost("/stream/start/{filename}", async (string filename, RtspStreamManager streamManager) =>
{
    var success = await streamManager.StartStreamByNameAsync(filename);

    if (!success)
    {
        return Results.BadRequest(new { error = $"Failed to start stream for '{filename}'. Check if file exists and is not already streaming." });
    }

    return Results.Ok(new { message = $"Stream '{filename}' started successfully" });
});

// Stop a specific stream
app.MapPost("/stream/stop/{filename}", (string filename, RtspStreamManager streamManager) =>
{
    var success = streamManager.StopStreamByName(filename);

    if (!success)
    {
        return Results.BadRequest(new { error = $"Failed to stop stream for '{filename}'. Stream may not be running." });
    }

    return Results.Ok(new { message = $"Stream '{filename}' stopped successfully" });
});

// Restart a specific stream
app.MapPost("/stream/restart/{filename}", async (string filename, RtspStreamManager streamManager) =>
{
    var success = await streamManager.RestartStreamAsync(filename);

    if (!success)
    {
        return Results.BadRequest(new { error = $"Failed to restart stream for '{filename}'" });
    }

    return Results.Ok(new { message = $"Stream '{filename}' restarted successfully" });
});

// Stop all streams
app.MapPost("/streams/stop-all", (RtspStreamManager streamManager) =>
{
    streamManager.StopAllStreams();
    return Results.Ok(new { message = "All streams stopped successfully" });
});

// Start all streams
app.MapPost("/streams/start-all", async (RtspStreamManager streamManager) =>
{
    await streamManager.StartAllAvailableStreamsAsync();
    var count = streamManager.GetAllStreams().Count();
    return Results.Ok(new { message = $"Started {count} stream(s)" });
});

// Set baseline encoding for a stream (restarts it if running)
app.MapPost("/stream/baseline/{filename}", async (string filename, BaselineRequest req, RtspStreamManager streamManager) =>
{
    await streamManager.SetBaselineAsync(filename, req.Enable);
    var action = req.Enable ? "switched to Baseline" : "reset to original codec";
    return Results.Ok(new { message = $"Stream '{filename}' {action}" });
});

// Root endpoint with usage info
app.MapGet("/", (RtspStreamManager streamManager) =>
{
    var streams = streamManager.GetAllStreams().ToList();
    var allVideos = streamManager.GetAvailableVideoFiles().ToList();

    var streamsList = string.Join("", allVideos.Select(v =>
    {
        var streamName = Path.GetFileNameWithoutExtension(v.FileName).Replace(" ", "_").Replace(".", "_");
        var rtspUrl = $"rtsp://localhost:8554/{streamName}";
        var isRunning = v.IsStreaming;
        var sizeStr = FormatBytes(v.Size);
        var isBaseline = streamManager.IsBaseline(v.FileName);
        var checkedAttr = isBaseline ? "checked" : "";

        return $@"<div class='stream'>
            <strong>{v.FileName}</strong> <span class='size'>({sizeStr})</span><br>
            <code>{rtspUrl}</code><br>
            <span class='status {(isRunning ? "running" : "stopped")}'>{(isRunning ? "Running" : "Stopped")}</span>
            <div class='controls'>
                {(isRunning ?
                    $@"<button onclick='controlStream(""{v.FileName}"", ""stop"")'>Stop</button>
                       <button onclick='controlStream(""{v.FileName}"", ""restart"")'>Restart</button>" :
                    $@"<button onclick='controlStream(""{v.FileName}"", ""start"")'>Start</button>")}
                <label class='baseline-label'>
                    <input type='checkbox' {checkedAttr} onchange='setBaseline(""{v.FileName}"", this.checked)'>
                    Run as Baseline
                </label>
            </div>
        </div>";
    }));

    return Results.Content($@"
<!DOCTYPE html>
<html>
<head>
    <title>RTSP Control Panel</title>
    <style>
        body {{ font-family: Arial, sans-serif; max-width: 1000px; margin: 50px auto; padding: 20px; background: #f5f5f5; }}
        h1 {{ color: #333; border-bottom: 3px solid #007acc; padding-bottom: 10px; }}
        h2 {{ color: #555; margin-top: 30px; }}
        code {{ background: #e8e8e8; padding: 4px 8px; border-radius: 4px; font-size: 14px; }}
        .endpoint {{ background: white; padding: 20px; margin: 15px 0; border-left: 4px solid #007acc; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .stream {{ background: white; padding: 15px; margin: 10px 0; border-left: 4px solid #28a745; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .status {{ display: inline-block; margin-top: 5px; font-weight: bold; }}
        .status.running {{ color: #28a745; }}
        .status.stopped {{ color: #dc3545; }}
        .size {{ color: #666; font-size: 0.9em; }}
        .controls {{ margin-top: 10px; }}
        .controls button {{
            background: #007acc;
            color: white;
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
            cursor: pointer;
            margin-right: 5px;
            font-size: 14px;
        }}
        .controls button:hover {{ background: #005a9e; }}
        .global-controls {{
            background: white;
            padding: 15px;
            margin: 20px 0;
            border-left: 4px solid #ffc107;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }}
        .global-controls button {{
            background: #ffc107;
            color: #333;
            border: none;
            padding: 10px 20px;
            border-radius: 4px;
            cursor: pointer;
            margin-right: 10px;
            font-weight: bold;
        }}
        .global-controls button:hover {{ background: #e0a800; }}
        .global-controls button.danger {{ background: #dc3545; color: white; }}
        .global-controls button.danger:hover {{ background: #c82333; }}
        #message {{
            display: none;
            padding: 15px;
            margin: 15px 0;
            border-radius: 4px;
            font-weight: bold;
        }}
        #message.success {{ background: #d4edda; color: #155724; border: 1px solid #c3e6cb; display: block; }}
        #message.error {{ background: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; display: block; }}
        .baseline-label {{ margin-left: 10px; font-size: 14px; cursor: pointer; color: #555; }}
        .baseline-label input {{ cursor: pointer; }}
    </style>
</head>
<body>
    <h1>RTSP Video Streaming Control Panel</h1>
    <p>Control and monitor RTSP streams from video files.</p>

    <div id='message'></div>

    <div class='global-controls'>
        <strong>Global Controls:</strong><br><br>
        <button onclick='startAllStreams()'>Start All</button>
        <button onclick='stopAllStreams()' class='danger'>Stop All</button>
        <button onclick='window.location.reload()'>Refresh</button>
    </div>

    <h2>Available Videos ({allVideos.Count})</h2>
    {(allVideos.Any() ? streamsList : "<p>No video files found in the videos directory.</p>")}

    <h2>API Endpoints</h2>
    <div class='endpoint'>
        <strong>GET /streams</strong> - List active streams<br>
        <strong>GET /videos</strong> - List all video files<br>
        <strong>POST /stream/start/{{filename}}</strong> - Start stream<br>
        <strong>POST /stream/stop/{{filename}}</strong> - Stop stream<br>
        <strong>POST /stream/restart/{{filename}}</strong> - Restart stream
    </div>

    <script>
        function showMessage(msg, isError = false) {{
            const el = document.getElementById('message');
            el.textContent = msg;
            el.className = isError ? 'error' : 'success';
            setTimeout(() => {{ el.style.display = 'none'; }}, 5000);
        }}

        async function controlStream(filename, action) {{
            try {{
                const response = await fetch(`/stream/${{action}}/${{encodeURIComponent(filename)}}`, {{
                    method: 'POST'
                }});
                const data = await response.json();

                if (response.ok) {{
                    showMessage(data.message);
                    setTimeout(() => window.location.reload(), 1000);
                }} else {{
                    showMessage(data.error || 'Operation failed', true);
                }}
            }} catch (err) {{
                showMessage('Network error: ' + err.message, true);
            }}
        }}

        async function setBaseline(filename, enable) {{
            try {{
                const response = await fetch(`/stream/baseline/${{encodeURIComponent(filename)}}`, {{
                    method: 'POST',
                    headers: {{ 'Content-Type': 'application/json' }},
                    body: JSON.stringify({{ enable }})
                }});
                const data = await response.json();
                showMessage(data.message + (enable ? ' — restarting with Baseline profile...' : ' — restarting...'));
                setTimeout(() => window.location.reload(), 2500);
            }} catch (err) {{
                showMessage('Error: ' + err.message, true);
            }}
        }}

        async function startAllStreams() {{
            try {{
                const response = await fetch('/streams/start-all', {{ method: 'POST' }});
                const data = await response.json();
                showMessage(data.message);
                setTimeout(() => window.location.reload(), 2000);
            }} catch (err) {{
                showMessage('Error: ' + err.message, true);
            }}
        }}

        async function stopAllStreams() {{
            if (!confirm('Stop all streams?')) return;
            try {{
                const response = await fetch('/streams/stop-all', {{ method: 'POST' }});
                const data = await response.json();
                showMessage(data.message);
                setTimeout(() => window.location.reload(), 1000);
            }} catch (err) {{
                showMessage('Error: ' + err.message, true);
            }}
        }}
    </script>

    <p><a href='/streams'>JSON: Streams</a> | <a href='/videos'>JSON: Videos</a></p>
</body>
</html>
", "text/html");

    string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
});

app.Run();

record BaselineRequest(bool Enable);
