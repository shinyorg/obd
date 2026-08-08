using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Shiny.Obd.Wifi;

/// <summary>
/// Finds ELM327 WiFi adapters by probing the well-known endpoints and the current network's gateway.
/// </summary>
/// <remarks>
/// This is a targeted probe of a handful of addresses, not a sweep of the local subnet. Sweeping was
/// considered and rejected: it means opening a couple of hundred sockets across whatever network the
/// user happens to be on, which is both slow and rude, and it buys nothing - these adapters run their
/// own access point and answer on one of a very short list of addresses. Add your own to
/// <see cref="WifiObdConfiguration.EndpointCandidates"/> if you have an odd one.
///
/// <para>
/// Each probe opens a socket, asks ATI, and closes it before moving on. That teardown is not
/// housekeeping - most of these adapters accept exactly one TCP client at a time, so a scanner that
/// held connections open would lock out the transport that is about to connect for real.
/// </para>
///
/// <para>
/// <see cref="ObdDiscoveredDevice.NativeDevice"/> is the <see cref="WifiObdEndpoint"/>, and
/// <see cref="ObdDiscoveredDevice.Id"/> is <c>host:port</c>.
/// </para>
/// </remarks>
public class WifiObdDeviceScanner : IObdDeviceScanner
{
    readonly WifiObdConfiguration config;
    readonly ILogger logger;

    /// <summary>
    /// Creates a scanner. A null configuration uses the defaults, which is the usual case - the
    /// candidate list is the interesting part and it has a sensible default.
    /// </summary>
    public WifiObdDeviceScanner(
        WifiObdConfiguration? config = null,
        ILogger<WifiObdDeviceScanner>? logger = null
    )
    {
        this.config = config ?? new WifiObdConfiguration();
        this.logger = logger ?? NullLogger<WifiObdDeviceScanner>.Instance;
    }

    /// <inheritdoc/>
    public async Task Scan(Action<ObdDiscoveredDevice> onDeviceFound, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(onDeviceFound);

        // An adapter powers up with the ignition and the phone may join its AP seconds later, so this
        // re-probes on an interval rather than returning once - it matches how a BLE scan behaves and
        // lets a picker UI stay live.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!ct.IsCancellationRequested)
        {
            foreach (var candidate in WifiObdProbe.BuildCandidates(this.config))
            {
                if (ct.IsCancellationRequested)
                    return;

                if (seen.Contains(candidate.ToString()))
                    continue;

                string? identity;
                try
                {
                    identity = await WifiObdProbe.Identify(candidate, this.config, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (identity == null)
                    continue;

                if (!seen.Add(candidate.ToString()))
                    continue;

                this.logger.LogDebug("Discovered OBD adapter '{Identity}' at {Endpoint}", identity, candidate);

                // An adapter that answers the prompt but names itself nothing is still an adapter -
                // some clones return an empty ATI. Never surface a blank row to a picker UI.
                var name = String.IsNullOrWhiteSpace(identity) ? $"OBD adapter ({candidate})" : identity;
                onDeviceFound(new ObdDiscoveredDevice(name, candidate.ToString(), candidate));
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
