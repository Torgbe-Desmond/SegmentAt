using System.Net;

namespace SegmentAPI.Extensions;

public static class CookieLoader
{
    public static List<Cookie> LoadFromNetscapeFile(string path)
    {
        var cookies = new List<Cookie>();

        if (!File.Exists(path))
            throw new FileNotFoundException($"Cookie file not found at '{path}'.");

        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) || (line.StartsWith('#') && !line.StartsWith("#HttpOnly_")))
                continue;

            if (line.StartsWith("#HttpOnly_"))
                line = line["#HttpOnly_".Length..];

            string[] parts = line.Split('\t');
            if (parts.Length < 7)
                continue;

            string domain = parts[0];
            string path_ = parts[2];
            bool secure = parts[3].Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            string name = parts[5];
            string value = parts[6];

            cookies.Add(new Cookie(name, value, path_, domain.TrimStart('.'))
            {
                Secure = secure
            });
        }

        return cookies;
    }
}