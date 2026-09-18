// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Application;
using System.Collections.Generic;

// ReSharper disable RedundantLinebreak

namespace SharpCrafters.Backstage.Licensing.Consumption.Requirements;

/// <summary>
/// The requirement of the Visual Studio extension, which is one product serving both product families. It is
/// therefore not a requirement of either family and lives with the product-neutral services, unlike the
/// requirements of the features of a product.
/// </summary>
/// <remarks>
/// The extension is presented to the user under the name of the product they are working with, as
/// <c>InstallVsx.cshtml</c> and the toast notifications do, so the component name is built from the profile rather
/// than naming one family.
/// </remarks>
[PublicAPI]
public class ToolingLicenseRequirement : LicenseRequirement
{
    public ToolingLicenseRequirement( ProductProfile productProfile, ServicingPhase requiredServicingPhase = ServicingPhase.Current ) : base(
        $"Visual Studio Tools for {productProfile.Name}",
        requiredServicingPhase ) { }

    public override bool IsEligible( LicenseConsumptionContext context )
    {
        if ( !base.IsEligible( context ) )
        {
            return false;
        }

        // Check that the product is eligible.
        switch ( context.License.LicenseProduct )
        {
            case LicenseProduct.MetalamaCommunity:
            case LicenseProduct.MetalamaProfessional:
            case LicenseProduct.MetalamaEnterprise:
            case LicenseProduct.PostSharpFramework:
            case LicenseProduct.PostSharpUltimate:

#pragma warning disable CS0618 // Type or member is obsolete
            case LicenseProduct.MetalamaStarter:
            case LicenseProduct.MetalamaUltimate:
#pragma warning restore CS0618 // Type or member is obsolete
                break;

            default:
                context.Logger.Warning?.Log(
                    $"License '{context.License.DisplayName}' not eligible: the product {context.License.LicenseProduct} is not eligible." );

                return false;
        }

        return true;
    }

    protected override IReadOnlyList<LicenseProduct> GetEligibleProducts()
        =>
        [
            LicenseProduct.MetalamaCommunity,
            LicenseProduct.MetalamaProfessional,
            LicenseProduct.MetalamaEnterprise,
            LicenseProduct.PostSharpFramework,
            LicenseProduct.PostSharpUltimate,
#pragma warning disable CS0618 // Type or member is obsolete
            LicenseProduct.MetalamaStarter,
            LicenseProduct.MetalamaUltimate
#pragma warning restore CS0618 // Type or member is obsolete
        ];
}
