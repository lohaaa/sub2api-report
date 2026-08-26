using System.Reflection;
using ApiAssembly = Sub2ApiReport.Api.AssemblyReference;
using ApplicationAssembly = Sub2ApiReport.Application.AssemblyReference;
using CliAssembly = Sub2ApiReport.Cli.AssemblyReference;
using DomainAssembly = Sub2ApiReport.Domain.AssemblyReference;
using InfrastructureAssembly = Sub2ApiReport.Infrastructure.AssemblyReference;
using UpdateContractsAssembly = Sub2ApiReport.UpdateContracts.AssemblyReference;
using UpdaterAssembly = Sub2ApiReport.Updater.AssemblyReference;

namespace Sub2ApiReport.ArchitectureTests;

public sealed class DependencyDirectionTests
{
    [Fact]
    public void DomainDoesNotReferenceOtherSolutionLayers() =>
        AssertDoesNotReference(
            typeof(DomainAssembly).Assembly,
            "Sub2ApiReport.Application",
            "Sub2ApiReport.Infrastructure",
            "Sub2ApiReport.Api",
            "Sub2ApiReport.UpdateContracts",
            "Sub2ApiReport.Updater");

    [Fact]
    public void ApplicationDoesNotReferenceOuterLayers() =>
        AssertDoesNotReference(
            typeof(ApplicationAssembly).Assembly,
            "Sub2ApiReport.Infrastructure",
            "Sub2ApiReport.Api",
            "Sub2ApiReport.UpdateContracts",
            "Sub2ApiReport.Updater");

    [Fact]
    public void InfrastructureDoesNotReferenceHosts() =>
        AssertDoesNotReference(
            typeof(InfrastructureAssembly).Assembly,
            "Sub2ApiReport.Api",
            "Sub2ApiReport.UpdateContracts",
            "Sub2ApiReport.Updater");

    [Fact]
    public void ApiDoesNotReferenceUpdaterHost() =>
        AssertDoesNotReference(typeof(ApiAssembly).Assembly, "Sub2ApiReport.Updater");

    [Fact]
    public void CliDoesNotReferenceWebOrUpdaterHosts() =>
        AssertDoesNotReference(
            typeof(CliAssembly).Assembly,
            "Sub2ApiReport.Api",
            "Sub2ApiReport.UpdateContracts",
            "Sub2ApiReport.Updater");

    [Fact]
    public void UpdateContractsAreIsolatedFromHostsAndBusinessLayers() =>
        AssertDoesNotReference(
            typeof(UpdateContractsAssembly).Assembly,
            "Sub2ApiReport.Domain",
            "Sub2ApiReport.Application",
            "Sub2ApiReport.Infrastructure",
            "Sub2ApiReport.Api",
            "Sub2ApiReport.Updater");

    [Fact]
    public void UpdaterDoesNotReferenceBusinessLayers() =>
        AssertDoesNotReference(
            typeof(UpdaterAssembly).Assembly,
            "Sub2ApiReport.Domain",
            "Sub2ApiReport.Application",
            "Sub2ApiReport.Infrastructure",
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
