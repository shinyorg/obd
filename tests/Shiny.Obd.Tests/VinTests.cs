using System.Net;
using System.Text;
using Shiny.Obd.Vin;

namespace Shiny.Obd.Tests;

public class VinNumberTests
{
    [Theory]
    [InlineData("1HGCM82633A004352", "1HGCM82633A004352")]
    [InlineData("  1hgcm82633a004352  ", "1HGCM82633A004352")]
    [InlineData("1HGCM82633A004352\r\n", "1HGCM82633A004352")]
    public void Normalize_StripsPaddingAndUpperCases(string input, string expected)
        => Assert.Equal(expected, VinNumber.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_AnswersNullForNothingUsable(string? input)
        => Assert.Null(VinNumber.Normalize(input));

    [Fact]
    public void IsPlausible_AcceptsARealVin()
        => Assert.True(VinNumber.IsPlausible("1HGCM82633A004352"));

    [Fact]
    public void IsPlausible_AcceptsAPaddedRead()
        => Assert.True(VinNumber.IsPlausible(" 1hgcm82633a004352\0"));

    [Theory]
    [InlineData("1HGCM82633A00435")]    // 16 — a truncated read
    [InlineData("1HGCM82633A0043521")]  // 18
    [InlineData(null)]
    [InlineData("")]
    public void IsPlausible_RejectsTheWrongLength(string? vin)
        => Assert.False(VinNumber.IsPlausible(vin));

    /// <summary>
    /// I, O and Q are excluded from the VIN alphabet because they are confusable with 1 and 0 —
    /// seeing one means the read is wrong, not that the vehicle is unusual.
    /// </summary>
    [Theory]
    [InlineData("1HGCM82633A00435I")]
    [InlineData("1HGCM82633A00435O")]
    [InlineData("1HGCM82633A00435Q")]
    [InlineData("1HGCM82633A00435-")]
    public void IsPlausible_RejectsTheDisallowedAlphabet(string vin)
        => Assert.False(VinNumber.IsPlausible(vin));

    /// <summary>
    /// Deliberately not a check-digit validation: the check digit is only mandatory in North
    /// America, so rejecting this legitimate European VIN would be worse than one wasted request.
    /// </summary>
    [Fact]
    public void IsPlausible_DoesNotValidateTheCheckDigit()
        => Assert.True(VinNumber.IsPlausible("WBA3A5C51DF598123"));

    /// <summary>
    /// Every one of these decodes cleanly through vPIC — "Check Digit (9th position) is correct" —
    /// and they are the VINs the sample emulator answers with, so a break here breaks that too.
    /// </summary>
    [Theory]
    [InlineData("2HGFC2F51JH542108", '1')]   // 2018 Honda Civic
    [InlineData("4T1B11HK9KU742051", '9')]   // 2019 Toyota Camry
    [InlineData("WBA5R1C58KA751236", '8')]   // 2019 BMW 330i
    [InlineData("JTDKARFU5G3512804", '5')]   // 2016 Toyota Prius
    [InlineData("3VWRA7AU1FM024518", '1')]   // 2015 VW Golf TDI
    [InlineData("3C6UR5DL7JG264913", '7')]   // 2018 Ram 2500
    [InlineData("1N4AZ1CP9KC310468", '9')]   // 2019 Nissan Leaf
    [InlineData("1G1JC5246W7180425", '6')]   // 1998 Chevrolet Cavalier
    [InlineData("1HGCM82633A004352", '3')]
    public void CalculateCheckDigit_MatchesTheVinItCameFrom(string vin, char expected)
    {
        Assert.Equal(expected, VinNumber.CalculateCheckDigit(vin));
        Assert.True(VinNumber.IsCheckDigitValid(vin));
    }

    /// <summary>Position 9 weighs zero, so the digit already there cannot influence the answer.</summary>
    [Theory]
    [InlineData("2HGFC2F50JH542108")]
    [InlineData("2HGFC2F5XJH542108")]
    public void CalculateCheckDigit_IgnoresWhateverIsAlreadyInPositionNine(string vin)
        => Assert.Equal('1', VinNumber.CalculateCheckDigit(vin));

    /// <summary>A remainder of ten is written X — the one non-digit a check digit can be.</summary>
    [Fact]
    public void CalculateCheckDigit_UsesXForARemainderOfTen()
        => Assert.Equal('X', VinNumber.CalculateCheckDigit("1M8GDM9AXKP042788"));

    [Fact]
    public void CalculateCheckDigit_NormalizesFirst()
        => Assert.Equal('1', VinNumber.CalculateCheckDigit(" 2hgfc2f51jh542108\0"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2HGFC2F51JH54210")]         // 16 — a truncated read
    [InlineData("2HGFC2F51JH54210I")]        // not in the VIN alphabet
    public void CalculateCheckDigit_AnswersNullForAnythingImplausible(string? vin)
    {
        Assert.Null(VinNumber.CalculateCheckDigit(vin));
        Assert.False(VinNumber.IsCheckDigitValid(vin));
    }

    /// <summary>
    /// The whole point of keeping this out of <see cref="VinNumber.IsPlausible"/>: a legitimate
    /// European VIN whose check digit is not maintained still has to pass the plausibility gate.
    /// </summary>
    [Fact]
    public void IsCheckDigitValid_RejectsAVinIsPlausibleAccepts()
    {
        Assert.True(VinNumber.IsPlausible("WBA3A5C51DF598123"));
        Assert.False(VinNumber.IsCheckDigitValid("WBA3A5C51DF598123"));
    }

    /// <summary>Transposing two characters is the error a check digit exists to catch.</summary>
    [Fact]
    public void IsCheckDigitValid_CatchesATransposition()
        => Assert.False(VinNumber.IsCheckDigitValid("2HGFC2F51JH542180"));
}

public class VpicVinDecoderTests
{
    const string Vin = "1HGCM82633A004352";

    static VpicVinDecoder Decoder(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(new StubHttpClientFactory(json, status));

    static string Payload(string results) => $$"""{"Count":1,"Message":"Results returned","Results":[{{results}}]}""";

    [Fact]
    public async Task Decode_MapsTheIdentityFields()
    {
        var decoder = Decoder(Payload("""
            {"Make":"HONDA","Model":"Accord","ModelYear":"2003","Trim":"EX","ErrorCode":"0"}
        """));

        var vehicle = await decoder.Decode(Vin);

        Assert.NotNull(vehicle);
        Assert.Equal("HONDA", vehicle.Make);
        Assert.Equal("Accord", vehicle.Model);
        Assert.Equal(2003, vehicle.ModelYear);
        Assert.Equal("EX", vehicle.Trim);
    }

    [Fact]
    public async Task Decode_MapsThePowertrainAndBody()
    {
        var decoder = Decoder(Payload("""
            {"Make":"MAZDA","Model":"CX-5","ErrorCode":"0","FuelTypePrimary":"Gasoline",
             "EngineCylinders":"4","DisplacementL":"2.5","EngineHP":"187",
             "DriveType":"4WD/4-Wheel Drive","BodyClass":"Sport Utility Vehicle (SUV)",
             "TransmissionStyle":"Automatic"}
        """));

        var vehicle = await decoder.Decode(Vin);

        Assert.NotNull(vehicle);
        Assert.Equal("Gasoline", vehicle.FuelType);
        Assert.Equal(4, vehicle.EngineCylinders);
        Assert.Equal(2.5, vehicle.EngineDisplacementLitres);
        Assert.Equal(187, vehicle.EngineHorsepower);
        Assert.Equal("4WD/4-Wheel Drive", vehicle.DriveType);
        Assert.Equal("Sport Utility Vehicle (SUV)", vehicle.BodyClass);
        Assert.Equal("Automatic", vehicle.TransmissionStyle);
    }

    /// <summary>
    /// vPIC sends an invariant decimal point. Parsing with the current culture on a comma-decimal
    /// machine reads "2.5" as twenty-five and reports a 25-litre engine — which then travels
    /// wherever the caller sends its vehicle description.
    /// </summary>
    [Fact]
    public async Task Decode_ParsesDisplacementInvariantly()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var decoder = Decoder(Payload("""{"Make":"VW","Model":"Golf","DisplacementL":"1.4","ErrorCode":"0"}"""));

            var vehicle = await decoder.Decode(Vin);

            Assert.Equal(1.4, vehicle!.EngineDisplacementLitres);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// "Not Applicable" is vPIC's empty string and is what an ordinary petrol car carries for
    /// electrification. Storing the phrase would put it in front of a user as a specification.
    /// </summary>
    [Theory]
    [InlineData("Not Applicable")]
    [InlineData("Not Available")]
    [InlineData("N/A")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Decode_TreatsThePlaceholdersAsAbsent(string placeholder)
    {
        var decoder = Decoder(Payload(
            $$"""{"Make":"HONDA","Model":"Accord","ElectrificationLevel":"{{placeholder}}","ErrorCode":"0"}"""
        ));

        Assert.Null((await decoder.Decode(Vin))!.Electrification);
    }

    /// <summary>
    /// These end up in front of people and in prompts, where a garbled decode reads as a fact about
    /// the car rather than as bad data.
    /// </summary>
    [Theory]
    [InlineData("EngineCylinders", "402")]
    [InlineData("EngineCylinders", "0")]
    [InlineData("EngineCylinders", "four")]
    [InlineData("ModelYear", "3")]
    [InlineData("ModelYear", "9999")]
    [InlineData("EngineHP", "999999")]
    [InlineData("DisplacementL", "250")]
    [InlineData("DisplacementL", "0")]
    public async Task Decode_DropsAnImplausibleNumber(string field, string value)
    {
        var decoder = Decoder(Payload($$"""{"Make":"HONDA","Model":"Accord","{{field}}":"{{value}}","ErrorCode":"0"}"""));

        var vehicle = await decoder.Decode(Vin);

        Assert.NotNull(vehicle);

        // The decode still lands — one bad field is dropped to null, not a reason to discard a
        // vehicle that was otherwise identified
        Assert.Equal("HONDA", vehicle.Make);
        Assert.Null(ValueOf(vehicle, field));
    }

    /// <summary>The one field the case under test set, as a nullable number.</summary>
    static double? ValueOf(VinVehicle vehicle, string field) => field switch
    {
        "EngineCylinders" => vehicle.EngineCylinders,
        "ModelYear" => vehicle.ModelYear,
        "EngineHP" => vehicle.EngineHorsepower,
        "DisplacementL" => vehicle.EngineDisplacementLitres,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "unmapped field")
    };

    [Fact]
    public async Task Decode_KeepsANumberAtTheEdgeOfItsRange()
    {
        var decoder = Decoder(Payload("""
            {"Make":"HONDA","EngineCylinders":"16","ModelYear":"1900","EngineHP":"2000",
             "DisplacementL":"20","ErrorCode":"0"}
        """));

        var vehicle = await decoder.Decode(Vin);

        Assert.Equal(16, vehicle!.EngineCylinders);
        Assert.Equal(1900, vehicle.ModelYear);
        Assert.Equal(2000, vehicle.EngineHorsepower);
        Assert.Equal(20, vehicle.EngineDisplacementLitres);
    }

    /// <summary>
    /// vPIC answers 200 OK with an error payload for a VIN it cannot parse, so the transport
    /// succeeding says nothing about the decode.
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("11")]
    [InlineData("6,14")]
    public async Task Decode_AnswersNullOnAnErrorPayload(string errorCode)
    {
        var decoder = Decoder(Payload(
            $$"""{"Make":"HONDA","ErrorCode":"{{errorCode}}","ErrorText":"Check Digit calculated"}"""
        ));

        Assert.Null(await decoder.Decode(Vin));
    }

    /// <summary>ErrorCode can be a comma-separated list, so "0" has to be the whole of it.</summary>
    [Fact]
    public async Task Decode_AcceptsACleanErrorCode()
        => Assert.NotNull(await Decoder(Payload("""{"Make":"HONDA","ErrorCode":"0"}""")).Decode(Vin));

    [Fact]
    public async Task Decode_AnswersNullWhenNothingWasIdentified()
        => Assert.Null(await Decoder(Payload("""{"ErrorCode":"0","BodyClass":"Sedan"}""")).Decode(Vin));

    [Fact]
    public async Task Decode_AnswersNullForNoResults()
        => Assert.Null(await Decoder("""{"Count":0,"Results":[]}""").Decode(Vin));

    /// <summary>Not worth a network round trip, and a bad ECU read must not look like a decode failure.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TOOSHORT")]
    [InlineData("1HGCM82633A00435I")]
    public async Task Decode_RefusesAnImplausibleVinWithoutAsking(string? vin)
    {
        var factory = new StubHttpClientFactory(Payload("""{"Make":"HONDA","ErrorCode":"0"}"""));

        Assert.Null(await new VpicVinDecoder(factory).Decode(vin));
        Assert.Equal(0, factory.Requests);
    }

    /// <summary>
    /// Being offline is the ordinary case in a vehicle. A caller enriching a profile in the
    /// background has nowhere to surface an exception, so the contract is that this never throws.
    /// </summary>
    [Fact]
    public async Task Decode_AnswersNullRatherThanThrowingOnATransportFailure()
        => Assert.Null(await new VpicVinDecoder(new ThrowingHttpClientFactory()).Decode(Vin));

    [Fact]
    public async Task Decode_AnswersNullOnAnHttpError()
        => Assert.Null(await Decoder("", HttpStatusCode.ServiceUnavailable).Decode(Vin));

    [Fact]
    public async Task Decode_AnswersNullOnMalformedJson()
        => Assert.Null(await Decoder("not json at all").Decode(Vin));

    [Fact]
    public async Task Decode_NormalisesTheVinIntoTheRequest()
    {
        var factory = new StubHttpClientFactory(Payload("""{"Make":"HONDA","ErrorCode":"0"}"""));

        await new VpicVinDecoder(factory).Decode("  1hgcm82633a004352 ");

        Assert.Contains(Vin, factory.LastUri);
        Assert.StartsWith(VpicVinDecoder.Endpoint, factory.LastUri);
    }

    class StubHttpClientFactory(string json, HttpStatusCode status = HttpStatusCode.OK) : IHttpClientFactory
    {
        public int Requests { get; private set; }
        public string LastUri { get; private set; } = "";

        public HttpClient CreateClient(string name) => new(new StubHandler(this, json, status));

        class StubHandler(StubHttpClientFactory owner, string json, HttpStatusCode status) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                owner.Requests++;
                owner.LastUri = request.RequestUri!.ToString();

                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }
        }
    }

    class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ThrowingHandler());

        class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => throw new HttpRequestException("no network");
        }
    }
}
