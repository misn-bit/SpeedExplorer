using System;

namespace SpeedExplorer;

internal static class ImageViewerOcrCachePayload
{
    public static string ExtractJsonPayload(string raw, string separator)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        int separatorIndex = raw.IndexOf(separator, StringComparison.Ordinal);
        if (separatorIndex >= 0)
        {
            int jsonStart = separatorIndex + separator.Length;
            while (jsonStart < raw.Length &&
                   (raw[jsonStart] == '\r' || raw[jsonStart] == '\n' || char.IsWhiteSpace(raw[jsonStart])))
            {
                jsonStart++;
            }

            if (jsonStart < raw.Length)
                return raw.Substring(jsonStart);
        }

        int firstBrace = raw.IndexOf('{');
        return firstBrace > 0 ? raw.Substring(firstBrace) : raw;
    }
}
