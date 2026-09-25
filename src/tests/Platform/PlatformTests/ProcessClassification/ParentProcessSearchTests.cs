// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Testing;
using Xunit;

namespace SharpCrafters.Backstage.PlatformTests.ProcessClassification;

/// <summary>
/// Runs the parent process search of Linux, which reads <c>/proc</c>, and of macOS, which runs <c>ps</c>. The helper runs
/// under an apphost as the parent of a second helper, which searches its parents. The search of Windows runs in the unit
/// tests on a Windows host.
/// </summary>
public sealed class ParentProcessSearchTests
{
    /// <summary>
    /// Linux keeps 15 characters of a process name in <c>/proc/&lt;pid&gt;/comm</c>.
    /// </summary>
    [PlatformFact( TestPlatforms.Unix )]
    public async Task AParentWithALongNameIsReportedUnderItsFullName()
    {
        Assert.Equal( "sharpcrafters.backstage.platformtesthelper", await GetNameOfParentAsync( HelperProcess.AppHostPath ) );
    }

    /// <summary>
    /// <c>/proc/&lt;pid&gt;/stat</c> gives the name between parentheses before the parent process identifier, and
    /// <c>ps</c> gives the path before the arguments, so a space or a parenthesis in the name must not shift the fields.
    /// </summary>
    [PlatformFact( TestPlatforms.Unix )]
    public async Task AParentWithASpaceAndAParenthesisInItsNameIsReportedUnderItsName()
    {
        // The apphost finds the helper assembly in its own directory, so the copy is made beside it.
        var appHostPath = Path.Combine( AppContext.BaseDirectory, "Platform Test (Helper)" );
        File.Copy( HelperProcess.AppHostPath, appHostPath, overwrite: true );

        Assert.Equal( "platform test (helper)", await GetNameOfParentAsync( appHostPath ) );
    }

    private static async Task<string> GetNameOfParentAsync( string appHostPath )
    {
        using var helper = HelperProcess.StartAppHost( appHostPath, HelperCommands.Spawn, HelperCommands.PrintParents );
        var parents = await helper.ReadUntilAsync( HelperCommands.EndLine );
        await helper.WaitForExitAsync();

        Assert.NotEmpty( parents );

        // Each line is "<process id> <process name>", and the first line is the direct parent, which is the apphost.
        var firstParent = parents[0].Split( ' ', 2 );

        Assert.Equal( helper.Process.Id.ToString( System.Globalization.CultureInfo.InvariantCulture ), firstParent[0] );

        return firstParent[1];
    }
}
