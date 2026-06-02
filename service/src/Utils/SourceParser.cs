namespace Intropy.BusinessIncidentService.Utils;

public static class SourceParser
{
    public static string Parse(Uri? uri)
    {
        return Parse(uri?.ToString() ?? null);
    }

    public static string Parse(string? source)
    {
        if (string.IsNullOrEmpty(source))
            throw new ArgumentException("Source is null or empty");

        var segments = source.Split(':');
        if (segments.Length != 3)
            throw new ArgumentException("Invalid source format");

        return segments.Last();
    }
}