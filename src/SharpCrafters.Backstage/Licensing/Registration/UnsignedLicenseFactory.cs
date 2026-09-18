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
    /// Builds the license keys that the product issues to itself, from the descriptions that the editions give.
    /// </summary>
    /// <remarks>
    /// The division of labour is deliberate. A <see cref="SelfRegisteredEdition"/> says what the license says, because
    /// it is the only one that knows that the free edition of PostSharp is a PostSharp Ultimate key carrying the
    /// Community type. This class says how a key is built, because that is the same everywhere.
    /// </remarks>
    internal sealed class UnsignedLicenseFactory
    {
        private readonly RandomNumberGenerator _randomNumberGenerator;
        private readonly ILicenseProductCatalog _catalog;

        /// <summary>
        /// Initializes a new instance of the <see cref="UnsignedLicenseFactory"/> class.
        /// </summary>
        /// <param name="services">Services.</param>
        public UnsignedLicenseFactory( IServiceProvider services )
        {
            this._randomNumberGenerator = services.GetRequiredBackstageService<RandomNumberGenerator>();
            this._catalog = services.GetRequiredBackstageService<ILicenseProductCatalog>();
        }

        /// <summary>
        /// Builds and serializes the key of a described license.
        /// </summary>
        /// <remarks>
        /// The key carries a random identifier instead of the identifier of a sold license, which is what marks it as
        /// self-created, and it carries no signature: such a key is validated by the rules for self-created licenses
        /// rather than against a licensing authority.
        /// </remarks>
        public LicenseRegistrationProperties CreateLicenseKey( UnsignedLicense license )
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
