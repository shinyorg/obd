using System.Text;

namespace Shiny.Obd.Emulator;

/// <summary>
/// The per-connection state a real ELM327 keeps: echo, spaces, headers and linefeeds. Each connected
/// client gets its own, because each runs its own initialisation sequence.
/// </summary>
public sealed class Elm327Session
{
    public Elm327Session(string label) => this.Label = label;

    /// <summary>How the connection is shown in the UI - a BLE central id or a TCP endpoint.</summary>
    public string Label { get; }

    /// <summary>ELM327 powers up echoing commands back. Shiny's profile turns it off with ATE0.</summary>
    public bool Echo { get; set; } = true;

    public bool Spaces { get; set; } = true;
    public bool Headers { get; set; }
    public bool Linefeeds { get; set; }
    public string Protocol { get; set; } = "0";

    /// <summary>A real adapter only prints SEARCHING... while it is still hunting for the bus.</summary>
    public bool HasSearched { get; set; }

    public void Reset()
    {
        this.Echo = true;
        this.Spaces = true;
        this.Headers = false;
        this.Linefeeds = false;
        this.Protocol = "0";
        this.HasSearched = false;
    }

    string Eol => this.Linefeeds ? "\r\n" : "\r";

    /// <summary>
    /// Renders a reply the way the wire carries it: the echoed command if echo is on, the response
    /// lines, then a blank line and the '>' prompt that tells a client the adapter is ready again.
    /// </summary>
    public string Render(ObdExchange exchange)
    {
        var sb = new StringBuilder();

        if (this.Echo)
            sb.Append(exchange.Request).Append(this.Eol);

        foreach (var line in exchange.Lines)
        {
            // Headers on prefixes the responding ECU's CAN id. This is the simplification a simulator
            // can afford: a real adapter also emits the PCI byte, which Shiny never asks for (ATH0).
            if (this.Headers && exchange.IsData)
                sb.Append("7E8 ");

            sb.Append(line).Append(this.Eol);
        }

        sb.Append(this.Eol).Append('>');
        return sb.ToString();
    }
}
