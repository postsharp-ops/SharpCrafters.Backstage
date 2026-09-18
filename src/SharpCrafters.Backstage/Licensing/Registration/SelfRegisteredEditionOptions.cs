// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing.Consumption;
using System;

namespace SharpCrafters.Backstage.Licensing.Registration;

/// <summary>
/// What the caller of <see cref="ILicenseRegistrationService.Register"/> chose: the inputs of a registration that are
/// not the edition itself.
/// </summary>
/// <remarks>
/// A record rather than a parameter list, so that an edition which later needs another input does not change the
/// signature of every caller.
/// </remarks>
[PublicAPI]
public sealed record SelfRegisteredEditionOptions
{
    /// <summary>
    /// Gets the reason the user is entitled to the edition, for an edition that asks for one.
    /// </summary>
    /// <remarks>
    /// Only an edition whose <see cref="SelfRegisteredEdition.RequiresReason"/> is <see langword="true"/> reads it.
    /// </remarks>
    public CommunityLicenseReason CommunityLicenseReason { get; init; }

    /// <summary>
    /// Gets the sink of the messages that the registration reports, or <see langword="null"/> to discard them.
    /// </summary>
    /// <remarks>
    /// This is how an edition says something that does not stop the registration: that it replaced a key already held,
    /// for instance. A failure is reported by the result and not here.
    /// </remarks>
    public Action<LicensingMessage>? ReportMessage { get; init; }
}
