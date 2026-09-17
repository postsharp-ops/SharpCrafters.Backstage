// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.Consumption;

[PublicAPI]
public interface ILicenseConsumer
{
    /// <summary>
    /// Attempts to consume a license. If it succeeds, marks the license for audit.
    /// </summary>
    /// <param name="requirement">A predicate indicating whether the license key can be consumed.</param>
    /// <param name="reportMessage">A delegate that receives the message explaining why the requirement is not satisfied.</param>
    /// <param name="showsToastNotification">Whether to notify the user when the requirement is not satisfied.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A value indicating whether the <paramref name="requirement"/> is satisfied.</returns>
    /// <remarks>
    /// This is asynchronous because a licence is consumed when it is really used, and a licence leased from a license
    /// server is acquired at that moment: taking a seat from the pool of a team is a decision about a licence that is
    /// being used, not about one that might have been. A licence that is already available, which is every licence key
    /// and a lease that is already held, completes synchronously and allocates nothing.
    /// </remarks>
    ValueTask<bool> TryConsumeAsync(
        LicenseRequirement requirement,
        Action<LicensingMessage>? reportMessage = null,
        bool showsToastNotification = true,
        CancellationToken cancellationToken = default );

    /// <inheritdoc cref="TryConsumeAsync"/>
    [Obsolete(
        "Use TryConsumeAsync. This overload blocks the calling thread when a license has to be acquired from a "
        + "license server, which stalls a user interface thread." )]
    bool TryConsume( LicenseRequirement requirement, Action<LicensingMessage>? reportMessage = null, bool showsToastNotification = true );
}
