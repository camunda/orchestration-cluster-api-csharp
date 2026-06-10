using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// End-to-end tests for <see cref="CamundaClient.SearchVariablesAsDtoAsync{T}"/>, exercising the
/// paging loop and request shape against a faked HTTP transport.
/// </summary>
public class SearchVariablesAsDtoTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly CamundaClient _client;

    private static readonly JsonSerializerOptions s_payloadOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public SearchVariablesAsDtoTests()
    {
        _handler = new MockHttpMessageHandler();
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

    public record OrderVars(string OrderId, decimal? Amount);

    [Fact]
    public async Task SearchVariablesAsDto_CollapsesAcrossPages()
    {
        EnqueuePage(items: [("orderId", "\"ord-1\"", "100")], endCursor: "Y3Vyc29yMQ==");
        EnqueuePage(items: [("amount", "42.5", "100")], endCursor: null);

        var map = await _client.SearchVariablesAsDtoAsync<OrderVars>(
            ProcessInstanceKey.AssumeExists("100"));

        var dto = map.Validate();
        Assert.Equal("ord-1", dto.OrderId);
        Assert.Equal(42.5m, dto.Amount);
    }

    [Fact]
    public async Task SearchVariablesAsDto_SendsNameInFilter()
    {
        string? capturedBody = null;
        _handler.Enqueue(async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    PageJson(items: [("orderId", "\"ord-1\"", "100")], endCursor: null),
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
        });

        await _client.SearchVariablesAsDtoAsync<OrderVars>(ProcessInstanceKey.AssumeExists("100"));

        using var doc = JsonDocument.Parse(capturedBody!);

        var inValues = doc.RootElement
            .GetProperty("filter").GetProperty("name").GetProperty("$in")
            .EnumerateArray().Select(e => e.GetString()!)
            .OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["amount", "orderId"], inValues);

        var processInstanceKey = doc.RootElement
            .GetProperty("filter").GetProperty("processInstanceKey").GetProperty("$eq").GetString();
        Assert.Equal("100", processInstanceKey);
    }

    [Fact]
    public async Task SearchVariablesAsDto_StopsWhenCursorRepeats()
    {
        // A server that keeps returning the same cursor must not loop forever.
        EnqueuePage(items: [("orderId", "\"ord-1\"", "100")], endCursor: "c2FtZQ==");
        EnqueuePage(items: [("orderId", "\"ord-1\"", "100")], endCursor: "c2FtZQ==");

        var map = await _client.SearchVariablesAsDtoAsync<OrderVars>(
            ProcessInstanceKey.AssumeExists("100"));

        Assert.Equal("ord-1", map.Validate().OrderId);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task SearchVariablesAsDto_ScopeCollisionThrows()
    {
        EnqueuePage(
            items: [("orderId", "\"a\"", "100"), ("orderId", "\"b\"", "200")],
            endCursor: null);

        await Assert.ThrowsAsync<VariableScopeCollisionException>(() =>
            _client.SearchVariablesAsDtoAsync<OrderVars>(ProcessInstanceKey.AssumeExists("100")));
    }

    [Fact]
    public async Task SearchVariablesAsDto_InvalidPageSizeThrows()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _client.SearchVariablesAsDtoAsync<OrderVars>(
                ProcessInstanceKey.AssumeExists("100"),
                pageSize: 0));
    }

    private void EnqueuePage(
        (string Name, string Value, string ScopeKey)[] items,
        string? endCursor)
    {
        _handler.Enqueue(HttpStatusCode.OK, PageJson(items, endCursor));
    }

    private static string PageJson(
        (string Name, string Value, string ScopeKey)[] items,
        string? endCursor)
    {
        var payload = new
        {
            items = items.Select(i => new
            {
                value = i.Value,
                name = i.Name,
                tenantId = "<default>",
                variableKey = "1",
                scopeKey = i.ScopeKey,
                processInstanceKey = "100",
                isTruncated = false,
            }).ToArray(),
            page = new
            {
                totalItems = items.Length,
                hasMoreTotalItems = false,
                endCursor,
            },
        };

        return JsonSerializer.Serialize(payload, s_payloadOptions);
    }
}
