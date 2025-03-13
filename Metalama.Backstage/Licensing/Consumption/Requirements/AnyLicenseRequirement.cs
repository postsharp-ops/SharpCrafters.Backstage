// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.Collections.Generic;

namespace Metalama.Backstage.Licensing.Consumption.Requirements;

internal sealed class AnyLicenseRequirement : LicenseRequirement
{
    public AnyLicenseRequirement() : base( "Any", ServicingPhase.Default ) { }

    public override bool IsEligible( LicenseConsumptionContext context ) => true;

    protected override IReadOnlyList<LicenseProduct> GetEligibleProducts()
        =>
        [
            LicenseProduct.MetalamaCommunity, LicenseProduct.MetalamaEnterprise,
            LicenseProduct.MetalamaProfessional, LicenseProduct.PostSharpEssentials, LicenseProduct.PostSharpFramework, LicenseProduct.PostSharpUltimate,
            LicenseProduct.PostSharpCachingLibrary, LicenseProduct.PostSharpDiagnosticsLibrary, LicenseProduct.PostSharpModelLibrary,
            LicenseProduct.PostSharpThreadingLibrary, LicenseProduct.None,
#pragma warning disable CS0618 // Type or member is obsolete
            LicenseProduct.MetalamaFree, LicenseProduct.MetalamaStarter,
            LicenseProduct.MetalamaUltimate
#pragma warning restore CS0618 // Type or member is obsolete
        ];
}