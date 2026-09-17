// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SharpCrafters.Backstage.Licensing.Consumption.Sources
{
    internal abstract class LicenseSourceBase : ILicenseSource
    {
        private readonly IServiceProvider _services;

        public abstract string Description { get; }

        public abstract LicenseSourceKind Kind { get; }

        protected LicenseSourceBase( IServiceProvider services )
        {
            this._services = services;
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

#pragma warning disable CS1998 // The method has no await: the strings come from a configuration file, but a license
                              // among them may be leased from a server, which is fetched when it is consumed.
        public async IAsyncEnumerable<ILicense> GetLicensesAsync(
            Action<LicensingMessage> reportMessage,
            [EnumeratorCancellation] CancellationToken cancellationToken = default )
#pragma warning restore CS1998
        {
            var licenseFactory = new LicenseFactory( this._services );

            foreach ( var licenseString in this.GetLicenseStrings( reportMessage ) )
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ( licenseFactory.TryCreate( licenseString, out var license, out var errorMessage ) )
                {
                    yield return license;
                }
                else
                {
                    reportMessage( new LicensingMessage( errorMessage ) );
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
