// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.Collections.Generic;

namespace Metalama.Backstage.Licensing.Consumption.Requirements;

internal sealed class NoneLicenseRequirement : LicenseRequirement
{
    public NoneLicenseRequirement() : base( "None", ServicingPhase.Default ) { }

    public override bool IsEligible( LicenseConsumptionContext context ) => true;

    protected override IReadOnlyList<LicenseProduct> GetEligibleProducts() => [];
}