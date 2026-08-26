using System.Reflection;
using Sub2ApiReport.UpdateContracts;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseExceptionHandler();

app.MapHealthChecks("/health/live");
app.MapGet("/internal/v1/status", (CancellationToken cancellationToken) =>
{
    cancellationToken.ThrowIfCancellationRequested();
    var assembly = typeof(Program).Assembly;
    var version = assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0]
        ?? assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    return TypedResults.Ok(new UpdaterStatusResponse(
        version,
        InstallationEnabled: false,
        State: "scaffold"));
});

app.Run();

public partial class Program;
