// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
                Target: @"C:\Docs\report.pdf"),
            CancellationToken.None);

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
                Target: "https://example.test/docs"),
            CancellationToken.None);

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
                Target: @"C:\Workspace"),
            CancellationToken.None);

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
    public void OpenMapsExtendedNoAssociationErrorToNoAssociation()
    {
        OpenTargetPlatformResult result = ExecuteFailure(shellCode: 0, lastError: 1155);

        Assert.False(result.IsAccepted);
        Assert.Equal(OpenTargetFailureCodeValues.NoAssociation, result.FailureCode);
    }

    [Theory]
    [InlineData(0, 2, OpenTargetFailureCodeValues.TargetNotFound)]
    [InlineData(0, 3, OpenTargetFailureCodeValues.TargetNotFound)]
    [InlineData(0, 5, OpenTargetFailureCodeValues.TargetAccessDenied)]
    public void OpenMapsExtendedWin32FailureCodesWhenShellCodeIsUnavailable(int shellCode, int lastError, string expectedFailureCode)
    {
        OpenTargetPlatformResult result = ExecuteFailure(shellCode, lastError);

        Assert.False(result.IsAccepted);
        Assert.Equal(expectedFailureCode, result.FailureCode);
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

    [Fact]
    public async Task OpenCancelsBeforeDispatchWithoutExecutingShellCallback()
    {
        using ManualResetEventSlim beforeDispatchEntered = new(false);
        using ManualResetEventSlim releaseBeforeDispatch = new(false);
        FakeShellExecuteNativeAdapter adapter = new(
            invocation => new ShellExecuteNativeResult(
                Succeeded: true,
                HInstApp: (nint)33,
                ProcessHandle: nint.Zero,
                LastError: 0));
        DedicatedStaShellExecuteExecutor executor = new(() =>
        {
            beforeDispatchEntered.Set();
            releaseBeforeDispatch.Wait();
        });
        ShellExecuteOpenTargetPlatform platform = new(adapter, executor);
        using CancellationTokenSource cts = new();

        Task<OpenTargetPlatformResult> openTask = Task.Run(() => platform.Open(
            new OpenTargetPlatformRequest(
                TargetKind: OpenTargetKindValues.Document,
                Target: @"C:\Docs\report.pdf"),
            cts.Token));

        Assert.True(beforeDispatchEntered.Wait(TimeSpan.FromSeconds(2)));
        cts.Cancel();
        releaseBeforeDispatch.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => openTask);
        Assert.Equal(0, adapter.ExecuteCallCount);
    }

    [Fact]
    public async Task OpenCompletesFactuallyAfterDispatchStartedDespiteCallerCancellation()
    {
        using ManualResetEventSlim callbackEntered = new(false);
        using ManualResetEventSlim releaseCallback = new(false);
        FakeShellExecuteNativeAdapter adapter = new(
            invocation =>
            {
                callbackEntered.Set();
                releaseCallback.Wait();
                return new ShellExecuteNativeResult(
                    Succeeded: true,
                    HInstApp: (nint)33,
                    ProcessHandle: nint.Zero,
                    LastError: 0);
            });
        ShellExecuteOpenTargetPlatform platform = new(adapter, new DedicatedStaShellExecuteExecutor());
        using CancellationTokenSource cts = new();

        Task<OpenTargetPlatformResult> openTask = Task.Run(() => platform.Open(
            new OpenTargetPlatformRequest(
                TargetKind: OpenTargetKindValues.Document,
                Target: @"C:\Docs\report.pdf"),
            cts.Token));

        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(2)));
        cts.Cancel();
        releaseCallback.Set();

        OpenTargetPlatformResult result = await openTask;
        Assert.True(result.IsAccepted);
    }

    private static OpenTargetPlatformResult ExecuteFailure(int errorCode)
        => ExecuteFailure(errorCode, errorCode);

    private static OpenTargetPlatformResult ExecuteFailure(int shellCode, int lastError)
    {
        FakeShellExecuteNativeAdapter adapter = new(
            invocation => new ShellExecuteNativeResult(
                Succeeded: false,
                HInstApp: (nint)shellCode,
                ProcessHandle: nint.Zero,
                LastError: lastError));
        ShellExecuteOpenTargetPlatform platform = new(adapter, new InlineStaExecutor());
        return platform.Open(
            new OpenTargetPlatformRequest(
                TargetKind: OpenTargetKindValues.Document,
                Target: @"C:\Docs\report.pdf"),
            CancellationToken.None);
    }

    private sealed class InlineStaExecutor : IShellExecuteStaExecutor
    {
        public T Execute<T>(Func<T> callback, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return callback();
        }
    }

    private sealed class FakeShellExecuteNativeAdapter(Func<ShellExecuteInvocation, ShellExecuteNativeResult> executeHandler) : IShellExecuteNativeAdapter
    {
        public ShellExecuteInvocation? LastInvocation { get; private set; }

        public int CloseHandleCallCount { get; private set; }

        public int ExecuteCallCount { get; private set; }

        public nint LastClosedHandle { get; private set; }

        public uint? ProcessIdToReturn { get; init; }

        public ShellExecuteNativeResult Execute(ShellExecuteInvocation invocation)
        {
            ExecuteCallCount++;
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
