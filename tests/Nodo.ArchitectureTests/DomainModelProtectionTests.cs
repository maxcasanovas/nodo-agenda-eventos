using System.Reflection;
using Nodo.SharedKernel;

namespace Nodo.ArchitectureTests;

/// <summary>
/// Always-valid domain model: aggregates, entities and value objects cannot be built or mutated from outside
/// except through their factories and behaviour methods. Found by reflection so new types are covered automatically.
/// </summary>
public class DomainModelProtectionTests
{
    private static readonly Assembly[] DomainAssemblies =
    [
        typeof(Nodo.SharedKernel.AssemblyMarker).Assembly,
        typeof(Nodo.Quotes.Domain.AssemblyMarker).Assembly,
        typeof(Nodo.EventLifecycle.Domain.AssemblyMarker).Assembly,
        typeof(Nodo.HallSchedule.Domain.AssemblyMarker).Assembly
    ];

    private static IEnumerable<Type> ProtectedTypes() => DomainAssemblies
        .SelectMany(a => a.GetTypes())
        .Where(t => t is { IsClass: true, IsAbstract: false } && !t.IsNested && (IsEntity(t) || IsValueObject(t) || t == typeof(Money)));

    public static TheoryData<string> ProtectedTypeNames()
    {
        var data = new TheoryData<string>();
        foreach (var type in ProtectedTypes())
            data.Add(type.FullName!);
        return data;
    }

    private static Type Resolve(string fullName) => ProtectedTypes().Single(t => t.FullName == fullName);

    private static bool IsEntity(Type type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Entity<>))
                return true;
        }

        return false;
    }

    private static bool IsValueObject(Type type) => type.IsAssignableTo(typeof(ValueObject));

    [Fact]
    public void ProtectedTypes_IncludeEveryAggregateAndTheKeyValueObjects()
    {
        var names = ProtectedTypes().Select(t => t.Name).ToHashSet();

        string[] expected =
        [
            "HallEvent", "CustomerCredit", "Quote", "HallSchedule", "ScheduleBlock", "ExtraServiceItem",
            "GuaranteeDeposit", "TimeSlot", "Attendance", "PriceBreakdown", "Money"
        ];

        expected.Where(n => !names.Contains(n)).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ProtectedTypeNames))]
    public void ProtectedType_HasNoPublicConstructor(string typeName)
    {
        var type = Resolve(typeName);

        type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).ShouldBeEmpty(
            $"{type.Name} must be created through its factory method so it cannot start invalid.");
    }

    [Theory]
    [MemberData(nameof(ProtectedTypeNames))]
    public void ProtectedType_HasNoPublicSetterOrInitAccessor(string typeName)
    {
        var type = Resolve(typeName);

        var offenders = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name);

        offenders.ShouldBeEmpty($"{type.Name} must not expose public setters.");
    }

    [Theory]
    [MemberData(nameof(ProtectedTypeNames))]
    public void ProtectedType_DoesNotExposeMutableCollections(string typeName)
    {
        var type = Resolve(typeName);
        var mutable = new[]
        {
            typeof(List<>), typeof(IList<>), typeof(ICollection<>), typeof(HashSet<>), typeof(ISet<>),
            typeof(Dictionary<,>), typeof(IDictionary<,>)
        };

        var offenders = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType && mutable.Contains(p.PropertyType.GetGenericTypeDefinition()))
            .Select(p => p.Name);

        offenders.ShouldBeEmpty($"{type.Name} must expose collections as IReadOnlyCollection/IReadOnlyList.");
    }

    [Theory]
    [MemberData(nameof(ProtectedTypeNames))]
    public void ProtectedType_HasNoPublicFields(string typeName)
    {
        var type = Resolve(typeName);

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name);

        fields.ShouldBeEmpty();
    }

    [Fact]
    public void Aggregates_CanOnlyBeCreatedThroughStaticFactories()
    {
        // Each aggregate root exposes at least one public static factory returning itself.
        var roots = new[]
        {
            typeof(Nodo.Quotes.Domain.Quote),
            typeof(Nodo.EventLifecycle.Domain.CustomerCredit),
            typeof(Nodo.HallSchedule.Domain.HallSchedule)
        };

        foreach (var root in roots)
        {
            root.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Any(m => m.ReturnType == root)
                .ShouldBeTrue($"{root.Name} needs a static factory");
        }

        // HallEvent has none of its own: it is created by HallEventFactory (internal CreateRequested).
        typeof(Nodo.EventLifecycle.Domain.HallEvent).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .ShouldBeEmpty();
        typeof(Nodo.EventLifecycle.Domain.HallEventFactory).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(m => m.ReturnType == typeof(Nodo.EventLifecycle.Domain.HallEvent))
            .ShouldBeTrue();
    }
}
