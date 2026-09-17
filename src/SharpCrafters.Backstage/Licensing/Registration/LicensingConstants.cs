// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;

namespace SharpCrafters.Backstage.Licensing.Registration;

internal static class LicensingConstants
{
    /// <summary>
    /// Gets the time span of the evaluation license validity.
    /// </summary>
    internal static TimeSpan EvaluationPeriod { get; } = TimeSpan.FromDays( 45 );

    /// <summary>
    /// Gets the time span from the end of an evaluation license validity
    /// in which a new evaluation license cannot be registered.
    /// </summary>
    internal static TimeSpan NoEvaluationPeriod { get; } = TimeSpan.FromDays( 120 );

    public static TimeSpan LicenseExpirationWarningPeriod { get; } = TimeSpan.FromDays( 7 );

    public static TimeSpan SubscriptionExpirationWarningPeriod { get; } = TimeSpan.FromDays( 30 );

    /// <summary>
    /// Gets the minimal version of the product that can consume a registered license server URL.
    /// </summary>
    /// <remarks>
    /// A registered URL is stored in the group of this version, so that an earlier version skips it instead of
    /// reporting it as an invalid license key: every installed version reads the same <c>licensing.json</c>, and one
    /// that predates license server support would otherwise tell the user that what they registered is broken. It
    /// must stay at or below the version of the first release that ships this feature.
    /// </remarks>
    public static Version MinimalLicenseServerVersion { get; } = new( 2027, 0 );
}