using System.Reflection;
using Nodo.BuildingBlocks.Application;
using Nodo.BuildingBlocks.Infrastructure.Messaging;

namespace Nodo.Infrastructure.UnitTests;

/// <summary>
/// Outbox messages are stored as JSON and read back later (possibly after a deployment). Every contract must survive
/// that round trip; a renamed or reshaped property that the serializer cannot read back fails here, not in production.
/// </summary>
public class IntegrationEventContractTests
{
    private static readonly Assembly[] ContractAssemblies =
    [
        typeof(Nodo.Quotes.Contracts.QuoteIssuedIntegrationEvent).Assembly,
        typeof(Nodo.EventLifecycle.Contracts.EventRequestCreatedIntegrationEvent).Assembly,
        typeof(Nodo.HallSchedule.Contracts.SlotReleasedIntegrationEvent).Assembly
    ];

    private static readonly IntegrationEventSerializer Serializer = new(ContractAssemblies);

    public static TheoryData<string> ContractNames()
    {
        var data = new TheoryData<string>();
        foreach (var type in Serializer.KnownTypes.OrderBy(t => t.Name))
            data.Add(type.Name);
        return data;
    }

    private static Type Contract(string name) => Serializer.KnownTypes.Single(t => t.Name == name);

    [Fact]
    public void Serializer_KnowsEveryIntegrationEventOfTheThreeModules()
    {
        // 3 Quotes + 13 EventLifecycle + 3 HallSchedule. A new contract must be added to the round trip below automatically.
        Serializer.KnownTypes.Count.ShouldBe(19);
    }

    [Theory]
    [MemberData(nameof(ContractNames))]
    public void Contract_SurvivesAJsonRoundTrip_WithEveryValueIntact(string contractName)
    {
        var type = Contract(contractName);
        var original = (IIntegrationEvent)SampleFactory.Create(type);

        var (typeName, content) = Serializer.Serialize(original);
        var restored = Serializer.Deserialize(typeName, content);

        typeName.ShouldBe(contractName);
        restored.GetType().ShouldBe(type);
        restored.Id.ShouldBe(original.Id);
        restored.OccurredOnUtc.ShouldBe(original.OccurredOnUtc);
        Serializer.Serialize(restored).Content.ShouldBe(content); // nothing was lost or changed on the way back
    }

    [Theory]
    [MemberData(nameof(ContractNames))]
    public void Contract_RestoredEventHasNoDefaultValues_SoNothingWasSilentlyDropped(string contractName)
    {
        var type = Contract(contractName);
        var original = (IIntegrationEvent)SampleFactory.Create(type);

        var restored = Serializer.Deserialize(contractName, Serializer.Serialize(original).Content);

        foreach (var property in type.GetProperties())
        {
            var value = property.GetValue(restored);
            value.ShouldNotBeNull(property.Name);
            if (value is not System.Collections.IEnumerable and not string)
                value.ShouldNotBe(Activator.CreateInstance(property.PropertyType), $"{type.Name}.{property.Name} came back as its default");
        }
    }

    [Theory]
    [MemberData(nameof(ContractNames))]
    public void Contract_UsesOnlyPrimitivesAndSimpleDtos(string contractName)
    {
        // Published Language: no domain types, no ids wrapped in structs, nothing a consumer would need our assembly for.
        foreach (var property in Contract(contractName).GetProperties())
            IsPublishedLanguageType(property.PropertyType).ShouldBeTrue($"{contractName}.{property.Name}: {property.PropertyType}");
    }

    private static bool IsPublishedLanguageType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        var simple = new[]
        {
            typeof(Guid), typeof(string), typeof(int), typeof(decimal), typeof(bool), typeof(DateTime), typeof(DateOnly), typeof(TimeOnly)
        };
        if (simple.Contains(type))
            return true;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>))
            return IsPublishedLanguageType(type.GetGenericArguments()[0]);

        // A DTO declared in a Contracts assembly whose own properties are published-language types.
        return ContractAssemblies.Contains(type.Assembly) && type.GetProperties().All(p => IsPublishedLanguageType(p.PropertyType));
    }

    [Fact]
    public void Serialize_UnregisteredType_IsRejected()
    {
        var stranger = new StrangerEvent(Guid.NewGuid(), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

        Should.Throw<InvalidOperationException>(() => Serializer.Serialize(stranger));
    }

    [Fact]
    public void Deserialize_UnknownTypeName_IsRejected() =>
        Should.Throw<InvalidOperationException>(() => Serializer.Deserialize("NoSuchEvent", "{}"));

    [Fact]
    public void Deserialize_NullContent_IsRejected() =>
        Should.Throw<InvalidOperationException>(() => Serializer.Deserialize("QuoteExpiredIntegrationEvent", "null"));

    private sealed record StrangerEvent(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;
}

/// <summary>Builds an instance of a contract with a distinct, non-default value in every property.</summary>
internal static class SampleFactory
{
    private static int _counter;

    public static object Create(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type; // a nullable field is sampled with a value
        if (type == typeof(Guid)) return new Guid(++_counter, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        if (type == typeof(string)) return $"sample-{++_counter}";
        if (type == typeof(int)) return 7 + ++_counter;
        if (type == typeof(decimal)) return 123.45m + ++_counter;
        if (type == typeof(bool)) return true;
        if (type == typeof(DateTime)) return new DateTime(2026, 11, 1, 12, 30, 15, DateTimeKind.Utc).AddMinutes(++_counter);
        if (type == typeof(DateOnly)) return new DateOnly(2026, 11, 20).AddDays(++_counter);
        if (type == typeof(TimeOnly)) return new TimeOnly(10, 0).AddHours(++_counter % 5);

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>))
        {
            var item = type.GetGenericArguments()[0];
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item))!;
            list.Add(Create(item));
            list.Add(Create(item));
            return list;
        }

        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        return constructor.Invoke(constructor.GetParameters().Select(p => Create(p.ParameterType)).ToArray());
    }
}
