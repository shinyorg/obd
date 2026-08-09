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

    /// <summary>The VIN alphabet in order, paired index-for-index with <see cref="LetterValues"/>.</summary>
    const string Letters = "ABCDEFGHJKLMNPRSTUVWXYZ";

    /// <summary>
    /// ISO 3779 transliteration. A-H are 1-8, J-N restart at 1, P is 7, R is 9, and S-Z run 2-9 —
    /// the gaps are where I, O and Q would have been.
    /// </summary>
    static readonly int[] LetterValues = [1, 2, 3, 4, 5, 6, 7, 8, 1, 2, 3, 4, 5, 7, 9, 2, 3, 4, 5, 6, 7, 8, 9];

    /// <summary>Positional weights. Position 9 weighs 0 because that is the check digit itself.</summary>
    static readonly int[] Weights = [8, 7, 6, 5, 4, 3, 2, 10, 0, 9, 8, 7, 6, 5, 4, 3, 2];

    /// <summary>The 1-based position of the check digit.</summary>
    const int CheckDigitPosition = 9;

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

    /// <summary>
    /// The ISO 3779 check digit — position 9 — that the rest of the VIN implies, or null when the
    /// input is not a plausible VIN to begin with. '0'-'9' or 'X', which stands in for a remainder
    /// of ten.
    /// </summary>
    /// <remarks>
    /// Whatever character currently sits in position 9 is ignored: its weight is zero, so a VIN
    /// carrying the wrong check digit still computes the right one. That is what makes this usable
    /// for <i>generating</i> a valid VIN — build the other sixteen characters, then ask for this.
    /// </remarks>
    public static char? CalculateCheckDigit(string? vin)
    {
        var normalized = Normalize(vin);
        if (!IsPlausible(normalized))
            return null;

        var sum = 0;
        for (var i = 0; i < Length; i++)
        {
            var c = normalized![i];
            var value = Char.IsAsciiDigit(c) ? c - '0' : LetterValues[Letters.IndexOf(c)];
            sum += value * Weights[i];
        }

        var remainder = sum % 11;
        return remainder == 10 ? 'X' : (char)('0' + remainder);
    }

    /// <summary>
    /// Whether the VIN's check digit agrees with the rest of it.
    /// </summary>
    /// <remarks>
    /// <b>Opt-in, and deliberately not part of <see cref="IsPlausible"/>.</b> The check digit is
    /// mandatory in North America and merely conventional elsewhere, so plenty of legitimate
    /// European and Asian VINs fail this — do not use it to reject a VIN read off a vehicle. Its
    /// honest uses are the other direction: catching a transcription error in a VIN a user typed,
    /// and generating VINs that a decoder will accept, which is what the sample emulator does.
    /// </remarks>
    public static bool IsCheckDigitValid(string? vin)
    {
        var expected = CalculateCheckDigit(vin);
        return expected != null && Normalize(vin)![CheckDigitPosition - 1] == expected;
    }
}
