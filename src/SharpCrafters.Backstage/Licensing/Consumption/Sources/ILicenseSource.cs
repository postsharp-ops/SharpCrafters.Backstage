// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Licensing.Consumption.Sources
{
    /// <summary>
    /// Source of licenses for consumption.
    /// </summary>
    internal interface ILicenseSource
    {
        /// <summary>
        /// Gets a description of the license source.
        /// </summary>
        string Description { get; }

        LicenseSourceKind Kind { get; }

        /// <summary>
        /// Gets the licenses of the source, if any. <paramref name="reportMessage"/> is called for each one that is
        /// present but unusable.
        /// </summary>
        /// <param name="reportMessage">Action to be called when a license is invalid.</param>
        /// <returns>The licenses of the source, which may be none.</returns>
        /// <remarks>
        /// Enumerating a source costs no I/O: it turns license strings into <see cref="ILicense"/> objects and does
        /// nothing else. A license server is contacted when the licence that stands for it is resolved, which
        /// <see cref="ILicenseConsumptionService.CreateConsumerAsync"/> does after the enumeration.
        /// </remarks>
        IEnumerable<ILicense> GetLicenses( Action<LicensingMessage> reportMessage );

        /// <summary>
        /// Event raised when the current source has changed.
        /// </summary>
        event Action? Changed;

        LicenseSourcePriority Priority { get; }

        /// <summary>
        /// Determines whether the <see cref="ILicense.GetRegistrationBlockerAsync"/> and
        /// <see cref="ILicense.GetRegistrationPropertiesAsync"/> methods are supported.
        /// </summary>
        bool SupportsRegistration { get; }
    }

    [PublicAPI]
    public enum LicenseSourcePriority
    {
        Unattended,
        Explicit,
        UserProfile
    }
}