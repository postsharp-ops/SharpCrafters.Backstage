// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Collections.Generic;
using System.Threading;

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
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The licenses of the source, which may be none.</returns>
        /// <remarks>
        /// The sequence is asynchronous because a license server is contacted over HTTP while it is enumerated.
        /// </remarks>
        IAsyncEnumerable<ILicense> GetLicensesAsync( Action<LicensingMessage> reportMessage, CancellationToken cancellationToken = default );

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