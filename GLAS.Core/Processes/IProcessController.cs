namespace GLAS.Core.Processes;

/// <summary>
/// Abstraction over launcher processes so the core can be tested without
/// touching real running applications.
/// </summary>
public interface IProcessController
{
    /// <summary>Kills every running process whose name (no extension) is in the list and waits for exit.</summary>
    Task EnsureClosedAsync(IReadOnlyList<string> processNames, CancellationToken ct = default);

    /// <summary>True when at least one process with the given name (no extension) is running.</summary>
    bool IsRunning(string processName);

    Task StartAsync(string exePath, string arguments = "", CancellationToken ct = default);
}
