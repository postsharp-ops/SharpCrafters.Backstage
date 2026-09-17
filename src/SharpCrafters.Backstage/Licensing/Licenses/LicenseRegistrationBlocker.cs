// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Backstage.Licensing.Licenses;

/// <summary>
/// Why a licence cannot be registered, or that nothing prevents it.
/// </summary>
/// <param name="Kind">The reason. <see cref="LicenseRegistrationBlockerKind.None"/> means that the licence can be registered.</param>
/// <param name="Message">
/// The reason in words, for the user, or <see langword="null"/> when there is no blocker.
/// </param>
/// <remarks>
/// The kind exists so that a test asserts what is wrong rather than matching the wording of a message, which changes.
/// The default value of the structure is <see cref="None"/>, so a licence that forgets to say anything is registrable
/// rather than blocked for a reason nobody can read.
/// </remarks>
internal readonly record struct LicenseRegistrationBlocker( LicenseRegistrationBlockerKind Kind, string? Message )
{
    /// <summary>
    /// Gets a value indicating whether the licence cannot be registered.
    /// </summary>
    public bool IsBlocked => this.Kind != LicenseRegistrationBlockerKind.None;

    /// <summary>
    /// Gets the blocker of a licence that can be registered.
    /// </summary>
    public static LicenseRegistrationBlocker None => default;

    /// <summary>
    /// Creates the blocker of a licence that cannot be used at all.
    /// </summary>
    /// <param name="message">The reason the licence cannot be used.</param>
    public static LicenseRegistrationBlocker Unusable( string message ) => new( LicenseRegistrationBlockerKind.Unusable, message );

    /// <summary>
    /// Gets the blocker of a redistribution license key.
    /// </summary>
    public static LicenseRegistrationBlocker Redistribution { get; } =
        new( LicenseRegistrationBlockerKind.Redistribution, "this is a redistribution license key" );
}
