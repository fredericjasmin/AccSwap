namespace AccSwap.Core.Files;

/// <summary>
/// File writes that never leave a half-written target behind: content is
/// written to a temp file in the same directory and swapped in with
/// File.Replace (when the target exists) or File.Move.
/// </summary>
public static class AtomicFile
{
    private const string TempSuffix = ".glas-tmp";

    public static void WriteAllBytes(string path, byte[] content)
    {
        PrepareDirectory(path);
        string temp = path + TempSuffix;
        File.WriteAllBytes(temp, content);
        Swap(temp, path);
    }

    public static void WriteAllText(string path, string content) =>
        WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(content));

    public static byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public static void Copy(string sourcePath, string destinationPath)
    {
        PrepareDirectory(destinationPath);
        string temp = destinationPath + TempSuffix;
        File.Copy(sourcePath, temp, overwrite: true);
        Swap(temp, destinationPath);
    }

    private static void Swap(string temp, string target)
    {
        try
        {
            if (File.Exists(target))
            {
                File.Replace(temp, target, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, target, overwrite: true);
            }
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort: callers treat delete failures as non-fatal.
        }
    }

    private static void PrepareDirectory(string path)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
