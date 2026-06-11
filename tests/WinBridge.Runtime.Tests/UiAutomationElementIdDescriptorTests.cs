// SPDX-FileCopyrightText: 2025-2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Windows.UIA;

namespace WinBridge.Runtime.Tests;

public sealed class UiAutomationElementIdDescriptorTests
{
    [Fact]
    public void TryParsePreservesRuntimeIdAndControlPath()
    {
        Assert.True(UiaElementIdDescriptor.TryParse("rid:1.2;path:0/3/4", out UiaElementIdDescriptor descriptor));

        Assert.Equal(UiaElementIdPathKind.Control, descriptor.PathKind);
        Assert.NotNull(descriptor.ExpectedRuntimeId);
        Assert.Equal([1, 2], descriptor.ExpectedRuntimeId!);
        Assert.Equal([3, 4], descriptor.Ordinals);
    }

    [Fact]
    public void TryParsePreservesRuntimeIdAndRawPath()
    {
        Assert.True(UiaElementIdDescriptor.TryParse("rid:9.8;raw:0/5", out UiaElementIdDescriptor descriptor));

        Assert.Equal(UiaElementIdPathKind.Raw, descriptor.PathKind);
        Assert.NotNull(descriptor.ExpectedRuntimeId);
        Assert.Equal([9, 8], descriptor.ExpectedRuntimeId!);
        Assert.Equal([5], descriptor.Ordinals);
    }

    [Fact]
    public void TryParseSupportsPathOnlyLegacyIdsWithoutIdentityCheck()
    {
        Assert.True(UiaElementIdDescriptor.TryParse("path:0/2", out UiaElementIdDescriptor descriptor));

        Assert.Equal(UiaElementIdPathKind.Control, descriptor.PathKind);
        Assert.Null(descriptor.ExpectedRuntimeId);
        Assert.Equal([2], descriptor.Ordinals);
        Assert.True(descriptor.MatchesExpectedRuntimeId([77]));
    }

    [Fact]
    public void TryParseRejectsRuntimeIdWithoutBoundedPath()
    {
        Assert.False(UiaElementIdDescriptor.TryParse("rid:1.2", out _));
    }

    [Fact]
    public void MatchesExpectedRuntimeIdRequiresExactIdentityWhenRuntimeIdIsPresent()
    {
        Assert.True(UiaElementIdDescriptor.TryParse("rid:1.2;path:0/3", out UiaElementIdDescriptor descriptor));

        Assert.True(descriptor.MatchesExpectedRuntimeId([1, 2]));
        Assert.False(descriptor.MatchesExpectedRuntimeId([1, 3]));
        Assert.False(descriptor.MatchesExpectedRuntimeId(null));
    }
}
