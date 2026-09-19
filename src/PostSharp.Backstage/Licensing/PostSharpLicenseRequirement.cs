// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Consumption;
using System.Collections.Generic;
using System.Linq;

namespace PostSharp.Backstage;

/// <summary>
/// The requirement of a feature of PostSharp: the one package the feature needs.
/// </summary>
/// <remarks>
/// <para>
/// A Metalama requirement names the products that satisfy it. A PostSharp requirement names a package, and a license
/// satisfies it when the packages it grants contain that one. The two are not interchangeable, because what a
/// PostSharp license grants depends on its license type as well as on its product: PostSharp Ultimate entitles every
/// package, unless the key carries the Community type, in which case it entitles the free edition alone.
/// </para>
/// <para>
/// <see cref="GetEligibleProducts"/> therefore answers a different question from <see cref="IsEligible"/>. It lists
/// the products that a user could buy to obtain this package, which is what a diagnostic tells them. The decision is
/// made by <see cref="IsEligible"/>, which reads the license type as well.
/// </para>
/// </remarks>
[PublicAPI]
public class PostSharpLicenseRequirement : LicenseRequirement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PostSharpLicenseRequirement"/> class.
    /// </summary>
    /// <param name="componentName">The name of the feature, as it appears in a diagnostic.</param>
    /// <param name="requiredPackage">The package the feature needs.</param>
    /// <param name="requiredServicingPhase">The servicing phase the build requires.</param>
    public PostSharpLicenseRequirement(
        string componentName,
        LicensedPackages requiredPackage,
        ServicingPhase requiredServicingPhase = ServicingPhase.Current ) : base( componentName, requiredServicingPhase )
    {
        this.RequiredPackage = requiredPackage;
    }

    /// <summary>
    /// Gets the package that the feature needs.
    /// </summary>
    public LicensedPackages RequiredPackage { get; }

    /// <inheritdoc />
    public override bool IsEligible( LicenseConsumptionContext context )
    {
        if ( !base.IsEligible( context ) )
        {
            return false;
        }

        var grantedPackages = context.License.GetLicensedPackages();

        if ( !grantedPackages.Includes( this.RequiredPackage ) )
        {
            context.Logger.Warning?.Log(
                $"License {context.License.DisplayName} not eligible: it grants {grantedPackages}, and {this.RequiredPackage} is required." );

            return false;
        }

        return true;
    }

    /// <summary>
    /// The products of PostSharp, in the order in which they are presented.
    /// </summary>
    /// <remarks>
    /// These are the products that a consumed license can name, so they are the normalized ones: a free edition key
    /// arrives as <see cref="LicenseProduct.PostSharpEssentials"/> rather than as PostSharp Ultimate with the
    /// Community type, and <c>PostSharpUltimate1</c> never arrives at all. The list is therefore not the catalogue of
    /// a shop, and PostSharp Essentials belongs in it: a diagnostic that left it out would refuse the free edition
    /// even for the one requirement the free edition satisfies.
    /// </remarks>
    private static readonly LicenseProduct[] _products =
    [
        LicenseProduct.PostSharpUltimate,
        LicenseProduct.PostSharpFramework,
        LicenseProduct.PostSharpEssentials,
        LicenseProduct.PostSharpDiagnosticsLibrary,
        LicenseProduct.PostSharpModelLibrary,
        LicenseProduct.PostSharpThreadingLibrary,
        LicenseProduct.PostSharpCachingLibrary
    ];

    /// <inheritdoc />
    /// <remarks>
    /// A product is eligible when a key of it can grant the package, which is both what the base class checks the
    /// license against and what a diagnostic names. The license type is left open here, so PostSharp Ultimate is
    /// listed for every package even though a key of it that carries the Community type grants only the free
    /// edition; <see cref="IsEligible"/> makes that distinction, and such a key is normalized to PostSharp
    /// Essentials before it arrives anyway.
    /// </remarks>
    protected override IReadOnlyList<LicenseProduct> GetEligibleProducts()
        => _products
            .Where( product => PostSharpLicenseExtensions.GetLicensedPackages( product, LicenseType.Business ).Includes( this.RequiredPackage ) )
            .ToList();
}
