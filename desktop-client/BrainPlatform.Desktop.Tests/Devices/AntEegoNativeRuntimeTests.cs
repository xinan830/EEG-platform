using System.Linq.Expressions;

namespace BrainPlatform.Desktop.Tests.Devices;

public sealed class AntEegoNativeRuntimeTests
{
    [Fact]
    public void RuntimeLoadsSdkOnceAcrossModeLeasesAndExitsOnlyAtShutdown()
    {
        var bindings = new NativeBindings();
        var loads = 0;
        var runtime = new AntEegoNativeRuntimeState(_ =>
        {
            loads++;
            return new AntEegoNativeApi(0, bindings.Bind);
        });

        runtime.Acquire("eego-SDK.dll", true).Dispose();
        runtime.Acquire("eego-SDK.dll", false).Dispose();
        var activeLease = runtime.Acquire("eego-SDK.dll", true);

        Assert.Equal(1, loads);
        Assert.Equal(0, bindings.ExitCalls);
        Assert.Throws<InvalidOperationException>(() => runtime.Shutdown());
        Assert.Equal(0, bindings.ExitCalls);

        activeLease.Dispose();
        runtime.Shutdown();

        Assert.Equal(1, bindings.ExitCalls);
        Assert.Throws<ObjectDisposedException>(() => runtime.Acquire("eego-SDK.dll", true));
    }

    private sealed class NativeBindings
    {
        public int ExitCalls { get; private set; }

        public Delegate? Bind(string name, Type delegateType)
        {
            var signature = delegateType.GetMethod("Invoke")!;
            var parameters = signature.GetParameters()
                .Select(parameter => Expression.Parameter(parameter.ParameterType)).ToArray();
            var arguments = Expression.NewArrayInit(typeof(object), parameters
                .Select(parameter => Expression.Convert(parameter, typeof(object))));
            var dispatch = Expression.Call(Expression.Constant(this), nameof(Dispatch), null,
                Expression.Constant(name), arguments);
            Expression body = signature.ReturnType == typeof(void)
                ? Expression.Block(dispatch, Expression.Empty())
                : Expression.Convert(dispatch, signature.ReturnType);
            return Expression.Lambda(delegateType, body, parameters).Compile();
        }

        public int Dispatch(string name, object[] arguments)
        {
            if (name == "eemagine_sdk_exit") ExitCalls++;
            return 0;
        }
    }
}
