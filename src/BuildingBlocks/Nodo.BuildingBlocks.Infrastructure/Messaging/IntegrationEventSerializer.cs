using System.Reflection;
using System.Text.Json;
using Nodo.BuildingBlocks.Application;

namespace Nodo.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Serializes integration events to the outbox and back. The contract's type name is the discriminator, so renaming
/// a contract breaks delivery of pending messages; the round-trip unit tests guard every contract.
/// </summary>
public sealed class IntegrationEventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyDictionary<string, Type> _types;

    /// <param name="contractAssemblies">The <c>*.Contracts</c> assemblies that publish integration events.</param>
    public IntegrationEventSerializer(IEnumerable<Assembly> contractAssemblies)
    {
        _types = contractAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IIntegrationEvent)))
            .ToDictionary(t => t.Name);
    }

    public IReadOnlyCollection<Type> KnownTypes => _types.Values.ToList();

    public (string Type, string Content) Serialize(IIntegrationEvent integrationEvent)
    {
        var type = integrationEvent.GetType();
        if (!_types.ContainsKey(type.Name))
            throw new InvalidOperationException($"'{type.Name}' is not a registered integration event contract.");

        return (type.Name, JsonSerializer.Serialize(integrationEvent, type, Options));
    }

    public IIntegrationEvent Deserialize(string typeName, string content)
    {
        if (!_types.TryGetValue(typeName, out var type))
            throw new InvalidOperationException($"Unknown integration event type '{typeName}'.");

        return (IIntegrationEvent?)JsonSerializer.Deserialize(content, type, Options)
            ?? throw new InvalidOperationException($"The content of '{typeName}' deserialized to null.");
    }
}
