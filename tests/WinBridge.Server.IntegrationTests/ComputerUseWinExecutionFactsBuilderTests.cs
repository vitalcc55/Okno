// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Reflection;
using WinBridge.Server.ComputerUse;

namespace WinBridge.Server.IntegrationTests;

public sealed class ComputerUseWinExecutionFactsBuilderTests
{
    [Fact]
    public void BuildMapsSemanticExecutorToSemanticDispatchClass()
    {
        ComputerUseWinExecutionFacts facts = ComputerUseWinExecutionFactsBuilder.Build(
            new ComputerUseWinExecutionFactsInputs(
                Executor: "uia_toggle",
                ConfirmationRequired: false,
                Confirmed: true,
                FallbackUsed: false,
                TargetProof: ComputerUseWinTargetProofValues.UiaRevalidated,
                StateTokenPresent: true,
                CaptureReferencePresent: false,
                WindowContinuity: ComputerUseWinWindowContinuityValues.Accepted,
                ForegroundIntegrity: ComputerUseWinForegroundIntegrityValues.Accepted,
                ObserveAfterRequested: false,
                SuccessorStateAvailable: false));

        Assert.Equal(ComputerUseWinDispatchClassValues.Semantic, facts.DispatchClass);
        Assert.Equal("uia_toggle", facts.Executor);
        Assert.False(facts.PhysicalPointerUsed);
        Assert.False(facts.PhysicalKeyboardUsed);
        Assert.False(facts.SystemCursorMoved);
    }

    [Fact]
    public void BuildMapsExpectedPhysicalExecutorWithoutFallbackToExpectedPhysical()
    {
        ComputerUseWinExecutionFacts facts = ComputerUseWinExecutionFactsBuilder.Build(
            new ComputerUseWinExecutionFactsInputs(
                Executor: "win32_sendinput_keypress",
                ConfirmationRequired: true,
                Confirmed: true,
                FallbackUsed: false,
                TargetProof: ComputerUseWinTargetProofValues.None,
                StateTokenPresent: true,
                CaptureReferencePresent: false,
                WindowContinuity: ComputerUseWinWindowContinuityValues.Accepted,
                ForegroundIntegrity: ComputerUseWinForegroundIntegrityValues.Accepted,
                ObserveAfterRequested: true,
                SuccessorStateAvailable: true));

        Assert.Equal(ComputerUseWinDispatchClassValues.ExpectedPhysical, facts.DispatchClass);
        Assert.False(facts.PhysicalPointerUsed);
        Assert.True(facts.PhysicalKeyboardUsed);
        Assert.False(facts.SystemCursorMoved);
        Assert.True(facts.ConfirmationRequired);
        Assert.True(facts.ConfirmationSatisfied);
    }

    [Fact]
    public void BuildMapsFallbackPhysicalExecutorToFallbackPhysical()
    {
        ComputerUseWinExecutionFacts facts = ComputerUseWinExecutionFactsBuilder.Build(
            new ComputerUseWinExecutionFactsInputs(
                Executor: "capture_pixels_text_input",
                ConfirmationRequired: true,
                Confirmed: true,
                FallbackUsed: true,
                TargetProof: ComputerUseWinTargetProofValues.CapturePoint,
                StateTokenPresent: true,
                CaptureReferencePresent: true,
                WindowContinuity: ComputerUseWinWindowContinuityValues.Accepted,
                ForegroundIntegrity: ComputerUseWinForegroundIntegrityValues.Accepted,
                ObserveAfterRequested: true,
                SuccessorStateAvailable: false));

        Assert.Equal(ComputerUseWinDispatchClassValues.FallbackPhysical, facts.DispatchClass);
        Assert.True(facts.PhysicalPointerUsed);
        Assert.True(facts.PhysicalKeyboardUsed);
        Assert.True(facts.SystemCursorMoved);
        Assert.True(facts.FallbackUsed);
    }

    [Fact]
    public void BuildMapsExpandCollapsePatternExecutorToSemanticDispatchClass()
    {
        ComputerUseWinExecutionFacts facts = ComputerUseWinExecutionFactsBuilder.Build(
            new ComputerUseWinExecutionFactsInputs(
                Executor: "uia_expand_collapse_pattern",
                ConfirmationRequired: false,
                Confirmed: true,
                FallbackUsed: false,
                TargetProof: ComputerUseWinTargetProofValues.UiaRevalidated,
                StateTokenPresent: true,
                CaptureReferencePresent: false,
                WindowContinuity: ComputerUseWinWindowContinuityValues.Accepted,
                ForegroundIntegrity: ComputerUseWinForegroundIntegrityValues.Accepted,
                ObserveAfterRequested: false,
                SuccessorStateAvailable: false));

        Assert.Equal(ComputerUseWinDispatchClassValues.Semantic, facts.DispatchClass);
        Assert.Equal("uia_expand_collapse_pattern", facts.Executor);
        Assert.False(facts.PhysicalPointerUsed);
        Assert.False(facts.PhysicalKeyboardUsed);
        Assert.False(facts.SystemCursorMoved);
    }

    [Fact]
    public void SharedExecutionExecutorDescriptorsCoverAllDeclaredExecutorConstants()
    {
        HashSet<string> declaredExecutors = typeof(ComputerUseWinExecutionExecutorValues)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> registeredExecutors = ComputerUseWinExecutionExecutorValues.SupportedDescriptors
            .Select(descriptor => descriptor.Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(declaredExecutors, registeredExecutors);
        Assert.Equal(declaredExecutors, ComputerUseWinExecutionExecutorValues.All.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public void BuildClassifiesAllRegisteredExecutionExecutors()
    {
        foreach (string executor in ComputerUseWinExecutionExecutorValues.All)
        {
            ComputerUseWinExecutionFacts facts = ComputerUseWinExecutionFactsBuilder.Build(
                new ComputerUseWinExecutionFactsInputs(
                    Executor: executor,
                    ConfirmationRequired: false,
                    Confirmed: true,
                    FallbackUsed: false,
                    TargetProof: ComputerUseWinTargetProofValues.None,
                    StateTokenPresent: true,
                    CaptureReferencePresent: false,
                    WindowContinuity: ComputerUseWinWindowContinuityValues.Accepted,
                    ForegroundIntegrity: ComputerUseWinForegroundIntegrityValues.Accepted,
                    ObserveAfterRequested: false,
                    SuccessorStateAvailable: false));

            Assert.Equal(executor, facts.Executor);
        }
    }

    [Fact]
    public void BuildFailsClosedForUnknownExecutor()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ComputerUseWinExecutionFactsBuilder.Build(
                new ComputerUseWinExecutionFactsInputs(
                    Executor: "mystery_executor",
                    ConfirmationRequired: false,
                    Confirmed: false,
                    FallbackUsed: false,
                    TargetProof: ComputerUseWinTargetProofValues.None,
                    StateTokenPresent: false,
                    CaptureReferencePresent: false,
                    WindowContinuity: ComputerUseWinWindowContinuityValues.Unknown,
                    ForegroundIntegrity: ComputerUseWinForegroundIntegrityValues.Unknown,
                    ObserveAfterRequested: false,
                    SuccessorStateAvailable: false)));

        Assert.Contains("mystery_executor", exception.Message, StringComparison.Ordinal);
    }
}
