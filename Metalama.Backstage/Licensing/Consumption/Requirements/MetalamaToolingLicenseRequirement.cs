// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace Metalama.Backstage.Licensing.Consumption.Requirements;

public class MetalamaToolingLicenseRequirement : LicenseRequirement
{
    public MetalamaToolingLicenseRequirement( ServicingPhase requiredServicingPhase = ServicingPhase.Default ) : base(
        "Visual Studio Tools for Metalama",
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

    public override string RequiredLicenseDescription
        => this.ServicingPhase switch
        {
            ServicingPhase.Default =>
                "a Metalama Community, Metalama Professional, Metalama Enterprise, PostSharp Framework or PostSharp Ultimate license",
            ServicingPhase.Extended =>
                "a Metalama Professional, Metalama Enterprise, PostSharp Framework or PostSharp Ultimate license",
            ServicingPhase.LongTerm => "a Metalama Enterprise license or a PostSharp Framework or PostSharp Ultimate license with long-term support",
            _ => throw new ArgumentOutOfRangeException()
        };
}