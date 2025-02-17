// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Metalama.Backstage.Application;
using Metalama.Backstage.Infrastructure;
using Metalama.Backstage.Licensing.Licenses.LicenseFields;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Metalama.Backstage.Licensing.Licenses
{
    public partial record LicenseKeyData
    {
        internal bool Validate(
            IDateTimeProvider dateTimeProvider,
            IApplicationInfo applicationInfo,
            LicensingAuthority licensingAuthority,
            [MaybeNullWhen( true )] out string errorDescription )
        {
#pragma warning disable CS0618
            if ( this.LicenseType == LicenseType.Anonymous )
            {
                // Anonymous licenses are always valid but confer no right.
                errorDescription = null;

                return true;
            }
#pragma warning restore CS0618

            if ( this.SignatureKeyId == 0 && this.LicenseId is not 0 and < 20 )
            {
                errorDescription = "the license key has been revoked";

                return false;
            }

            if ( this.RequiresSignature && !this.VerifySignature( licensingAuthority ) )
            {
                errorDescription = "the license key has an invalid signature";

                return false;
            }

            if ( this.ValidFrom.HasValue && this.ValidFrom > dateTimeProvider.UtcNow )
            {
                errorDescription = "the license key is not yet valid";

                return false;
            }

            if ( this.ValidTo.HasValue && this.ValidTo < dateTimeProvider.UtcNow )
            {
                errorDescription = "the license key has expired";

                return false;
            }

            if ( !Enum.IsDefined( typeof(LicenseType), this.LicenseType ) )
            {
                errorDescription = "the license key license type is unknown";

                return false;
            }

            if ( !Enum.IsDefined( typeof(LicensedProduct), this.Product ) )
            {
                errorDescription = "the license key licensed product is unknown";

                return false;
            }

            if ( this._fields.Keys.Any(
                    i =>
                        i.IsMustUnderstand()
                        && !Enum.IsDefined( typeof(LicenseFieldIndex), i ) ) )
            {
                errorDescription = "the license key contains unknown must-understand fields";

                return false;
            }

            if ( this.SubscriptionEndDate.HasValue )
            {
                if ( !applicationInfo.BuildDate.HasValue )
                {
                    throw new InvalidOperationException( $"Application '{applicationInfo.Name}' is missing build date information." );
                }

                var latestComponentMadeByPostSharp = applicationInfo.GetLatestComponentMadeByPostSharp();

                if ( this.SubscriptionEndDate < latestComponentMadeByPostSharp.BuildDate )
                {
                    errorDescription =
                        $"the license key does not allow to use the licensed product '{latestComponentMadeByPostSharp.Name}' version {latestComponentMadeByPostSharp.PackageVersion} released on {latestComponentMadeByPostSharp.BuildDate:d} - only versions released before {this.SubscriptionEndDate:d} are allowed to use by this license";

                    return false;
                }
            }

            if ( this.IsRedistribution && !this.IsLimitedByNamespace )
            {
                errorDescription = "is a redistribution license, but it is not limited by a namespace";

                return false;
            }

            errorDescription = null;

            return true;
        }

        public bool VerifySignature( LicensingAuthority licensingAuthority )
        {
            if ( !this.RequiresSignature() )
            {
                return true;
            }

            if ( this.Signature == null )
            {
                return false;
            }

            if ( licensingAuthority.KeyId != this.SignatureKeyId )
            {
                throw new ArgumentOutOfRangeException( nameof(licensingAuthority), "Licensing authority mistmatch." );
            }

            var buffer = this.GetSignedBuffer();

            return licensingAuthority.VerifySignature( buffer, this.Signature );
        }
    }
}