using System.Text;

namespace Pottmayer.UrlShortener.Shortening;

/// <summary>Encodes a counter value (from the KGS) into a base62 short code.</summary>
internal static class Base62
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string Encode(long value)
    {
        if (value <= 0) return "0";

        var builder = new StringBuilder();
        while (value > 0)
        {
            builder.Insert(0, Alphabet[(int)(value % 62)]);
            value /= 62;
        }
        return builder.ToString();
    }
}
