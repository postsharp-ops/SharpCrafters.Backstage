// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Consumption;

namespace PostSharp.Backstage.Licensing;

/// <summary>
/// Computes what a PostSharp license grants.
/// </summary>
[PublicAPI]
public static class PostSharpLicenseExtensions
{
    /// <summary>
    /// Gets the packages that a license grants.
    /// </summary>
    public static LicensedPackages GetLicensedPackages( this LicenseConsumptionProperties license )
        => GetLicensedPackages( license.LicenseProduct, license.LicenseType );

    /// <summary>
    /// Gets the packages that a license of a given product and type grants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The license type matters in three cases only, and in each of them it decides rather than refines. PostSharp
    /// Ultimate with the Community type is the free edition and grants what the free edition grants, not what
    /// Ultimate grants. An unattended build is entitled to everything but Logging, which is licensed per production
    /// server and not per build. A build of unmodified sources is entitled to everything. Everywhere else the
    /// product alone decides, so a Site, Academic or Evaluation key of one product grants exactly what a
    /// per-developer key of that product grants.
    /// </para>
    /// <para>
    /// A product this version does not know grants the free edition, which is what PostSharp 2026.0 falls back to.
    /// It is never nothing: a key that parses and validates always entitles the free edition.
    /// </para>
    /// </remarks>
    public static LicensedPackages GetLicensedPackages( LicenseProduct product, LicenseType licenseType )
    {
#pragma warning disable CS0618 // Type or member is obsolete: the mapping must name the products that are no longer offered.
        switch ( licenseType )
        {
            case LicenseType.Unattended:
                return LicensedPackageSets.Unattended;

            case LicenseType.Unmodified:
                return LicensedPackageSets.Ultimate;
        }

        return product switch
        {
            LicenseProduct.PostSharpFramework => LicensedPackageSets.Framework,

            // The free edition is a PostSharp Ultimate key carrying the Community type, which is how PostSharp 2026.0
            // writes the key it generates for PostSharp Essentials. LicenseKeyDataExtensions.NormalizeProduct turns
            // that pair into PostSharpEssentials, so a consumed license arrives here already named; the pair is
            // matched as well, for a caller that asks about a key it has not normalized.
            LicenseProduct.PostSharpEssentials => LicensedPackageSets.Essentials,
            LicenseProduct.PostSharpUltimate => licenseType == LicenseType.Community
                ? LicensedPackageSets.Essentials
                : LicensedPackageSets.Ultimate,
            LicenseProduct.PostSharpModelLibrary => LicensedPackageSets.Mvvm,
            LicenseProduct.PostSharpThreadingLibrary => LicensedPackageSets.Threading,
            LicenseProduct.PostSharpDiagnosticsLibrary => LicensedPackageSets.Logging,
            LicenseProduct.PostSharpCachingLibrary => LicensedPackageSets.Caching,
            _ => LicensedPackageSets.Essentials
        };
#pragma warning restore CS0618
    }
}
