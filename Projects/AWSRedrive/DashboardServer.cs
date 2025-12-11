using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NLog.Web;

namespace AWSRedrive
{
    public class DashboardServer
    {
        private readonly IConfigurationReader _configReader;
        private readonly IOrchestrator _orchestrator;
        private readonly DashboardSettings _settings;
        private WebApplication _app;
        private byte[] _cachedHtml;
        private byte[] _cachedHtmlGzip;
        private CancellationTokenSource _cts;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public DashboardServer(IConfigurationReader configReader, IOrchestrator orchestrator, DashboardSettings settings)
        {
            _configReader = configReader;
            _orchestrator = orchestrator;
            _settings = settings ?? new DashboardSettings();
            LoadHtmlContent();
        }

        public DashboardServer(IConfigurationReader configReader, DashboardSettings settings)
            : this(configReader, null, settings)
        {
        }

        private void LoadHtmlContent()
        {
            var htmlPath = Path.Combine(AppContext.BaseDirectory, "dashboard.html");
            if (File.Exists(htmlPath))
            {
                var html = File.ReadAllText(htmlPath);
                _cachedHtml = Encoding.UTF8.GetBytes(html);

                using var ms = new MemoryStream();
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
                {
                    gz.Write(_cachedHtml, 0, _cachedHtml.Length);
                }
                _cachedHtmlGzip = ms.ToArray();
            }
            else
            {
                var fallback = "<html><body><h1>Dashboard</h1><p>dashboard.html not found</p></body></html>";
                _cachedHtml = Encoding.UTF8.GetBytes(fallback);
                _cachedHtmlGzip = _cachedHtml;
            }
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();

            var builder = WebApplication.CreateBuilder();

            // Clear default logging providers and use only NLog
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            
            // Configure Kestrel
            builder.WebHost.UseKestrel(options =>
            {
                options.ListenAnyIP(_settings.Port);
            });

            // Route ASP.NET Core logs through NLog (JSON formatted)
            builder.Host.UseNLog();

            _app = builder.Build();

            _app.MapGet("/", (HttpContext ctx) =>
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";

                if (ctx.Request.Headers.AcceptEncoding.ToString().Contains("gzip"))
                {
                    ctx.Response.Headers.ContentEncoding = "gzip";
                    return Results.Bytes(_cachedHtmlGzip, "text/html");
                }

                return Results.Bytes(_cachedHtml, "text/html");
            });

            _app.MapGet("/api/status", () =>
            {
                var status = GetStatus();
                return Results.Json(status, JsonOptions);
            });

            _app.MapGet("/api/stream", async (HttpContext ctx, CancellationToken ct) =>
            {
                ctx.Response.Headers.ContentType = "text/event-stream";
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);

                try
                {
                    while (!linked.Token.IsCancellationRequested)
                    {
                        var status = GetStatus();
                        var json = JsonSerializer.Serialize(status, JsonOptions);
                        await ctx.Response.WriteAsync($"data: {json}\n\n", linked.Token);
                        await ctx.Response.Body.FlushAsync(linked.Token);
                        await Task.Delay(_settings.RefreshIntervalMs, linked.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown
                }
            });

            _app.MapPost("/api/loglevel/{alias}", (string alias, HttpContext ctx) =>
            {
                if (_orchestrator == null)
                {
                    return Results.Json(new { success = false, error = "Orchestrator not available" }, JsonOptions, statusCode: 503);
                }

                var level = ctx.Request.Query["level"].ToString();

                if (string.IsNullOrWhiteSpace(level))
                {
                    return Results.Json(new { success = false, error = "Missing 'level' query parameter" }, JsonOptions, statusCode: 400);
                }

                var validLevels = new[] { "Trace", "Debug", "Info", "Warn", "Error", "Fatal" };
                if (!validLevels.Contains(level, StringComparer.OrdinalIgnoreCase))
                {
                    return Results.Json(new { success = false, error = $"Invalid level. Valid: {string.Join(", ", validLevels)}" }, JsonOptions, statusCode: 400);
                }

                var success = _orchestrator.SetLogLevel(alias, level);

                if (success)
                {
                    return Results.Json(new { success = true, alias, level }, JsonOptions);
                }

                return Results.Json(new { success = false, error = $"Alias '{alias}' not found" }, JsonOptions, statusCode: 404);
            });

            _app.MapGet("/api/loglevel/{alias}", (string alias) =>
            {
                if (_orchestrator == null)
                {
                    return Results.Json(new { success = false, error = "Orchestrator not available" }, JsonOptions, statusCode: 503);
                }

                var level = _orchestrator.GetLogLevel(alias);

                if (level != null)
                {
                    return Results.Json(new { success = true, alias, level }, JsonOptions);
                }

                return Results.Json(new { success = false, error = $"Alias '{alias}' not found" }, JsonOptions, statusCode: 404);
            });

            Task.Run(() => _app.RunAsync());
        }

        public void Stop()
        {
            _cts?.Cancel();

            if (_app != null)
            {
                try
                {
                    var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    _app.StopAsync(stopCts.Token).Wait();
                }
                catch
                {
                    // Ignore shutdown errors
                }
            }
        }

        private List<object> GetStatus()
        {
            var configs = _configReader.ReadConfiguration();
            var result = new List<object>();

            foreach (var config in configs)
            {
                var metrics = MetricsStore.GetOrCreate(config.Alias);
                var currentLogLevel = _orchestrator?.GetLogLevel(config.Alias) ?? config.LogLevel ?? "Error";
                var uptimeSeconds = metrics.StartedAt == default
                    ? 0
                    : (int)(DateTime.UtcNow - metrics.StartedAt).TotalSeconds;

                result.Add(new
                {
                    config.Alias,
                    config.QueueUrl,
                    config.Region,
                    config.RedriveUrl,
                    config.RedriveScript,
                    config.RedriveKafkaTopic,
                    config.Active,
                    config.Timeout,
                    LogLevel = currentLogLevel,
                    config.UseGET,
                    config.UsePUT,
                    config.UseDelete,
                    HasAccessKey = !string.IsNullOrEmpty(config.AccessKey),
                    HasAuthToken = !string.IsNullOrEmpty(config.AuthToken),
                    HasBasicAuth = !string.IsNullOrEmpty(config.BasicAuthUserName),
                    HasAwsGatewayToken = !string.IsNullOrEmpty(config.AwsGatewayToken),
                    Metrics = new
                    {
                        metrics.StartedAt,
                        metrics.MessagesReceived,
                        metrics.MessagesSent,
                        metrics.MessagesFailed,
                        metrics.LastMessageReceived,
                        metrics.LastMessageSent,
                        metrics.LastError,
                        metrics.LastErrorMessage,
                        metrics.LastMessageContent,
                        UptimeSeconds = uptimeSeconds
                    }
                });
            }

            return result;
        }
    }
}
