using System.Security.Cryptography;

namespace Pottmayer.UrlShortener.Shortening;

/// <summary>
/// Provisional random base62 code generator for Fase 1. Replaced in Fase 2 by the KGS
/// (base62 over a monotonic counter), which removes the need for a collision check.
/// </summary>
internal static class ShortCode
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int Length = 7;

    public static string NewCode()
    {
        Span<char> buffer = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(buffer);
    }
}
