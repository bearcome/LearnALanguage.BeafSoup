namespace BeafSoup.Api;

public static class RepositoryPath
{
    public static string Resolve(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var starts = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var start in starts)
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, configuredPath);
                if (Directory.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException($"找不到内容目录：{configuredPath}");
    }
}
