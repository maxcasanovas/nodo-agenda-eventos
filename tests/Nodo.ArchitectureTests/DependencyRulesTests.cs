using System.Reflection;
using NetArchTest.Rules;

namespace Nodo.ArchitectureTests;

public class DependencyRulesTests
{
    public static TheoryData<string> ModuleNames => [.. ProjectGraph.Modules];

    private static readonly string[] ForbiddenInDomain =
    [
        "Microsoft.EntityFrameworkCore", "MediatR", "Microsoft.AspNetCore", "FluentValidation", "Npgsql"
    ];

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Domain_Project_ReferencesOnlySharedKernel(string module)
    {
        var domain = ProjectGraph.Get($"Nodo.{module}.Domain");

        domain.ProjectReferences.ShouldBe(["Nodo.SharedKernel"]);
        domain.PackageReferences.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Domain_Types_DoNotDependOnFrameworksOrOtherLayers(string module)
    {
        var assembly = ModuleAssembly(module, "Domain");

        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny([.. ForbiddenInDomain, $"Nodo.{module}.Application", $"Nodo.{module}.Infrastructure"])
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Application_Project_DoesNotReferenceInfrastructure(string module)
    {
        var application = ProjectGraph.Get($"Nodo.{module}.Application");

        application.ProjectReferences.ShouldNotContain(r => r.EndsWith(".Infrastructure"));
        application.ProjectReferences.ShouldNotContain("Nodo.External.Simulated");
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Application_Types_DoNotDependOnInfrastructure(string module)
    {
        var assembly = ModuleAssembly(module, "Application");

        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny([.. ProjectGraph.Modules.Select(m => $"Nodo.{m}.Infrastructure"), "Microsoft.EntityFrameworkCore"])
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_Projects_NeverReferenceDomainApplicationOrInfrastructureOfAnotherModule(string module)
    {
        var otherInternals = ProjectGraph.Modules
            .Where(m => m != module)
            .SelectMany(m => new[] { $"Nodo.{m}.Domain", $"Nodo.{m}.Application", $"Nodo.{m}.Infrastructure" })
            .ToHashSet();

        foreach (var layer in new[] { "Domain", "Application", "Infrastructure", "Contracts" })
        {
            var project = ProjectGraph.Get($"Nodo.{module}.{layer}");
            project.ProjectReferences.Intersect(otherInternals).ShouldBeEmpty($"{project.Name} references another module's internals");
        }
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Contracts_Project_ReferencesOnlyBuildingBlocksApplication(string module)
    {
        var contracts = ProjectGraph.Get($"Nodo.{module}.Contracts");

        contracts.ProjectReferences.ShouldBe(["Nodo.BuildingBlocks.Application"]);
        contracts.ProjectReferences.ShouldNotContain(r => r.EndsWith(".Domain"));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Contracts_Types_DoNotDependOnDomain(string module)
    {
        var assembly = ModuleAssembly(module, "Contracts");

        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny(ProjectGraph.Modules.Select(m => $"Nodo.{m}.Domain").ToArray())
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void SharedKernel_Project_HasNoDependencies()
    {
        var kernel = ProjectGraph.Get("Nodo.SharedKernel");

        kernel.ProjectReferences.ShouldBeEmpty();
        kernel.PackageReferences.ShouldBeEmpty();
    }

    // ---- External.Simulated: the downstream systems know our Published Language, never our internals ----

    [Fact]
    public void External_Project_ReferencesOnlyBuildingBlocksAndTheModulesPublishedContracts()
    {
        var allowed = new[]
        {
            "Nodo.BuildingBlocks.Application", "Nodo.SharedKernel",
            "Nodo.Quotes.Contracts", "Nodo.EventLifecycle.Contracts", "Nodo.HallSchedule.Contracts"
        };

        var external = ProjectGraph.Get("Nodo.External.Simulated");

        external.ProjectReferences.Where(r => !allowed.Contains(r)).ShouldBeEmpty();
        external.PackageReferences.ShouldBeEmpty();
    }

    [Fact]
    public void External_Types_DoNotDependOnAnyModuleDomainApplicationOrInfrastructure()
    {
        var forbidden = ProjectGraph.Modules
            .SelectMany(m => new[] { $"Nodo.{m}.Domain", $"Nodo.{m}.Application", $"Nodo.{m}.Infrastructure" })
            .Append("Microsoft.EntityFrameworkCore")
            .ToArray();

        var result = Types.InAssembly(typeof(Nodo.External.Simulated.AssemblyMarker).Assembly)
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void OnlyInfrastructureProjectsAndTheApi_ReferenceTheExternalSystems()
    {
        var offenders = ProjectGraph.Projects.Values
            .Where(p => p.ProjectReferences.Contains("Nodo.External.Simulated"))
            .Select(p => p.Name)
            .Where(name => !name.EndsWith(".Infrastructure") && name != "Nodo.Api");

        offenders.ShouldBeEmpty();
    }

[Fact]
    public void Api_IsTheCompositionRoot_NoProjectReferencesIt()
    {
        foreach (var project in ProjectGraph.Projects.Values)
        {
            project.ProjectReferences.ShouldNotContain("Nodo.Api");
        }
    }

    private static Assembly ModuleAssembly(string module, string layer) => layer switch
    {
        "Domain" => module switch
        {
            "Quotes" => typeof(Quotes.Domain.AssemblyMarker).Assembly,
            "EventLifecycle" => typeof(EventLifecycle.Domain.AssemblyMarker).Assembly,
            _ => typeof(HallSchedule.Domain.AssemblyMarker).Assembly
        },
        "Application" => module switch
        {
            "Quotes" => typeof(Quotes.Application.AssemblyMarker).Assembly,
            "EventLifecycle" => typeof(EventLifecycle.Application.AssemblyMarker).Assembly,
            _ => typeof(HallSchedule.Application.AssemblyMarker).Assembly
        },
        _ => module switch
        {
            "Quotes" => typeof(Quotes.Contracts.AssemblyMarker).Assembly,
            "EventLifecycle" => typeof(EventLifecycle.Contracts.AssemblyMarker).Assembly,
            _ => typeof(HallSchedule.Contracts.AssemblyMarker).Assembly
        }
    };
}
