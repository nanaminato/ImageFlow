namespace ImageFlow;

public sealed class ImageStorage(EngineOptions options)
{
    public string Root => Path.GetFullPath(Path.Combine(options.DataDirectory, "images"));
    public string Validate(string path)
    {
        var full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            Path.GetExtension(full) is not (".png" or ".svg") ||
            (Directory.Exists(Root) && (File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0) ||
            (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Image path is not an ordinary file in engine image storage.");
        return full;
    }
    public string FromName(string fileName)
    {
        if (fileName != Path.GetFileName(fileName)) throw new IOException("Invalid image filename.");
        return Validate(Path.Combine(Root, fileName));
    }
}
