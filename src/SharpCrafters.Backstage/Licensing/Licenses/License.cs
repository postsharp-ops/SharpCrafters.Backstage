// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCrafters.Backstage.Licensing.Licenses
{
    /// <summary>
    /// Represents a license serialized in a license key.
    /// </summary>
    internal sealed class License : AuditableLicense
    {
        private readonly string _licenseKey;

        private readonly IDateTimeProvider _dateTimeProvider;
        private readonly ILicensingAuthorityProvider _licensingAuthorityProvider;
        private readonly IApplicationInfo _applicationInfo;
        private readonly ProductProfile _productProfile;
        private readonly ILicenseProductCatalog _catalog;

        /// <summary>
        /// Initializes a new instance of the <see cref="License"/> class.
        /// </summary>
        /// <param name="licenseKey">The license key.</param>
        /// <param name="services">Services.</param>
        internal License( string licenseKey, IServiceProvider services ) : base( services )
        {
            this._licenseKey = CleanLicenseKey( licenseKey );
            this._dateTimeProvider = services.GetRequiredBackstageService<IDateTimeProvider>();
            this._licensingAuthorityProvider = services.GetRequiredBackstageService<ILicensingAuthorityProvider>();
            this._applicationInfo = services.GetRequiredBackstageService<IApplicationInfoProvider>().Application;
            this._productProfile = services.GetRequiredBackstageService<ProductProfile>();
            this._catalog = services.GetRequiredBackstageService<ILicenseProductCatalog>();
        }

        private static string CleanLicenseKey( string licenseKey )
        {
            var stringBuilder = new StringBuilder( licenseKey.Length );

            // Remove all spaces from the license.
            foreach ( var c in licenseKey )
            {
                if ( char.IsLetterOrDigit( c ) || c == '-' )
                {
                    stringBuilder.Append( c );
                }
            }

            return stringBuilder.ToString().ToUpperInvariant();
        }

        /// <inheritdoc />
        /// <remarks>
        /// A licence key carries everything it needs, so the operation completes synchronously and allocates nothing.
        /// The asynchronous shape exists for the licence leased from a license server, which is fetched over HTTP.
        /// </remarks>
        public override ValueTask<LicenseRegistrationBlocker> GetRegistrationBlockerAsync( CancellationToken cancellationToken = default )
            => new( this.GetRegistrationBlockerCore() );

        /// <inheritdoc />
        public override ValueTask<LicenseConsumptionResult> GetConsumptionPropertiesAsync(
            LicenseConsumptionOptions options,
            CancellationToken cancellationToken = default )
            => new(
                this.TryGetConsumptionPropertiesCore( options, out var properties, out var errorMessage, out var errorKind )
                    ? LicenseConsumptionResult.Success( properties )
                    : LicenseConsumptionResult.Failure( errorMessage, errorKind ) );

        /// <inheritdoc />
        public override ValueTask<LicenseRegistrationPropertiesResult> GetRegistrationPropertiesAsync( CancellationToken cancellationToken = default )
            => new(
                this.TryGetRegistrationPropertiesCore( out var properties, out var errorMessage )
                    ? LicenseRegistrationPropertiesResult.Success( properties )
                    : LicenseRegistrationPropertiesResult.Failure( errorMessage ) );

        private LicenseRegistrationBlocker GetRegistrationBlockerCore()
        {
            // Validates that the key can be consumed.
            if ( !this.TryGetConsumptionPropertiesCore(
                    LicenseConsumptionOptions.ForRegistration,
                    out var licenseConsumptionData,
                    out var errorMessage,
                    out _ ) )
            {
                return LicenseRegistrationBlocker.Unusable( errorMessage );
            }

#pragma warning disable CS0612 // Type or member is obsolete
            if ( licenseConsumptionData.IsRedistributable )
            {
                return LicenseRegistrationBlocker.Redistribution;
            }
#pragma warning restore CS0612 // Type or member is obsolete

            return LicenseRegistrationBlocker.None;
        }

        private bool TryGetConsumptionPropertiesCore(
            LicenseConsumptionOptions options,
            [MaybeNullWhen( false )] out LicenseConsumptionProperties licenseConsumptionProperties,
            [MaybeNullWhen( true )] out string errorMessage,
            out LicensingMessageKind errorKind )
        {
            licenseConsumptionProperties = null;

            // A key that fails one of the checks below is unusable whatever it is used for, which is what the default
            // says. The checks that mean something else set the kind themselves.
            errorKind = LicensingMessageKind.InvalidLicenseKey;

            if ( !this.TryGetLicenseKeyData( out var licenseKeyData, out errorMessage ) )
            {
                return false;
            }

#pragma warning disable CS0618
            if ( licenseKeyData.LicenseType == LicenseType.Anonymous )
            {
                errorMessage = "anonymous licenses are no longer supported";

                return false;
            }
#pragma warning restore CS0618

            if ( licenseKeyData.SignatureKeyId is { } signatureKeyId
                 && ProductionLicensingAuthorityProvider.KeyIdentifiers.Contains( signatureKeyId )
                 && (licenseKeyData is { LicenseId: not 0 and not 22 and < 100 } || RevokedLicenseKeys.Ids.Contains( licenseKeyData.LicenseId )) )
            {
                // The rule covers every production key, including the Elliptic Curve DSA key, so that a revoked license
                // cannot be reissued under another key. The license identifiers below 100 are used to test the
                // licensing authority.
                errorMessage = "the license key has been revoked";
                errorKind = LicensingMessageKind.Revoked;

                return false;
            }

            if ( !licenseKeyData.TryVerifySignature( this._licensingAuthorityProvider, out var signatureErrorMessage ) )
            {
                errorMessage = signatureErrorMessage;

                return false;
            }

            if ( licenseKeyData.ValidFrom.HasValue && licenseKeyData.ValidFrom > this._dateTimeProvider.UtcNow )
            {
                errorMessage = "the license key is not yet valid";

                return false;
            }

            if ( licenseKeyData.ValidTo.HasValue && licenseKeyData.ValidTo < this._dateTimeProvider.UtcNow )
            {
                errorMessage = "the license key has expired";
                errorKind = LicensingMessageKind.Expired;

                return false;
            }

            if ( licenseKeyData.ValidTo == null && licenseKeyData.SubscriptionEndDate == null && licenseKeyData.RequiresSignature()
                 && string.IsNullOrEmpty( licenseKeyData.Namespace ) && licenseKeyData.PublicKeyToken == null )
            {
                errorMessage = "the license key has neither a validity end date, nor a subscription end date, nor a namespace constraint";

                return false;
            }

            SubscriptionStatus subscriptionStatus;

            if ( licenseKeyData.SubscriptionEndDate != null )
            {
                if ( licenseKeyData.SubscriptionEndDate >= this._dateTimeProvider.UtcNow )
                {
                    subscriptionStatus = SubscriptionStatus.Active;
                }
                else if ( options.SubscriptionGracePeriod != null && licenseKeyData.SubscriptionEndDate.Value.Add( options.SubscriptionGracePeriod.Value )
                         >= this._dateTimeProvider.UtcNow )
                {
                    subscriptionStatus = SubscriptionStatus.Grace;
                }
                else
                {
                    subscriptionStatus = SubscriptionStatus.Expired;
                }
            }
            else
            {
                subscriptionStatus = SubscriptionStatus.None;
            }

            if ( !options.IgnoreSubscriptionPeriod )
            {
                if ( !this._applicationInfo.BuildDate.HasValue )
                {
                    throw new InvalidOperationException( $"Application '{this._applicationInfo.Name}' is missing build date information." );
                }

                var latestVendorComponent = this._applicationInfo.GetLatestVendorComponent( this._productProfile.Company );

                var endDate = licenseKeyData.SubscriptionEndDate;

                if ( endDate < latestVendorComponent.BuildDate )
                {
                    errorMessage =
                        $"the license key does not allow to use the licensed product '{latestVendorComponent.Name}' version {latestVendorComponent.PackageVersion} released on {latestVendorComponent.BuildDate:d} - only versions released before {licenseKeyData.SubscriptionEndDate:d} are allowed to use by this license";

                    // The key is valid and the subscription it carries is not, which is the same thing to the user as
                    // an expired key: they renew it.
                    errorKind = LicensingMessageKind.Expired;

                    return false;
                }
            }

            var licenseType = licenseKeyData.NormalizeLicenseType();
            var product = licenseKeyData.NormalizeProduct();

            if ( !this._catalog.IsProductOfFamily( product ) )
            {
                errorMessage = $"the license key is for {licenseKeyData.Product} and not for {this._productProfile.Name}";
                errorKind = LicensingMessageKind.WrongProductFamily;

                return false;
            }

#pragma warning disable CS0612 // Type or member is obsolete
            if ( licenseKeyData is { IsRedistribution: true, IsLimitedByNamespace: false } )
            {
                errorMessage = "is a redistribution license, but it is not limited by a namespace";

                return false;
            }
#pragma warning restore CS0612 // Type or member is obsolete

            errorMessage = null;
            errorKind = LicensingMessageKind.Default;

#pragma warning disable CS0618 // Type or member is obsolete
            var isRedistributable = licenseType is LicenseType.OpenSourceRedistribution or LicenseType.CommercialRedistribution;
#pragma warning restore CS0618 // Type or member is obsolete

            licenseConsumptionProperties = new LicenseConsumptionProperties(
                product,
                licenseType,
                licenseKeyData.Namespace,
                $"{licenseKeyData.GetDisplayName( this._catalog )}, Id={licenseKeyData.LicenseUniqueId}",
                licenseKeyData.GetMinPostSharpVersion(),
                licenseKeyData.LicenseString,
                isRedistributable,
                licenseKeyData.Auditable ?? true,
                licenseKeyData.ValidTo,
                licenseKeyData.SubscriptionEndDate,
                subscriptionStatus,
                licenseKeyData.Generation.GetValueOrDefault(),
                licenseKeyData.NormalizeServicingPhase( this._catalog ) );

            return true;
        }

        private bool TryGetRegistrationPropertiesCore(
            [MaybeNullWhen( false )] out LicenseRegistrationProperties licenseProperties,
            [MaybeNullWhen( true )] out string errorMessage )
        {
            if ( !this.TryGetLicenseKeyData( out var licenseKeyData, out errorMessage ) )
            {
                licenseProperties = null;

                return false;
            }

            if ( !licenseKeyData.TryVerifySignature( this._licensingAuthorityProvider, out var signatureErrorMessage ) )
            {
                errorMessage = $"The license key {licenseKeyData.LicenseUniqueId} cannot be used because {signatureErrorMessage}.";
                this.Logger.Warning?.Log( errorMessage );
                licenseProperties = null;

                return false;
            }

            licenseProperties = licenseKeyData.ToLicenseRegistrationProperties( this._catalog );

            return true;
        }

        private bool TryGetLicenseKeyData( [MaybeNullWhen( false )] out LicenseKeyData data, [MaybeNullWhen( true )] out string errorMessage )
        {
            this.Logger.Trace?.Log( $"Deserializing license '{this._licenseKey}'." );

            if ( !LicenseKeyData.TryDeserialize( this._licenseKey, out data, out errorMessage ) || !data.ValidateFields( out errorMessage ) )
            {
                errorMessage = $"Cannot parse the license key '{this._licenseKey}': {errorMessage}.";

                this.Logger.Error?.Log( errorMessage );

                return false;
            }
            else
            {
                this.Logger.Trace?.Log( $"Deserialized license: {data}" );

                return true;
            }
        }

        /// <inheritdoc />
        public override bool Equals( object? obj ) => obj is License license && this._licenseKey == license._licenseKey;

        /// <inheritdoc />
        public override int GetHashCode() => 668981160 + EqualityComparer<string>.Default.GetHashCode( this._licenseKey );

        /// <inheritdoc />
        public override string ToString() => this._licenseKey;
    }
}