// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using System;
using System.Collections.Immutable;

namespace SharpCrafters.Backstage.Licensing.Consumption;

[PublicAPI]
public sealed record LicenseConsumptionOptions
{
    public string? ProjectLicenseKey { get; init; }

    /// <summary>
    /// Gets the licenses that the application supplies for the project, each with a description of where the
    /// application read it from. They are considered in the order of this list and before
    /// <see cref="ProjectLicenseKey"/>. Like <see cref="ProjectLicenseKey"/>, they are of kind
    /// <see cref="LicenseSourceKind.Project"/> and <see cref="IgnoredLicenseSources"/> does not apply to them: the
    /// application supplied them for this call, so it can leave out what it does not want considered.
    /// </summary>
    /// <remarks>
    /// <see cref="ProjectLicenseKey"/> is the short form of this list, for an application that reads its licenses from
    /// one place only. An application that has several places, such as a command-line argument and a configuration
    /// file, uses this list so that a message about an unusable license names the place the license came from.
    /// </remarks>
    public ImmutableArray<ExplicitLicense> ExplicitLicenses { get; init; } = ImmutableArray<ExplicitLicense>.Empty;

    public string? ProjectName { get; init; }

    public TimeSpan? SubscriptionGracePeriod { get; init; }

    public LicenseSourceKind IgnoredLicenseSources { get; init; } = LicenseSourceKind.None;

    /// <summary>
    /// Gets a value indicating whether to ignore the condition that requires the build date to be within the subscription period.
    /// This property is enabled when license keys are registered, so the build date of the registration tool does not interfere
    /// (only the build date of the consuming tool matters).
    /// </summary>
    internal bool IgnoreSubscriptionPeriod { get; private init; }

    /// <summary>
    /// Gets a value indicating whether obsolete license types and products should be accepted.
    /// This property is enabled when license keys are registered, because the registration tool may be of a
    /// more recent version than the consuming tool.
    /// </summary>
    internal bool AcceptsObsoleteLicenses { get; private init; }

    public static LicenseConsumptionOptions Default { get; } = new();

    // While registering license keys, we ignore the subscription period, i.e. we allow to register the license key for any build
    // of the application. This allows to use a recent build of the licensing registration service to register a license key that will
    // be used by a more recent consumer.
    internal static LicenseConsumptionOptions ForRegistration { get; } = new() { IgnoreSubscriptionPeriod = true, AcceptsObsoleteLicenses = true };
}