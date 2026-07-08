using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Core;

/// <summary>
/// Manages the EasyTier core subprocess (easytier-core.exe).
/// Spawns, monitors, and terminates the process.
/// The room secret is NEVER passed on the command line — it lives in the config file.
/// </summary>
public sealed class EasyTierProcessService : IDisposable
{
    private Process? _process;
    private readonly string _easytierCorePath;
    private readonly string _logDir;
    private readonly ILogger<EasyTierProcessService> _logger;
    private bool _disposed;

    // Regex to redact --network-secret or network_secret values from logs
    private static readonly Regex SecretRedactRegex = new(
        @"(--network-secret\s+)(\S+)|(network_secret\s*=\s*""?)([^""\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Whether the EasyTier core process is currently running.
    /// </summary>
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// The process ID of the running EasyTier core, or null if not running.
    /// </summary>
    public int? ProcessId => _process?.Id;

    /// <summary>
    /// Raised when the process exits unexpectedly.
    /// </summary>
    public event EventHandler<int>? UnexpectedExit;

    public EasyTierProcessService(string easytierCorePath, string logDir, ILogger<EasyTierProcessService> logger)
    {
        _easytierCorePath = easytierCorePath;
        _logDir = logDir;
        _logger = logger;
    }

    /// <summary>
    /// Starts easytier-core.exe with the given config file.
    /// The config file contains the network secret — it is NEVER passed as a CLI argument.
    /// </summary>
    public Task StartAsync(string configFilePath, string rpcPortal, string devName, IEnumerable<string>? extraArgs = null, CancellationToken ct = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("EasyTier core is already running.");

        if (!File.Exists(_easytierCorePath))
            throw new FileNotFoundException($"EasyTier core not found: {_easytierCorePath}");

        if (!File.Exists(configFilePath))
            throw new FileNotFoundException($"Config file not found: {configFilePath}");

        Directory.CreateDirectory(_logDir);
        var logFile = Path.Combine(_logDir, $"easytier-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        var args = $"--config-file \"{configFilePath}\" --rpc-portal {rpcPortal} --dev-name \"{devName}\"";
        if (extraArgs != null)
            foreach (var a in extraArgs) args += $" {a}";

        _logger.LogInformation("Starting EasyTier core: {Path} --config-file {Config} --rpc-portal {Portal} --dev-name {Dev}",
            _easytierCorePath, configFilePath, rpcPortal, devName);

        var psi = new ProcessStartInfo
        {
            FileName = _easytierCorePath,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(_easytierCorePath) ?? "",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _process.OutputDataReceived += (_, e) => LogSanitized(e.Data, logFile, "OUT");
        _process.ErrorDataReceived += (_, e) => LogSanitized(e.Data, logFile, "ERR");

        _process.Exited += (_, _) =>
        {
            var exitCode = _process.ExitCode;
            _logger.LogWarning("EasyTier core exited with code {ExitCode}", exitCode);
            UnexpectedExit?.Invoke(this, exitCode);
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        _logger.LogInformation("EasyTier core started: PID {Pid}", _process.Id);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the EasyTier core process gracefully, falling back to kill.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_process == null) return;

        try
        {
            if (!_process.HasExited)
            {
                _logger.LogInformation("Stopping EasyTier core (PID {Pid})...", _process.Id);

                // Try graceful close first
                _process.CloseMainWindow();

                // Wait briefly, then kill
                var exited = _process.WaitForExit(3000);
                if (!exited && !_process.HasExited)
                {
                    _logger.LogWarning("EasyTier core did not exit gracefully, killing...");
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping EasyTier core");
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }

        await Task.CompletedTask;
    }

    private void LogSanitized(string? data, string logFile, string stream)
    {
        if (data == null) return;

        // Redact network secret from logs
        var sanitized = SecretRedactRegex.Replace(data, match =>
        {
            if (match.Groups[1].Success)
                return $"{match.Groups[1].Value}[REDACTED]";
            if (match.Groups[3].Success)
                return $"{match.Groups[3].Value}[REDACTED]";
            return match.Value;
        });

        File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] [{stream}] {sanitized}{Environment.NewLine}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_process != null)
        {
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); }
                catch { /* best effort */ }
            }
            _process.Dispose();
            _process = null;
        }
    }
}
