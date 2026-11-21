using GodrejWMS.Application.Common.Behaviours;
using MediatR;
using Xunit;

namespace GodrejWMS.Application.Tests;

public sealed class SerialisedExecutionTests
{
    private sealed record Ping : IRequest<int>;

    [Fact]
    public async Task OverlappingRequests_RunOneAtATime_AndNestedRequestsDoNotDeadlock()
    {
        var gate = new DbOperationGate();
        var behaviour = new SerialisedExecutionBehaviour<Ping, int>(gate);
        var running = 0;
        var maxRunning = 0;

        async Task<int> Work(bool nested)
        {
            var now = nested ? Interlocked.Increment(ref running) : running;
            maxRunning = Math.Max(maxRunning, now);
            await Task.Delay(30);
            if (nested)
            {
                await behaviour.Handle(new Ping(), _ => Work(false), default);
            }
            if (nested) Interlocked.Decrement(ref running);
            return 1;
        }

        var tasks = Enumerable.Range(0, 6).Select(i => behaviour.Handle(new Ping(), _ => Work(i % 2 == 0), default));
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, maxRunning);
    }
}
