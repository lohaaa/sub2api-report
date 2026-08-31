using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sub2ApiReport.Updater;
using Sub2ApiReport.Updater.Maintenance;
using Sub2ApiReport.Updater.Security;

namespace Sub2ApiReport.UpdaterTests.Maintenance;

public sealed class AppMaintenanceClientTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    [Fact]
    public async Task EnterMaintenancePreservesProblemDetail()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Detail = "存在正在执行或排队的报告任务，暂时不能升级。",
            }),
        });

        var exception = await Assert.ThrowsAsync<UpdateOperationException>(() =>
            client.EnterMaintenanceAsync(Guid.NewGuid().ToString("N"), CancellationToken.None));

        Assert.Equal(StatusCodes.Status502BadGateway, exception.StatusCode);
        Assert.Equal("存在正在执行或排队的报告任务，暂时不能升级。", exception.Message);
    }

    [Fact]
    public async Task EnterMaintenanceUsesSafeFallbackForInvalidProblemDetails()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/problem+json"),
        });

        var exception = await Assert.ThrowsAsync<UpdateOperationException>(() =>
            client.EnterMaintenanceAsync(Guid.NewGuid().ToString("N"), CancellationToken.None));

        Assert.Equal(StatusCodes.Status502BadGateway, exception.StatusCode);
        Assert.Equal("App 维护请求失败。", exception.Message);
    }

    public void Dispose() => _temp.Dispose();

    private AppMaintenanceClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var tokenPath = Path.Combine(_temp.FullPath, "updater-token");
        File.WriteAllText(tokenPath, new string('a', 64));
        var httpClient = new HttpClient(new StubHttpHandler(responder))
        {
            BaseAddress = new Uri("http://app:8080"),
        };
        return new AppMaintenanceClient(httpClient, new UpdaterTokenProvider(tokenPath));
    }
}
