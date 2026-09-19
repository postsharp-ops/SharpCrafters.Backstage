// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Licensing.Registration;

/// <summary>
/// Whether an edition can be registered on this machine as things stand, and why not when it cannot.
/// </summary>
/// <remarks>
/// The default value is <see cref="Available"/>, so an edition that says nothing is offered rather than withheld for a
/// reason nobody can read. This mirrors <c>LicenseRegistrationBlocker</c>, which is the same shape for the same reason.
/// </remarks>
[PublicAPI]
public readonly record struct SelfRegisteredEditionAvailability
{
    private SelfRegisteredEditionAvailability( string message )
    {
        this.Message = message;
    }

    /// <summary>
    /// Gets the reason the edition cannot be registered, or <see langword="null"/> when it can.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets a value indicating whether the edition can be registered.
    /// </summary>
    public bool IsAvailable => this.Message == null;

    /// <summary>
    /// Gets the availability of an edition that can be registered.
    /// </summary>
    public static SelfRegisteredEditionAvailability Available => default;

    /// <summary>
    /// Creates the availability of an edition that cannot be registered.
    /// </summary>
    /// <param name="message">The reason, for the user.</param>
    public static SelfRegisteredEditionAvailability Unavailable( string message ) => new( message );
}
