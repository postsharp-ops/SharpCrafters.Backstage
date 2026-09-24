// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using System;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Diagnostics;

/// <summary>
/// A user who asks for a debugger gets one launched where a just-in-time debugger exists, and is otherwise told to
/// attach one by hand, so that a debugging request is not silently ignored on Linux, on macOS or in a container.
/// </summary>
public sealed class DebuggerHelperTests
{
    [Theory]

    // Without an override, only Windows outside a container has a just-in-time debugger.
    [InlineData( null, true, false, true )]
    [InlineData( null, true, true, false )]
    [InlineData( null, false, false, false )]

    // The override wins in both directions, in the two forms it accepts.
    [InlineData( "0", true, false, false )]
    [InlineData( "1", false, false, true )]
    [InlineData( "false", true, false, false )]
    [InlineData( "True", false, true, true )]

    // A value that is neither form is ignored.
    [InlineData( "yes", true, false, true )]
    [InlineData( "yes", false, false, false )]
    public void JustInTimeDebuggerAvailability( string? environmentVariableValue, bool isWindows, bool isRunningInContainer, bool expected )
        => Assert.Equal(
            expected,
            DebuggerHelper.CanLaunchJustInTimeDebuggerCore( environmentVariableValue, isWindows, _ => isRunningInContainer ) );

    [Fact]
    public void OverrideDoesNotProbeTheContainer()
        => Assert.True(
            DebuggerHelper.CanLaunchJustInTimeDebuggerCore(
                "1",
                true,
                _ => throw new InvalidOperationException( "The container must not be probed when the variable decides." ) ) );
}
