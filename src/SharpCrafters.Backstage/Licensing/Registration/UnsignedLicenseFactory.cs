// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;

namespace SharpCrafters.Backstage.Licensing.Registration
{
    /// <summary>
    /// Builds the license keys that the product issues to itself, from the descriptions that the product family
    /// gives.
    /// </summary>
    /// <remarks>
    /// The division of labour is deliberate. The family says what the license says, because it is the only one that
    /// knows that the free edition of PostSharp is a PostSharp Ultimate key carrying the Community type. This class
    /// says how a key is built, because that is the same everywhere.
    /// </remarks>
    internal sealed class UnsignedLicenseFactory
    {
        private readonly IDateTimeProvider _time;
        private readonly RandomNumberGenerator _randomNumberGenerator;
        private readonly ILicenseProductCatalog _catalog;

        /// <summary>
        /// Initializes a new instance of the <see cref="UnsignedLicenseFactory"/> class.
        /// </summary>
        /// <param name="services">Services.</param>
        public UnsignedLicenseFactory( IServiceProvider services )
        {
            this._time = services.GetRequiredBackstageService<IDateTimeProvider>();
            this._randomNumberGenerator = services.GetRequiredBackstageService<RandomNumberGenerator>();
            this._catalog = services.GetRequiredBackstageService<ILicenseProductCatalog>();
        }

        /// <summary>
        /// Creates the trial license of the product family.
        /// </summary>
        public LicenseRegistrationProperties CreateEvaluationLicense() => this.Build( this._catalog.CreateTrialLicense( this._time.UtcNow ) );

        /// <summary>
        /// Creates the free license that a user registers without buying anything.
        /// </summary>
        /// <exception cref="InvalidOperationException">The family offers no such edition.</exception>
        public LicenseRegistrationProperties CreateCommunityLicense()
            => this.Build(
                this._catalog.CreateFreeLicense( this._time.UtcNow )
                ?? throw new InvalidOperationException( "The product family has no free edition." ) );

        /// <summary>
        /// Creates the free license that earlier versions of the product issued.
        /// </summary>
        /// <exception cref="InvalidOperationException">The family never had such an edition.</exception>
        [Obsolete]
        public LicenseRegistrationProperties CreateLegacyFreeLicense()
            => this.Build(
                this._catalog.CreateLegacyFreeLicense( this._time.UtcNow )
                ?? throw new InvalidOperationException( "The product family has no legacy free edition." ) );

        /// <summary>
        /// Builds and serializes the key of a described license.
        /// </summary>
        private LicenseRegistrationProperties Build( UnsignedLicense license )
        {
            var licenseKeyData = new LicenseKeyDataBuilder
            {
                Generation = LicenseGeneration.Current,
                LicenseGuid = this._randomNumberGenerator.NextGuid(),
                Product = license.Product,
                LicenseType = license.LicenseType,
                ValidFrom = license.ValidFrom,
                ValidTo = license.ValidTo,
                SubscriptionEndDate = license.SubscriptionEndDate
            };

            return licenseKeyData.Build().ToLicenseRegistrationProperties( this._catalog, licenseKeyData.Serialize() );
        }
    }
}
