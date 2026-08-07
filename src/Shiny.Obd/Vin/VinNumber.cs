namespace Shiny.Obd.Vin;

/// <summary>
/// VIN sanity checks. Pure, so the rules are table-tested.
/// </summary>
/// <remarks>
/// Worth doing before spending a network round trip: mode 09 PID 02 comes back over a serial link
/// through an adapter of unknown quality, and a partially-read VIN is common — short, padded with
/// nulls or spaces, or carrying a stray prompt character. A decoder service will typically answer
/// <c>200 OK</c> with an error payload for those, so filtering here keeps a bad read from looking
/// like a decode failure.
/// </remarks>
public static class VinNumber
{
    /// <summary>A VIN is exactly 17 characters.</summary>
    public const int Length = 17;

    /// <summary>
    /// I, O and Q are excluded from the VIN alphabet precisely because they are confusable with
    /// 1 and 0 — seeing one means the read is wrong, not that the vehicle is unusual.
    /// </summary>
    const string DisallowedLetters = "IOQ";

    /// <summary>Trims the padding an adapter may add and upper-cases; null when nothing usable remains.</summary>
    public static string? Normalize(string? vin)
    {
        if (String.IsNullOrWhiteSpace(vin))
            return null;

        var trimmed = new string(vin.Where(c => !Char.IsControl(c) && !Char.IsWhiteSpace(c)).ToArray())
            .ToUpperInvariant();

        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// Whether this looks like a real VIN and is worth sending to a decoder. Deliberately not a
    /// check-digit validation: the check digit is only mandatory in North America, and rejecting a
    /// legitimate non-NA VIN would be worse than one wasted request.
    /// </summary>
    public static bool IsPlausible(string? vin)
    {
        var normalized = Normalize(vin);
        if (normalized is not { Length: Length })
            return false;

        return normalized.All(c =>
            (Char.IsAsciiDigit(c) || Char.IsAsciiLetterUpper(c)) &&
            !DisallowedLetters.Contains(c)
        );
    }
}
