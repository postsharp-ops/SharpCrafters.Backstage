// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.ProcessClassification;
using System;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Licensing.Consumption.Sources
{
    internal abstract class LicenseSourceBase : ILicenseSource
    {
        private readonly IServiceProvider _services;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IUnattendedProcessDetector _unattendedProcessDetector;

        public abstract string Description { get; }

        public abstract LicenseSourceKind Kind { get; }

        protected LicenseSourceBase( IServiceProvider services )
        {
            this._services = services;
            this._loggerFactory = services.GetLoggerFactory();
            this._unattendedProcessDetector = services.GetRequiredBackstageService<IUnattendedProcessDetector>();
        }

        /// <summary>
        /// Gets the license strings of the source, in the order in which they should be considered.
        /// </summary>
        /// <param name="reportMessage">Action to be called when a license string is present but unusable.</param>
        /// <remarks>
        /// The method yields the strings and not their parsed properties, because a license string is a license key
        /// <b>or</b> the URL of a license server, and only <see cref="LicenseFactory"/> decides which. A source that
        /// parsed the string itself would reject a URL before the factory ever saw it.
        /// </remarks>
        protected abstract IEnumerable<string> GetLicenseStrings( Action<LicensingMessage> reportMessage );

        /// <inheritdoc />
        public IEnumerable<ILicense> GetLicenses( Action<LicensingMessage> reportMessage )
        {
            var licenseFactory = new LicenseFactory( this._services );

            // Asking the question logs, and most sources hold no license server at all, so it is asked at most once
            // and only when a license server is actually met.

            foreach ( var licenseString in this.GetLicenseStrings( reportMessage ) )
            {
                if ( LicenseServerUrl.IsLicenseServerUrl( licenseString ) )
                {
                    if ( this._unattendedProcessDetector.IsCurrentProcessUnattended )
                    {
                        // An unattended process is licensed by the unattended license, which costs nothing, so it must
                        // never take a seat from the pool of the team: a build server builds far more often than a
                        // developer, and it would hold seats that the people who need them cannot get.
                        //
                        // The license server is skipped silently rather than reported. What is configured is right and
                        // the process it does not apply to is not the one whose user could act on a message; saying it
                        // would mean a warning on every continuous integration build, for ever.
                        this._loggerFactory.Licensing()
                            .Trace?.Log( $"Ignoring the license server '{licenseString}': this process is unattended." );

                        continue;
                    }
                }

                if ( licenseFactory.TryCreate( licenseString, reportMessage, out var license, out var errorMessage ) )
                {
                    yield return license;
                }
                else
                {
                    reportMessage( new LicensingMessage( errorMessage ) { Kind = LicensingMessageKind.InvalidLicenseKey } );
                }
            }
        }

        public event Action? Changed;

        public abstract LicenseSourcePriority Priority { get; }

        public bool SupportsRegistration => true;

        protected void OnChanged() => this.Changed?.Invoke();

        public override string ToString() => this.GetType().Name;
    }
}