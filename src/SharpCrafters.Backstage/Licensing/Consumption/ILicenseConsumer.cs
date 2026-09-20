// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;
using System.Collections.Immutable;

namespace SharpCrafters.Backstage.Licensing.Consumption;

[PublicAPI]
public interface ILicenseConsumer
{
    /// <summary>
    /// Gets the licenses that the consumer holds, in the order in which <see cref="TryConsume"/> considers them.
    /// These are the licenses that were found and are usable; a license that was found and is not usable was reported
    /// when the consumer was created.
    /// </summary>
    /// <remarks>
    /// An application reads this to tell the user what it found, which is a different message from the one
    /// <see cref="TryConsume"/> produces: a build with no license at all asks the user to register one, and a build
    /// with a license that does not cover a feature names the license the user holds.
    /// </remarks>
    ImmutableArray<LicenseConsumptionProperties> Licenses { get; }

    /// <summary>
    /// Attempts to consume a license. If it succeeds, marks the license for audit.
    /// </summary>
    /// <param name="requirement">A predicate indicating whether the license key can be consumed.</param>
    /// <param name="reportMessage">A delegate that receives the message explaining why the requirement is not satisfied.</param>
    /// <param name="showsToastNotification">Whether to notify the user when the requirement is not satisfied.</param>
    /// <returns>A value indicating whether the <paramref name="requirement"/> is satisfied.</returns>
    /// <remarks>
    /// This is synchronous although a licence may be leased from a license server over HTTP, because every licence of
    /// the consumer is resolved by the time <see cref="ILicenseConsumptionService.CreateConsumerAsync"/> returns. This
    /// is what contains the asynchrony: the method that a compilation calls once per requirement, on its critical
    /// path, neither waits nor allocates. It follows that this method <b>never contacts a license server and never
    /// takes a seat</b>; the lease was acquired when the consumer was created. See <c>docs/license-server.md</c>.
    /// </remarks>
    bool TryConsume( LicenseRequirement requirement, Action<LicensingMessage>? reportMessage = null, bool showsToastNotification = true );
}
