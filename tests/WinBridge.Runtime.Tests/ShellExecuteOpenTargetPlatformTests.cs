using System.Runtime.InteropServices;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Windows.Launch;

namespace WinBridge.Runtime.Tests;

public sealed class ShellExecuteOpenTargetPlatformTests
{
    [Fact]
    public void OpenUsesExpectedFlagsAndDefaultVerbAndNullParametersAndDirectory()
    {
        FakeShellExecuteNativeAdapter adapter = new(
            invocation => new ShellExecuteNativeResult(
                Succeeded: true,
                HInstApp: (nint)33,
                ProcessHandle: nint.Zero,
                LastError: 0));
        ShellExecuteOpenTargetPlatform platform = new(adapter, new InlineStaExecutor());

        OpenTargetPlatformResult result = platform.Open(
            new OpenTargetPlatformRequest(
                TargetKind: OpenTargetKindValues.Document,
                Target: @"C:\Docs\report.pdf"));

        Assert.True(result.IsAccepted);
        Assert.NotNull(adapter.LastInvocation);
        Assert.Equal(@"C:\Docs\report.pdf", adapter.LastInvocation!.Value.Target);
        Assert.Equal(
            ShellExecuteOpenTargetPlatform.ExpectedMask,
            adapter.LastInvocation.Value.Mask);
        Assert.Null(adapter.LastInvocation.Value.Verb);
        Assert.Null(adapter.LastInvocation.Value.Parameters);
        Assert.Null(adapter.LastInvocation.Value.Directory);
    }

    [Fact]
    public void OpenReturnsAcceptedWithoutHandlerProcessIdWhenShellDoesNotReturnProcessHandle()
    {
        FakeShellExecuteNativeAdapter adapter = new(
            invocation => new ShellExecuteNativeResult(
                Succeeded: true,
                HInstApp: (nint)33,
                ProcessHandle: nint.Zero,
                LastError: 0));
        ShellExecuteOpenTargetPlatform platform = new(adapter, new InlineStaExecutor());

        OpenTargetPlatformResult result = platform.Open(
            new OpenTargetPlatformRequest(
                TargetKind: OpenTargetKindValues.Url,
                Target: "https://example.test/docs"));

        Assert.True(result.IsAccepted);
        Assert.Null(result.HandlerProcessId);
        Assert.Null(result.FailureCode);
        Assert.Equal(0, adapter.CloseHandleCallCount);
    }

    [Fact]
    public void OpenReturnsAcceptedWithHandlerProcessIdWhenShellReturnsProcessHandle()
    {
        FakeShellExecuteNativeAdapter adapter = new(
            invocation => new ShellExecuteNativeResult(
                Succeeded: true,
                HInstApp: (nint)33,
                ProcessHandle: (nint)1234,
                LastError: 0))
        {
            ProcessIdToReturn = 4242,
        };
        ShellExecuteOpenTargetPlatform platform = new(adapter, new InlineStaExecutor());

        OpenTargetPlatformResult result = platform.Open(
            new OpenTargetPlatformRequest(
                TargetKind: OpenTargetKindValues.Folder,
                Target: @"C:\Workspace"));

        Assert.True(result.IsAccepted);
        Assert.Equal(4242, result.HandlerProcessId);
        Assert.Equal(1, adapter.CloseHandleCallCount);
        Assert.Equal((nint)1234, adapter.LastClosedHandle);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void OpenMapsPathErrorsToTargetNotFound(int errorCode)
    {
        OpenTargetPlatformResult result = ExecuteFailure(errorCode);

        Assert.False(result.IsAccepted);
        Assert.Equal(OpenTargetFailureCodeValues.TargetNotFound, result.FailureCode);
    }

    [Theory]
    [InlineData(27)]
    [InlineData(31)]
    public void OpenMapsAssociationErrorsToNoAssociation(int errorCode)
    {
        OpenTargetPlatformResult result = ExecuteFailure(errorCode);

        Assert.False(result.IsAccepted);
        Assert.Equal(OpenTargetFailureCodeValues.NoAssociation, result.FailureCode);
    }

    [Fact]
    public void OpenMapsAccessDeniedToTargetAccessDenied()
    {
        OpenTargetPlatformResult result = ExecuteFailure(5);

        Assert.False(result.IsAccepted);
        Assert.Equal(OpenTargetFailureCodeValues.TargetAccessDenied, result.FailureCode);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(26)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(32)]
    public void OpenMapsGenericShellErrorsToShellRejectedTarget(int errorCode)
    {
        OpenTargetPlatformResult result = ExecuteFailure(errorCode);

        Assert.False(result.IsAccepted);
        Assert.Equal(OpenTargetFailureCodeValues.ShellRejectedTarget, result.FailureCode);
    }

    private static OpenTargetPlatformResult ExecuteFailure(int errorCode)
    {
        FakeShellExecuteNativeAdapter adapter = new(
            invocation => new ShellExecuteNativeResult(
                Succeeded: false,
                HInstApp: (nint)errorCode,
                ProcessHandle: nint.Zero,
                LastError: errorCode));
        ShellExecuteOpenTargetPlatform platform = new(adapter, new InlineStaExecutor());
        return platform.Open(
            new OpenTargetPlatformRequest(
                TargetKind: OpenTargetKindValues.Document,
                Target: @"C:\Docs\report.pdf"));
    }

    private sealed class InlineStaExecutor : IShellExecuteStaExecutor
    {
        public T Execute<T>(Func<T> callback) => callback();
    }

    private sealed class FakeShellExecuteNativeAdapter(Func<ShellExecuteInvocation, ShellExecuteNativeResult> executeHandler) : IShellExecuteNativeAdapter
    {
        public ShellExecuteInvocation? LastInvocation { get; private set; }

        public int CloseHandleCallCount { get; private set; }

        public nint LastClosedHandle { get; private set; }

        public uint? ProcessIdToReturn { get; init; }

        public ShellExecuteNativeResult Execute(ShellExecuteInvocation invocation)
        {
            LastInvocation = invocation;
            return executeHandler(invocation);
        }

        public uint? TryGetProcessId(nint processHandle) => ProcessIdToReturn;

        public void CloseHandle(nint processHandle)
        {
            CloseHandleCallCount++;
            LastClosedHandle = processHandle;
        }
    }
}
