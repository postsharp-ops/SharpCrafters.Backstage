// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Metalama.Backstage.Extensibility;
using Metalama.Backstage.Licensing.Consumption.Sources;
using System;
using System.Collections.Immutable;

namespace Metalama.Backstage.Licensing.Consumption
{
    /// <summary>
    /// Exposes a service to verify the current license and consume features from iut.
    /// </summary>
    [PublicAPI]
    public interface ILicenseConsumptionService : IBackstageService
    {
        ILicenseConsumer CreateConsumer(
            string? projectLicenseKey,
            LicenseSourceKind ignoredLicenseKinds,
            out ImmutableArray<LicensingMessage> messages );

        ILicenseConsumer CreateConsumer(
            string? projectLicenseKey = null,
            LicenseSourceKind ignoredLicenseKinds = LicenseSourceKind.None );

        event Action? Changed;
    }
}