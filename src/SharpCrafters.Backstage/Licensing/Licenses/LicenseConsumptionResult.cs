// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Consumption;
using System.Diagnostics.CodeAnalysis;

namespace SharpCrafters.Backstage.Licensing.Licenses;

/// <summary>
/// What examining a licence for consumption produced: either its properties, or the reason it cannot be used.
/// </summary>
/// <remarks>
/// A licence may have to be fetched from a license server, so the examination is asynchronous, and the
/// <c>Try…( out … )</c> shape that the rest of this assembly uses is not available to it.
/// </remarks>
/// <param name="Properties">The properties of the licence, or <see langword="null"/> when it cannot be used.</param>
/// <param name="ErrorMessage">The reason the licence cannot be used, or <see langword="null"/> when it can.</param>
internal readonly record struct LicenseConsumptionResult( LicenseConsumptionProperties? Properties, string? ErrorMessage )
{
    [MemberNotNullWhen( true, nameof(Properties) )]
    [MemberNotNullWhen( false, nameof(ErrorMessage) )]
    public bool IsSuccess => this.Properties != null;

    public static LicenseConsumptionResult Success( LicenseConsumptionProperties properties ) => new( properties, null );

    public static LicenseConsumptionResult Failure( string errorMessage ) => new( null, errorMessage );
}
