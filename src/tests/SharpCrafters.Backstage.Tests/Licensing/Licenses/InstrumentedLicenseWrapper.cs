// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Tests.Licensing.Licenses
{
    internal sealed class InstrumentedLicenseWrapper : ILicense, IUsable
    {
        private readonly ILicense _license;

        public int NumberOfAuditReports { get; private set; }

        public InstrumentedLicenseWrapper( ILicense license )
        {
            this._license = license;
        }

        public ValueTask<string?> GetRegistrationBlockerAsync( CancellationToken cancellationToken = default )
            => throw new NotImplementedException();

        public ValueTask<LicenseConsumptionResult> GetConsumptionPropertiesAsync(
            LicenseConsumptionOptions options,
            CancellationToken cancellationToken = default )
            => this._license.GetConsumptionPropertiesAsync( options, cancellationToken );

        public ValueTask<LicenseRegistrationPropertiesResult> GetRegistrationPropertiesAsync( CancellationToken cancellationToken = default )
            => this._license.GetRegistrationPropertiesAsync( cancellationToken );

        public void ReportUse()
        {
            this.NumberOfAuditReports++;
            this._license.ReportUse();
        }

        public void ResetNumberOfAuditReports()
        {
            this.NumberOfAuditReports = 0;
        }

        public override bool Equals( object? obj )
        {
            return obj is InstrumentedLicenseWrapper license &&
                   EqualityComparer<ILicense>.Default.Equals( this._license, license._license );
        }

        public override int GetHashCode()
        {
            return HashCode.Combine( this._license );
        }
    }
}
