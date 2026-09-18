// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing.Licenses.LicenseFields;
using SharpCrafters.Backstage.Licensing.Registration;
using System;

namespace SharpCrafters.Backstage.Licensing.Licenses
{
    /// <summary>
    /// Provides extension methods for processing license key data for license consumption, registration and audit.
    /// </summary>
    public static class LicenseKeyDataExtensions
    {
        /// <summary>
        /// The first version of PostSharp whose reader skips a field it does not know instead of rejecting the key.
        /// The same tolerance was released as 6.5.17 and 6.8.10 on the branches maintained beside it, and every
        /// Metalama has it; this is the version named because it is the one on the main line.
        /// </summary>
        private static readonly Version _firstTolerantVersion = new( 6, 9, 3 );

        /// <summary>
        /// If the <paramref name="licenseKeyData"/> contains an obsolete license type, it gets transformed to a respective non-obsolete one.
        /// Otherwise, the same license type is returned.
        /// </summary>
        internal static LicenseType NormalizeLicenseType( this LicenseKeyData licenseKeyData )
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if ( licenseKeyData is { Product: LicenseProduct.PostSharpUltimate1, LicenseType: LicenseType.Professional } )
#pragma warning restore CS0618 // Type or member is obsolete
            {
                return LicenseType.Business;
            }
            else
            {
                return licenseKeyData.LicenseType;
            }
        }

#pragma warning disable CS0612, CS0618 // Type or member is obsolete
        internal static LicenseProduct NormalizeProduct( this LicenseKeyData licenseKeyData )
            => (licenseKeyData.Product, licenseKeyData.LicenseType) switch
            {
                (LicenseProduct.PostSharpUltimate1, LicenseType.Professional) => LicenseProduct.PostSharpFramework,
                (LicenseProduct.PostSharpUltimate1 or LicenseProduct.PostSharpUltimate, LicenseType.Essentials) => LicenseProduct.PostSharpEssentials,
                (LicenseProduct.PostSharpUltimate1, _) => LicenseProduct.PostSharpUltimate,
                _ => licenseKeyData.Product
            };
#pragma warning restore CS0618

        internal static ServicingPhase NormalizeServicingPhase( this LicenseKeyData licenseKeyData, ILicenseProductCatalog catalog )
            => licenseKeyData.Generation.GetValueOrDefault() == LicenseGeneration.None
                ? ServicingPhase.LongTerm
                : licenseKeyData.ServicingPhase ?? catalog.GetDefaultServicingPhase( licenseKeyData.Product );

        internal static string GetDisplayName( this LicenseKeyData licenseKeyData, ILicenseProductCatalog catalog )
            => catalog.GetLicenseDisplayName( NormalizeProduct( licenseKeyData ), licenseKeyData.LicenseType );

        /// <summary>
        /// Gets the minimal version of PostSharp that can read a license key.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The version is derived from what the key contains: its product, its license type and the set of fields it
        /// carries. A reader released before a field existed rejects a key carrying that field, so the presence of a
        /// field dates the key. The rules and their boundaries are those of
        /// <c>docs/license-key-generations.md</c> in BusinessSystems, which were verified against every license key
        /// in production.
        /// </para>
        /// <para>
        /// The <see cref="LicenseKeyData.MinPostSharpVersion"/> field is deliberately not read as the answer, only as
        /// evidence that the key was issued in the era that introduced it. The field answers the opposite question.
        /// It looks forward: it lets the generator of a key say that no version below the one it names can use the
        /// key, so that a version reading it can refuse the key with a message that names the version to upgrade to,
        /// rather than with whatever its validation happens to report. What is computed here looks backward: which of
        /// the versions already released accept a key, which is what decides where the key is stored so that the
        /// versions that would choke on it never read it.
        /// </para>
        /// <para>
        /// Nothing reads the field for its own purpose today, so a key that requires a later version still fails with
        /// a message that does not name it. That message would be worth having, and the field is the means to it; it
        /// is a nice-to-have rather than a gap in what is computed here.
        /// </para>
        /// <para>
        /// A key signed by the Elliptic Curve DSA authority raises the result, because a reader that does not know
        /// that authority reports the signature as invalid whatever the rest of the key says. This mirrors
        /// <see cref="LicenseKeyData.MinMetalamaVersion"/>, which the other family decides on the same ground.
        /// </para>
        /// </remarks>
        internal static Version GetMinPostSharpVersion( this LicenseKeyData licenseKeyData )
        {
            var minVersion = GetMinPostSharpVersionOfContent( licenseKeyData );

            return licenseKeyData.IsSignedByECDsaKey && minVersion < LicenseKeyData.FirstVersionSupportingECDsaSignature
                ? LicenseKeyData.FirstVersionSupportingECDsaSignature
                : minVersion;
        }

        /// <summary>
        /// Gets the oldest version of PostSharp that reads the content of a license key, ignoring its signature. The
        /// rules are checked from the most demanding feature down, so a key gets the version of the newest feature it
        /// carries.
        /// </summary>
        private static Version GetMinPostSharpVersionOfContent( LicenseKeyData licenseKeyData )
        {
#pragma warning disable 618

            // A length-prefixed field (22 to 253, in practice Generation and ServicingPhase) is skipped only by the
            // tolerant readers. The earlier ones treat every field as must-understand and reject the key.
            if ( licenseKeyData.HasLengthPrefixedField )
            {
                return _firstTolerantVersion;
            }

            // The products and the license type introduced by PostSharp 6.6.
            if ( licenseKeyData.Product is LicenseProduct.PostSharpUltimate or LicenseProduct.PostSharpFramework
                     or LicenseProduct.PostSharpCachingLibrary
                 || licenseKeyData.LicenseType == LicenseType.PerUsage )
            {
                return new Version( 6, 6, 0 );
            }

            // The fields and the license type introduced by PostSharp 5.0.22. MinPostSharpVersion counts here as the
            // era that introduced it, not as the version it names.
            if ( licenseKeyData.HasField( LicenseFieldIndex.LicenseServerEligible )
                 || licenseKeyData.HasField( LicenseFieldIndex.MinPostSharpVersion )
                 || licenseKeyData.LicenseType == LicenseType.Enterprise )
            {
                return new Version( 5, 0, 22 );
            }

            if ( licenseKeyData.HasField( LicenseFieldIndex.AllowInheritance ) )
            {
                return new Version( 4, 2, 0 );
            }

            // The legacy encoding of the current products, and the two fields added by PostSharp 3.0. The generator
            // kept issuing keys of this encoding until 2025, so this is the largest class by far.
            if ( licenseKeyData.Product is LicenseProduct.PostSharpUltimate1 or LicenseProduct.PostSharpDiagnosticsLibrary
                     or LicenseProduct.PostSharpModelLibrary or LicenseProduct.PostSharpThreadingLibrary
                 || licenseKeyData.HasField( LicenseFieldIndex.SubscriptionEndDate )
                 || licenseKeyData.HasField( LicenseFieldIndex.Auditable ) )
            {
                return new Version( 3, 0, 0 );
            }

            if ( licenseKeyData.Product == LicenseProduct.PostSharp20 )
            {
                return new Version( 2, 0, 0 );
            }

            // A key that carries nothing later than PostSharp 3.0, including a key of a Metalama product that no
            // version of PostSharp accepts. The product is rejected by the catalog of the family rather than here,
            // so the value only says that nothing in the key requires a later reader.
            return new Version( 3, 0, 0 );
#pragma warning restore 618
        }

        /// <summary>
        /// Converts the data of a license key to its registration properties.
        /// </summary>
        /// <param name="licenseKeyData">The data of the license key.</param>
        /// <param name="catalog">The catalog that names the products.</param>
        /// <param name="licenseString">The license key, when <see cref="LicenseKeyData.LicenseString"/> is <c>null</c>.</param>
        [PublicAPI]
        public static LicenseRegistrationProperties ToLicenseRegistrationProperties(
            this LicenseKeyData licenseKeyData,
            ILicenseProductCatalog catalog,
            string? licenseString = null )
        {
            var description = licenseKeyData.GetDisplayName( catalog );

            bool licenseServerEligible;

            if ( licenseKeyData.LicenseServerEligible.HasValue )
            {
                licenseServerEligible = licenseKeyData.LicenseServerEligible.Value;
            }
#pragma warning disable CS0618 // Type or member is obsolete
            else if ( licenseKeyData.LicenseType == LicenseType.PerUsage )
#pragma warning restore CS0618
            {
                // A per-usage license key is metered per build and cannot be leased. The rule comes from PostSharp,
                // whose IsLicenseServerEligible checks the license type before it falls back to the identifier.
                licenseServerEligible = false;
            }
            else
            {
                const int lastLicenseIdBefore50Rtm = 100802;
                licenseServerEligible = licenseKeyData.LicenseId is > 0 and <= lastLicenseIdBefore50Rtm;
            }

            var auditable = licenseKeyData.LicenseType switch
            {
#pragma warning disable CS0618 // Type or member is obsolete
                LicenseType.Site or LicenseType.Global or LicenseType.Anonymous => false,
#pragma warning restore CS0618                  // Type or member is obsolete
                LicenseType.Evaluation => true, // We want to audit evaluation licenses so we know how people are using the product during evaluation.
                _ => licenseKeyData.Auditable ?? true
            };

            LicenseRegistrationProperties data = new(
                licenseKeyData.LicenseString ?? licenseString ?? throw new ArgumentNullException(
                    nameof(licenseString),
                    "'licenseString' cannot be null if LicenseKeyData.LicenseString is null." ),
                licenseKeyData.LicenseUniqueId,
                licenseKeyData.LicenseGuid != null,
                licenseKeyData.LicenseGuid == null ? licenseKeyData.LicenseId : null,
                licenseKeyData.Licensee,
                description,
                licenseKeyData.NormalizeProduct(),
                licenseKeyData.NormalizeLicenseType(),
                licenseKeyData.ValidFrom,
                licenseKeyData.ValidTo,
                !licenseKeyData.ValidTo.HasValue,
                licenseKeyData.SubscriptionEndDate,
                auditable,
                licenseServerEligible,
                licenseKeyData.GetMinPostSharpVersion(),
                licenseKeyData.Generation.GetValueOrDefault(),
                licenseKeyData.NormalizeServicingPhase( catalog ) ) { MinMetalamaVersion = licenseKeyData.MinMetalamaVersion };

            return data;
        }
    }
}