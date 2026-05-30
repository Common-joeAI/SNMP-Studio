using Moq;
using SNMP.Core.Enums;
using SNMP.Core.Interfaces;
using SNMP.Engine.Services;
using Xunit;

namespace SNMP.Tests;

/// <summary>
/// NegotiationService unit tests use a mock ILogService so no real network calls are made.
/// The negotiation logic against unreachable hosts verifies the failure path and report structure.
/// </summary>
public class NegotiationServiceTests
{
    private static NegotiationService BuildService()
    {
        var log = new Mock<ILogService>();
        return new NegotiationService(log.Object);
    }

    [Fact]
    public async Task NegotiateAsync_InvalidHost_ReturnsDnsFailure()
    {
        var svc    = BuildService();
        var report = await svc.NegotiateAsync("this.host.does.not.exist.invalid", 161);

        Assert.False(report.Success);
        // DNS failure hits before any attempt, so category can be NoResponse or Unknown
        Assert.True(report.Result is NegotiationResult.NoResponse or NegotiationResult.Unknown);
        Assert.NotEmpty(report.FriendlyMessage);
    }

    [Fact]
    public async Task NegotiateAsync_ClosedPort_ReturnsNoResponse()
    {
        // 127.0.0.1 port 1 is always closed on dev machines
        var svc    = BuildService();
        var report = await svc.NegotiateAsync("127.0.0.1", 1);

        Assert.False(report.Success);
        // Should be Timeout or NoResponse after exhausting all probes
        Assert.True(report.Result is NegotiationResult.Timeout
                                  or NegotiationResult.NoResponse
                                  or NegotiationResult.Unknown);
    }

    [Fact]
    public async Task NegotiateAsync_ReturnsAttemptLog()
    {
        var svc    = BuildService();
        var report = await svc.NegotiateAsync("127.0.0.1", 1,
            communitiesToTry: new[] { "public" });

        // AttemptLog must contain at least one entry regardless of outcome
        Assert.NotEmpty(report.AttemptLog);
    }

    [Fact]
    public async Task NegotiateAsync_CancellationRespected()
    {
        var svc = BuildService();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancelled before start

        // Should complete quickly without throwing
        var report = await svc.NegotiateAsync("127.0.0.1", 161, ct: cts.Token);
        // May succeed trivially or return a failure — the key check is no exception thrown
        Assert.NotNull(report);
    }
}
