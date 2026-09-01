using System.Reflection;
using ApplicationAssembly = Sub2ApiReport.Application.AssemblyReference;
using CliAssembly = Sub2ApiReport.Cli.AssemblyReference;
using DomainAssembly = Sub2ApiReport.Domain.AssemblyReference;
using InfrastructureAssembly = Sub2ApiReport.Infrastructure.AssemblyReference;

namespace Sub2ApiReport.ArchitectureTests;

public sealed class DependencyDirectionTests
{
    [Fact]
    public void DomainDoesNotReferenceOtherSolutionLayers() =>
        AssertDoesNotReference(
            typeof(DomainAssembly).Assembly,
            "Sub2ApiReport.Application",
            "Sub2ApiReport.Infrastructure",
            "Sub2ApiReport.Api");

    [Fact]
    public void ApplicationDoesNotReferenceOuterLayers() =>
        AssertDoesNotReference(
            typeof(ApplicationAssembly).Assembly,
            "Sub2ApiReport.Infrastructure",
            "Sub2ApiReport.Api");

    [Fact]
    public void InfrastructureDoesNotReferenceHosts() =>
        AssertDoesNotReference(
            typeof(InfrastructureAssembly).Assembly,
            "Sub2ApiReport.Api");

    [Fact]
    public void CliDoesNotReferenceWebHost() =>
        AssertDoesNotReference(
            typeof(CliAssembly).Assembly,
            "Sub2ApiReport.Api");

    private static void AssertDoesNotReference(Assembly assembly, params string[] forbiddenAssemblies)
    {
        var references = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var forbiddenAssembly in forbiddenAssemblies)
        {
            Assert.DoesNotContain(forbiddenAssembly, references);
        }
    }
}
