// Compilable usage examples for message and signal operations.
// Region tags are referenced by DocFX overwrite files.
#pragma warning disable CS8321 // Local function is declared but never used

using Camunda.Orchestration.Sdk;
using Camunda.Orchestration.Sdk.Api;

namespace Camunda.Orchestration.Sdk.Examples;

internal static class MessageExamples
{
    // <CorrelateMessage>
    static async Task CorrelateMessageExample()
    {
        using var client = Camunda.CreateClient();

        var result = await client.CorrelateMessageAsync(new MessageCorrelationRequest
        {
            Name = "payment-received",
            CorrelationKey = "ORD-12345",
            Variables = new Dictionary<string, object>
            {
                ["paymentId"] = "PAY-98765",
                ["amount"] = 99.95
            }
        });

        Console.WriteLine($"Message key: {result.MessageKey}");
    }
    // </CorrelateMessage>

    // <PublishMessage>
    static async Task PublishMessageExample()
    {
        using var client = Camunda.CreateClient();

        await client.PublishMessageAsync(new MessagePublicationRequest
        {
            Name = "order-placed",
            CorrelationKey = "ORD-12345",
            TimeToLive = 60_000, // 1 minute
            Variables = new Dictionary<string, object>
            {
                ["orderId"] = "ORD-12345"
            }
        });
    }
    // </PublishMessage>

    // <BroadcastSignal>
    static async Task BroadcastSignalExample()
    {
        using var client = Camunda.CreateClient();

        var result = await client.BroadcastSignalAsync(new SignalBroadcastRequest
        {
            SignalName = "system-shutdown",
            Variables = new Dictionary<string, object>
            {
                ["reason"] = "maintenance"
            }
        });

        Console.WriteLine($"Signal key: {result.SignalKey}");
    }
    // </BroadcastSignal>
}
