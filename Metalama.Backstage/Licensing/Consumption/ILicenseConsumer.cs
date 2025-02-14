// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;

namespace Metalama.Backstage.Licensing.Consumption;

[PublicAPI]
public interface ILicenseConsumer
{
    /// <summary>
    /// Provides information about availability of <paramref name="requirement"/>.
    /// </summary>
    /// <param name="requirement">The required license requirement.</param>
    /// <returns>A value indicating if the <paramref name="requirement"/> is available.</returns>
    bool CanConsume( Predicate<LicenseConsumptionData> requirement );

    bool IsTrialLicense { get; }

    string? LicenseString { get; }
}