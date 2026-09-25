// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Testing;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Application;

public sealed class ApplicationInfoProviderTests
{
    [Fact]
    public void TheDeclaredKindIsUsedAndNothingIsDetected()
    {
        var detections = 0;

        var provider = new ApplicationInfoProvider(
            new TestApplicationInfo { ProcessKind = ProcessKind.Compiler },
            () =>
            {
                detections++;

                return ProcessKind.Rider;
            } );

        Assert.Equal( ProcessKind.Compiler, provider.ProcessKind );
        Assert.Equal( 0, detections );
    }

    [Fact]
    public void TheKindIsDetectedWhenTheApplicationDeclaresNone()
    {
        var provider = new ApplicationInfoProvider( new TestApplicationInfo { ProcessKind = null }, () => ProcessKind.Rider );

        Assert.Equal( ProcessKind.Rider, provider.ProcessKind );
    }

    [Fact]
    public void TheKindIsDetectedOnce()
    {
        var detections = 0;

        var provider = new ApplicationInfoProvider(
            new TestApplicationInfo { ProcessKind = null },
            () =>
            {
                detections++;

                return ProcessKind.Rider;
            } );

        _ = provider.ProcessKind;
        _ = provider.ProcessKind;

        Assert.Equal( 1, detections );
    }

    [Fact]
    public void ThePublicConstructorDetectsTheKindOfTheCurrentProcess()
    {
        var provider = new ApplicationInfoProvider( new TestApplicationInfo { ProcessKind = null } );

        Assert.Equal( ProcessKindDetector.GetCurrentProcessKind(), provider.ProcessKind );
    }
}
