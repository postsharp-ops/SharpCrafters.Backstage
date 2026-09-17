// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using System;
using System.Diagnostics.CodeAnalysis;

namespace SharpCrafters.Backstage.Licensing.Licenses
{
    /// <summary>
    /// Creates an <see cref="ILicense" /> object from a license string.
    /// </summary>
    internal sealed class LicenseFactory
    {
        private readonly IServiceProvider _services;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="LicenseFactory"/> class.
        /// </summary>
        /// <param name="services">Services.</param>
        public LicenseFactory( IServiceProvider services )
        {
            this._services = services;
            this._logger = services.GetLoggerFactory().Licensing();
        }

        /// <summary>
        /// Attempts to create an <see cref="ILicense" /> object from a license string.
        /// </summary>
        /// <param name="licenseString">The license string. E.g. license key or license server URL.</param>
        /// <param name="license">The <see cref="ILicense" /> object represented by the <paramref name="licenseString"/>.</param>
        /// <param name="errorMessage">Description of a failure when the return value is <c>false</c>.</param>
        /// <returns>A value indicating if the <paramref name="licenseString"/> represents a license.</returns>
        public bool TryCreate( string? licenseString, [MaybeNullWhen( false )] out ILicense license, [MaybeNullWhen( true )] out string errorMessage )
        {
            licenseString = licenseString?.Trim();

            if ( string.IsNullOrEmpty( licenseString ) )
            {
                errorMessage = "Empty license string provided.";
                this._logger.Error?.Log( errorMessage );
                license = null;

                return false;
            }

            // ReSharper disable once RedundantSuppressNullableWarningExpression
            if ( LicenseServerUrl.IsLicenseServerUrl( licenseString!, out var urlErrorMessage ) )
            {
                // The lease is not acquired here: this method is called wherever a license string is turned into a
                // license, including where no network call is acceptable, so the server is contacted only when the
                // license is consumed or registered.
                // ReSharper disable once RedundantSuppressNullableWarningExpression
                license = new LeasedLicense( licenseString!, this._services );
                errorMessage = null;

                return true;
            }
            else if ( Uri.IsWellFormedUriString( licenseString, UriKind.Absolute ) )
            {
                // A well-formed absolute URL is not a license key either, so the reason it is not a valid license
                // server URL is more useful to the user than letting it fail later as an unparsable key.
                errorMessage = urlErrorMessage;
                this._logger.Error?.Log( errorMessage );
                license = null;

                return false;
            }
            else
            {
                // ReSharper disable once RedundantSuppressNullableWarningExpression
                license = new License( licenseString!, this._services );
                errorMessage = null;

                return true;
            }
        }
    }
}