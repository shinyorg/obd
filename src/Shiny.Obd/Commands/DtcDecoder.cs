namespace Shiny.Obd.Commands;

/// <summary>
/// Decodes the raw byte pairs of an OBD-II diagnostic trouble code response (modes 03, 07 and 0A)
/// into SAE J2012 code strings such as "P0301".
/// </summary>
public static class DtcDecoder
{
    // Bits 7-6 of the first byte select the system the code belongs to
    static readonly char[] SystemPrefixes = ['P', 'C', 'B', 'U'];

    /// <summary>
    /// Decodes a full mode 03/07/0A response.
    /// </summary>
    /// <param name="response">All response bytes, including the mode echo (0x43/0x47/0x4A).</param>
    /// <param name="responseMode">The expected mode echo byte.</param>
    public static IReadOnlyList<string> Decode(ReadOnlySpan<byte> response, byte responseMode)
    {
        if (response.Length == 0)
            return [];

        var payload = response;
        if (payload[0] == responseMode)
            payload = payload[1..];

        // CAN replies as `43 <dtcCount> <pairs...>`, so the payload length is odd; the older
        // protocols reply as `43 <pairs...>` (always whole pairs, so even). Use that parity to
        // decide whether a count byte is present rather than assuming a transport.
        if (payload.Length % 2 == 1)
            payload = payload[1..];

        if (payload.Length < 2)
            return [];

        var codes = new List<string>(payload.Length / 2);
        for (var i = 0; i + 1 < payload.Length; i += 2)
        {
            var code = DecodePair(payload[i], payload[i + 1]);
            if (code != null)
                codes.Add(code);
        }
        return codes;
    }

    /// <summary>
    /// Decodes a single two-byte code. Returns null for the 0x0000 padding that fills out unused
    /// slots in a fixed-size frame.
    /// </summary>
    public static string? DecodePair(byte a, byte b)
    {
        if (a == 0 && b == 0)
            return null;

        var prefix = SystemPrefixes[(a >> 6) & 0x03];
        var first = (a >> 4) & 0x03;
        var second = a & 0x0F;
        var third = (b >> 4) & 0x0F;
        var fourth = b & 0x0F;

        return String.Create(
            5,
            (prefix, first, second, third, fourth),
            static (span, state) =>
            {
                span[0] = state.prefix;
                span[1] = (char)('0' + state.first);
                span[2] = Hex(state.second);
                span[3] = Hex(state.third);
                span[4] = Hex(state.fourth);
            }
        );
    }

    static char Hex(int value) => (char)(value < 10 ? '0' + value : 'A' + (value - 10));
}
