using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// A request body whose type is a <c>[JsonPolymorphic]</c> union must reach the wire with its
/// discriminator. The client's send paths take the body as <c>object</c> and serialize it by its
/// runtime type, so this pins that System.Text.Json still writes the union's discriminator when
/// handed a concrete variant. <c>answerProbe</c> is the operation whose body is such a union
/// (<c>outcome</c> picks the variant), so the endpoint rejects an answer that lacks it.
///
/// The structural test walks every polymorphic type the SDK defines on every send path
/// (<c>SendAsync</c>, <c>SendVoidAsync</c>, <c>SendBinaryAsync</c>), so a union added later is
/// covered without naming its operation.
/// </summary>
public class PolymorphicRequestBodyTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler = new();
    private readonly CamundaClient _client;

    public PolymorphicRequestBodyTests()
    {
        _client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = _handler,
        });
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<string> CaptureBodyAsync(
        Func<Task> send,
        HttpResponseMessage response)
    {
        string? captured = null;
        _handler.Enqueue(async req =>
        {
            captured = await req.Content!.ReadAsStringAsync();
            return response;
        });
        await send();
        Assert.NotNull(captured);
        return captured!;
    }

    public static TheoryData<string, ProbeAnswerRequest, string> ProbeAnswers => new()
    {
        { "OK", new ProbeOkAnswerRequest { Data = new { valid = true } }, "{\"outcome\":\"OK\",\"data\":{\"valid\":true}}" },
        { "ERROR", new ProbeErrorAnswerRequest { Message = "boom" }, "{\"outcome\":\"ERROR\",\"message\":\"boom\"}" },
        { "UNSUPPORTED", new ProbeUnsupportedAnswerRequest(), "{\"outcome\":\"UNSUPPORTED\"}" },
    };

    [Theory]
    [MemberData(nameof(ProbeAnswers))]
    public async Task AnswerProbe_SendsTheVariantWithItsOutcome(
        string name,
        ProbeAnswerRequest body,
        string expectedJson)
    {
        var json = await CaptureBodyAsync(
            () => _client.AnswerProbeAsync(ProbeKey.AssumeExists("2251799813685249"), body),
            new HttpResponseMessage(HttpStatusCode.NoContent));

        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(expectedJson), JsonNode.Parse(json)),
            $"{name} answer body was {json}, expected {expectedJson}");
    }

    /// <summary>Every (derived type, discriminator property, discriminator value) the SDK declares.</summary>
    public static IEnumerable<object[]> PolymorphicVariants()
    {
        foreach (var baseType in typeof(CamundaClient).Assembly.GetTypes())
        {
            var polymorphic = baseType.GetCustomAttribute<JsonPolymorphicAttribute>(inherit: false);
            if (polymorphic is null)
                continue;
            foreach (var derived in baseType.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false))
            {
                yield return new object[]
                {
                    derived.DerivedType.Name,
                    derived.DerivedType,
                    polymorphic.TypeDiscriminatorPropertyName ?? "$type",
                    Convert.ToString(derived.TypeDiscriminator, System.Globalization.CultureInfo.InvariantCulture)!,
                };
            }
        }
    }

    [Fact]
    public void PolymorphicVariants_AreDiscovered()
    {
        // Guards the structural tests below against passing vacuously.
        Assert.True(PolymorphicVariants().Count() >= 20);
    }

    private static object CreateInstance(Type type) =>
        Activator.CreateInstance(type, nonPublic: true)
        ?? throw new InvalidOperationException($"Cannot create {type}");

    private static void AssertDiscriminator(string json, string property, string value, string variant)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.True(
            doc.RootElement.TryGetProperty(property, out var actual),
            $"{variant} was sent without its \"{property}\" discriminator: {json}");
        Assert.Equal(value, actual.GetString());
    }

    [Theory]
    [MemberData(nameof(PolymorphicVariants))]
    public async Task EverySendPath_KeepsTheDiscriminator(string variant, Type derived, string property, string value)
    {
        AssertDiscriminator(
            await CaptureBodyAsync(
                () => _client.SendAsync<JsonElement>(HttpMethod.Post, "/x", CreateInstance(derived)),
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                }),
            property, value, $"{variant} via SendAsync");

        AssertDiscriminator(
            await CaptureBodyAsync(
                () => _client.SendVoidAsync(HttpMethod.Post, "/x", CreateInstance(derived)),
                new HttpResponseMessage(HttpStatusCode.NoContent)),
            property, value, $"{variant} via SendVoidAsync");

        AssertDiscriminator(
            await CaptureBodyAsync(
                () => _client.SendBinaryAsync(HttpMethod.Post, "/x", CreateInstance(derived)),
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) }),
            property, value, $"{variant} via SendBinaryAsync");
    }
}
