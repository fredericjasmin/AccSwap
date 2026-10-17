using System.Diagnostics;

namespace AccSwap.Core.Processes;

/// <summary>
/// Real implementation against Windows processes.
/// </summary>
public sealed class ProcessController : IProcessController
{
    public async Task EnsureClosedAsync(IReadOnlyList<string> processNames, CancellationToken ct = default)
    {
        foreach (string rawName in processNames)
        {
            string name = Sanitize(rawName);
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(15), ct);
                    }
                    catch (InvalidOperationException)
                    {
                        // Process already exited between enumeration and kill.
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Access denied or already dying: next IsRunning check will report it.
                    }
                }
            }
        }
    }

    public bool IsRunning(string processName) =>
        Process.GetProcessesByName(Sanitize(processName)).Length > 0;

    public Task StartAsync(string exePath, string arguments = "", CancellationToken ct = default)
    {
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Launcher executable not found: {exePath}");
        }

        var info = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = true,
        };
        Process.Start(info);
        return Task.CompletedTask;
    }

    private static string Sanitize(string name) =>
        Path.GetFileNameWithoutExtension(name.Trim());
}
