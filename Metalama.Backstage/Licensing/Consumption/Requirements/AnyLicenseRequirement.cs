// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace Metalama.Backstage.Licensing.Consumption.Requirements;

internal sealed class AnyLicenseRequirement : LicenseRequirement
{
    public AnyLicenseRequirement() : base( "Any", ServicingPhase.Default ) { }

    public override bool IsEligible( LicenseConsumptionContext context ) => true;

    public override string RequiredLicenseDescription => "any license";
}