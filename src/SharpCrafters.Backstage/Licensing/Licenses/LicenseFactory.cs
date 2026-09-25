// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Consumption;
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
        private readonly LicenseServerUrlValidator _licenseServerUrlValidator;

        /// <summary>
        /// Initializes a new instance of the <see cref="LicenseFactory"/> class.
        /// </summary>
        /// <param name="services">Services.</param>
        public LicenseFactory( IServiceProvider services )
        {
            this._services = services;
            this._logger = services.GetLoggerFactory().Licensing();
            this._licenseServerUrlValidator = services.GetRequiredBackstageService<LicenseServerUrlValidator>();
        }

        /// <summary>
        /// Attempts to create an <see cref="ILicense" /> object from a license string.
        /// </summary>
        /// <param name="licenseString">The license string. E.g. license key or license server URL.</param>
        /// <param name="reportMessage">
        /// Action called with what the license string is worth saying about although it does produce a license, such
        /// as a license server that is reached over HTTP. It is not called for a failure, which the caller learns
        /// from <paramref name="errorMessage"/>.
        /// </param>
        /// <param name="license">The <see cref="ILicense" /> object represented by the <paramref name="licenseString"/>.</param>
        /// <param name="errorMessage">Description of a failure when the return value is <c>false</c>.</param>
        /// <returns>A value indicating if the <paramref name="licenseString"/> represents a license.</returns>
        public bool TryCreate(
            string? licenseString,
            Action<LicensingMessage>? reportMessage,
            [MaybeNullWhen( false )] out ILicense license,
            [MaybeNullWhen( true )] out string errorMessage )
        {
            licenseString = licenseString?.Trim();

            if ( string.IsNullOrEmpty( licenseString ) )
            {
                errorMessage = "Empty license string provided.";
                this._logger.Error?.Log( errorMessage );
                license = null;

                return false;
            }

            // A license key is never a well-formed absolute URI, so anything that is one was meant to be a license
            // server, and the reason a URL is refused is more useful to the user than letting it fail later as an
            // unparsable key.
            if ( Uri.IsWellFormedUriString( licenseString, UriKind.Absolute ) )
            {
                if ( !this._licenseServerUrlValidator.TryValidate( licenseString, out var urlErrorMessage, out var warning ) )
                {
                    errorMessage = urlErrorMessage;
                    this._logger.Error?.Log( errorMessage );
                    license = null;

                    return false;
                }

                if ( warning != null )
                {
                    reportMessage?.Invoke( new LicensingMessage( warning, LicensingMessageKind.InsecureLicenseServer ) );
                    this._logger.Warning?.Log( warning );
                }

                // The lease is not acquired here: this method is called wherever a license string is turned into a
                // license, including where no network call is acceptable, so the server is contacted only when the
                // license is consumed or registered.
                // ReSharper disable once RedundantSuppressNullableWarningExpression
                license = new LeasedLicense( licenseString!, this._services );
                errorMessage = null;

                return true;
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