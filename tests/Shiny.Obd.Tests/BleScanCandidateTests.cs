using System;
using System.Collections.Generic;
using System.Reactive;
using Shiny.BluetoothLE;
using Shiny.Obd.Ble;

namespace Shiny.Obd.Tests;

public class BleScanCandidateTests
{
    [Fact]
    public void From_PrefersPeripheralName()
    {
        var result = Advertisement(peripheralName: "VEEPEAK", localName: "OBDII");
        var candidate = BleScanCandidate.From(result);

        Assert.Equal("VEEPEAK", candidate.Name);
    }

    [Fact]
    public void From_FallsBackToLocalName_WhenPeripheralHasNoName()
    {
        // This is the iOS case: CBPeripheral.Name is null while scanning a peripheral that has never
        // been connected to, and the only name available is the one in the advertisement.
        var result = Advertisement(peripheralName: null, localName: "VEEPEAK");
        var candidate = BleScanCandidate.From(result);

        Assert.Equal("VEEPEAK", candidate.Name);
    }

    [Fact]
    public void From_NameIsNull_WhenNeitherSourceHasOne()
    {
        var result = Advertisement(peripheralName: null, localName: null);
        var candidate = BleScanCandidate.From(result);

        Assert.Null(candidate.Name);
    }

    [Fact]
    public void From_CarriesRssiAndAdvertisedServices()
    {
        var result = Advertisement(peripheralName: "VEEPEAK", localName: null, rssi: -62, serviceUuids: ["FFF0"]);
        var candidate = BleScanCandidate.From(result);

        Assert.Equal(-62, candidate.Rssi);
        Assert.NotNull(candidate.ServiceUuids);
        Assert.Equal(["FFF0"], candidate.ServiceUuids);
    }

    [Fact]
    public void Matches_NullFilter_MatchesEverything()
    {
        Assert.True(Candidate("VEEPEAK").Matches(null));
        Assert.True(Candidate(null).Matches(null));
    }

    [Fact]
    public void Matches_IsCaseInsensitivePartialMatch()
    {
        Assert.True(Candidate("VEEPEAK OBDCheck").Matches("veepeak"));
        Assert.True(Candidate("VEEPEAK OBDCheck").Matches("obdcheck"));
    }

    [Fact]
    public void Matches_RejectsOtherNames()
        => Assert.False(Candidate("OBDLink MX+").Matches("veepeak"));

    [Fact]
    public void Matches_UnnamedDevice_FailsAnyFilter()
        => Assert.False(Candidate(null).Matches("veepeak"));

    [Fact]
    public void Matches_UsesAdvertisedName_WhenPeripheralHasNoName()
    {
        var candidate = BleScanCandidate.From(Advertisement(peripheralName: null, localName: "VEEPEAK"));

        Assert.True(candidate.Matches("veepeak"));
    }

    static BleScanCandidate Candidate(string? name)
        => new(new StubPeripheral(null), name, -50, null);

    static ScanResult Advertisement(
        string? peripheralName,
        string? localName,
        int rssi = -50,
        string[]? serviceUuids = null
    ) => new(
        new StubPeripheral(peripheralName),
        rssi,
        new StubAdvertisementData(localName, serviceUuids)
    );

}
