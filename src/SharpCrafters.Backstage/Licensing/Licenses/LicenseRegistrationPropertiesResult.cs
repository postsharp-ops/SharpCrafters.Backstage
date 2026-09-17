// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Registration;
using System.Diagnostics.CodeAnalysis;

namespace SharpCrafters.Backstage.Licensing.Licenses;

/// <summary>
/// What examining a licence for registration produced: either its properties, or the reason it cannot be read.
/// </summary>
/// <param name="Properties">The properties of the licence, or <see langword="null"/> when it cannot be read.</param>
/// <param name="ErrorMessage">The reason the licence cannot be read, or <see langword="null"/> when it can.</param>
internal readonly record struct LicenseRegistrationPropertiesResult( LicenseRegistrationProperties? Properties, string? ErrorMessage )
{
    [MemberNotNullWhen( true, nameof(Properties) )]
    [MemberNotNullWhen( false, nameof(ErrorMessage) )]
    public bool IsSuccess => this.Properties != null;

    public static LicenseRegistrationPropertiesResult Success( LicenseRegistrationProperties properties ) => new( properties, null );

    public static LicenseRegistrationPropertiesResult Failure( string errorMessage ) => new( null, errorMessage );
}
