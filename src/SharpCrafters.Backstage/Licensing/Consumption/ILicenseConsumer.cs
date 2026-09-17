// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;

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
    /// <returns>A value indicating whether the <paramref name="requirement"/> is satisfied.</returns>
    /// <remarks>
    /// This is synchronous although a licence may be leased from a license server over HTTP, because every licence of
    /// the consumer is resolved by the time <see cref="ILicenseConsumptionService.CreateConsumerAsync"/> returns. This
    /// is what contains the asynchrony: the method that a compilation calls once per requirement, on its critical
    /// path, neither waits nor allocates.
    /// </remarks>
    bool TryConsume( LicenseRequirement requirement, Action<LicensingMessage>? reportMessage = null, bool showsToastNotification = true );
}
