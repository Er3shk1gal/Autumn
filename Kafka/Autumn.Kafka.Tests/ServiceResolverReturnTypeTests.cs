using Autumn.Kafka.Exceptions;
using Autumn.Kafka.Utils;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Autumn.Kafka.Tests;

// Covers the reflection return-type unwrap in ServiceResolver.InvokeMethodAsync — every
// supported handler shape. A bug here silently corrupts every RPC/response payload, so each
// form is pinned explicitly. VoidResultMarker is the internal sentinel meaning "no response value".
public class ReturnTypeService
{
    public int SyncInt() => 42;
    public void SyncVoid() { }
    public Task AsyncTask() => Task.CompletedTask;
    public Task<int> AsyncTaskInt() => Task.FromResult(7);
    public Task<string?> AsyncTaskNull() => Task.FromResult<string?>(null);
    public ValueTask AsyncValueTask() => ValueTask.CompletedTask;
    public ValueTask<int> AsyncValueTaskInt() => new(99);
    public string EchoParam(string p) => p;
    public void ThrowsBusiness() => throw new InvalidOperationException("boom");
    public void NoParams() { }
}

public class ServiceResolverReturnTypeTests
{
    private static IServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ReturnTypeService>();
        return services.BuildServiceProvider();
    }

    private static Task<object?> Invoke(string method, IEnumerable<object>? parameters = null)
        => ServiceResolver.InvokeMethodAsync(
            Provider(),
            typeof(ReturnTypeService).GetMethod(method)!,
            typeof(ReturnTypeService),
            parameters);

    [Fact]
    public async Task SyncNonVoid_ReturnsValue()
        => Assert.Equal(42, await Invoke(nameof(ReturnTypeService.SyncInt)));

    [Fact]
    public async Task SyncVoid_ReturnsVoidMarker()
        => Assert.Same(ServiceResolver.VoidResultMarker, await Invoke(nameof(ReturnTypeService.SyncVoid)));

    [Fact]
    public async Task NonGenericTask_ReturnsVoidMarker()
        => Assert.Same(ServiceResolver.VoidResultMarker, await Invoke(nameof(ReturnTypeService.AsyncTask)));

    [Fact]
    public async Task GenericTask_ReturnsValue()
        => Assert.Equal(7, await Invoke(nameof(ReturnTypeService.AsyncTaskInt)));

    [Fact]
    public async Task GenericTask_NullResult_ReturnsNull()
        => Assert.Null(await Invoke(nameof(ReturnTypeService.AsyncTaskNull)));

    [Fact]
    public async Task NonGenericValueTask_ReturnsVoidMarker()
        => Assert.Same(ServiceResolver.VoidResultMarker, await Invoke(nameof(ReturnTypeService.AsyncValueTask)));

    [Fact]
    public async Task GenericValueTask_ReturnsValue()
        => Assert.Equal(99, await Invoke(nameof(ReturnTypeService.AsyncValueTaskInt)));

    [Fact]
    public async Task Parameters_ArePassedThrough()
        => Assert.Equal("hi", await Invoke(nameof(ReturnTypeService.EchoParam), new object[] { "hi" }));

    [Fact]
    public async Task BusinessException_IsUnwrapped_NotTargetInvocation()
    {
        // TargetInvocationException must be unwrapped so ErrorPolicy/retry classification sees the real type.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Invoke(nameof(ReturnTypeService.ThrowsBusiness)));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task ParametersProvided_ButMethodTakesNone_Throws()
    {
        await Assert.ThrowsAsync<KafkaServiceResolutionException>(
            () => Invoke(nameof(ReturnTypeService.NoParams), new object[] { "unexpected" }));
    }

    [Fact]
    public async Task ParametersRequired_ButNoneProvided_Throws()
    {
        await Assert.ThrowsAsync<KafkaServiceResolutionException>(
            () => Invoke(nameof(ReturnTypeService.EchoParam), null));
    }
}
