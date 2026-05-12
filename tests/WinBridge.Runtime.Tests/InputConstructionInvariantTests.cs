// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Reflection;
using WinBridge.Runtime.Windows.Input;

namespace WinBridge.Runtime.Tests;

public sealed class InputConstructionInvariantTests
{
    [Fact]
    public void InputClickDispatchResultDoesNotExposePublicConstructorThatAcceptsCommittedSideEffects()
    {
        ConstructorInfo[] constructors = typeof(InputClickDispatchResult)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(
            constructors,
            constructor => constructor.IsPublic
                && constructor.GetParameters().Any(parameter =>
                    string.Equals(parameter.Name, "committedSideEffects", StringComparison.OrdinalIgnoreCase)
                    && parameter.ParameterType == typeof(bool)));
    }

    [Fact]
    public void InputExecutionOptionsDoubleClickDelayIsConstructorOnly()
    {
        PropertyInfo property = typeof(InputExecutionOptions).GetProperty(nameof(InputExecutionOptions.DoubleClickDelay))
            ?? throw new InvalidOperationException("DoubleClickDelay property was not found.");

        Assert.Null(property.SetMethod);
    }

    [Fact]
    public void ValidateDispatchEnvironmentReturnsDedicatedPreDispatchResultType()
    {
        MethodInfo method = typeof(Win32PointerBoundaryValidator).GetMethod(nameof(Win32PointerBoundaryValidator.ValidateDispatchEnvironment))
            ?? throw new InvalidOperationException("ValidateDispatchEnvironment method was not found.");
        Type returnType = method.ReturnType;

        Assert.Equal("InputDispatchEnvironmentResult", returnType.Name);
        Assert.NotNull(returnType.GetProperty("MouseButtonsSwapped"));
        Assert.Null(returnType.GetProperty("CommittedSideEffects"));
        Assert.Null(returnType.GetProperty("OutcomeKind"));
    }

    [Fact]
    public void InputDispatchEnvironmentResultDoesNotExposePublicConstructorWithSuccessAndFailureDetails()
    {
        AssertNoPublicConstructorWithBooleanAndFailureDetails(typeof(InputDispatchEnvironmentResult), "success");
    }

    [Fact]
    public void InputPointerSideEffectBoundaryResultDoesNotExposePublicConstructorWithSuccessAndFailureDetails()
    {
        AssertNoPublicConstructorWithBooleanAndFailureDetails(typeof(InputPointerSideEffectBoundaryResult), "success");
    }

    [Fact]
    public void InputTargetPreflightResultDoesNotExposePublicConstructorWithAllowedAndFailureDetails()
    {
        AssertNoPublicConstructorWithBooleanAndFailureDetails(typeof(InputTargetPreflightResult), "isAllowed");
    }

    private static void AssertNoPublicConstructorWithBooleanAndFailureDetails(Type type, string booleanParameterName)
    {
        ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(
            constructors,
            constructor => constructor.IsPublic
                && constructor.GetParameters().Any(parameter =>
                    string.Equals(parameter.Name, booleanParameterName, StringComparison.OrdinalIgnoreCase)
                    && parameter.ParameterType == typeof(bool))
                && constructor.GetParameters().Any(parameter =>
                    string.Equals(parameter.Name, "failureCode", StringComparison.OrdinalIgnoreCase)
                    && parameter.ParameterType == typeof(string))
                && constructor.GetParameters().Any(parameter =>
                    string.Equals(parameter.Name, "reason", StringComparison.OrdinalIgnoreCase)
                    && parameter.ParameterType == typeof(string)));
    }
}
