// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.Collections.Generic;

namespace SharpCrafters.Aspects.Introspection.Query.Projects;

/// <summary>
/// Compares the versions of .NET SDKs by the precedence of semantic versioning: the numbers of the version, then a release above
/// its prereleases, then the identifiers of the prerelease label from left to right, a number compared numerically and below a
/// text, which is compared ordinally. <c>11.0.100-rc.2.1</c> is above <c>11.0.100-rc.1.1</c>, and <c>11.0.100</c> above both.
/// </summary>
internal sealed class SdkVersionComparer : IComparer<string>
{
    public static SdkVersionComparer Instance { get; } = new();

    private SdkVersionComparer() { }

    public int Compare( string? x, string? y )
    {
        if ( x == null || y == null )
        {
            return x == null ? y == null ? 0 : -1 : 1;
        }

        var (xCore, xLabel) = Split( x );
        var (yCore, yLabel) = Split( y );

        var result = CompareIdentifiers( xCore.Split( '.' ), yCore.Split( '.' ) );

        if ( result != 0 )
        {
            return result;
        }

        // A release is above its prereleases.
        if ( xLabel == null || yLabel == null )
        {
            return xLabel == null ? yLabel == null ? 0 : 1 : -1;
        }

        return CompareIdentifiers( xLabel.Split( '.' ), yLabel.Split( '.' ) );
    }

    // The version without the build metadata, split at the start of its prerelease label.
    private static (string Core, string? Label) Split( string version )
    {
        var plus = version.IndexOf( '+', StringComparison.Ordinal );

        if ( plus >= 0 )
        {
            version = version[..plus];
        }

        var dash = version.IndexOf( '-', StringComparison.Ordinal );

        return dash < 0 ? (version, null) : (version[..dash], version[(dash + 1)..]);
    }

    private static int CompareIdentifiers( string[] x, string[] y )
    {
        for ( var i = 0; i < Math.Min( x.Length, y.Length ); i++ )
        {
            var xIsNumber = long.TryParse( x[i], out var xNumber );
            var yIsNumber = long.TryParse( y[i], out var yNumber );

            var result = (xIsNumber, yIsNumber) switch
            {
                (true, true) => xNumber.CompareTo( yNumber ),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal( x[i], y[i] )
            };

            if ( result != 0 )
            {
                return result;
            }
        }

        // With equal identifiers, the version that has more of them is above.
        return x.Length.CompareTo( y.Length );
    }
}
