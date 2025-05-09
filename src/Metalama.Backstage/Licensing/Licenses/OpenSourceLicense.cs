// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage.Licensing.Consumption;
using Metalama.Backstage.Licensing.Registration;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Metalama.Backstage.Licensing.Licenses;

internal sealed class OpenSourceLicense : AuditableLicense
{
    private static readonly LicenseConsumptionProperties _properties = new(
        LicenseProduct.MetalamaOpenSource,
        LicenseType.OpenSource,
        null,
        "Open Source",
        new Version( 0, 0 ),
        null,
        false,
        true,
        null,
        SubscriptionStatus.None,
        0,
        ServicingPhase.Current );

    public OpenSourceLicense( IServiceProvider services ) : base( services ) { }

    public override bool CanBeRegistered( [MaybeNullWhen( true )] out string errorMessage )
    {
        errorMessage = "Open-source licenses cannot be registered.";

        return false;
    }

    public override bool TryGetConsumptionProperties(
        LicenseConsumptionOptions options,
        [MaybeNullWhen( false )] out LicenseConsumptionProperties licenseConsumptionProperties,
        [MaybeNullWhen( true )] out string errorMessage )
    {
        licenseConsumptionProperties = _properties;
        errorMessage = null;

        return true;
    }

    public override bool TryGetRegistrationProperties(
        [MaybeNullWhen( false )] out LicenseRegistrationProperties licenseProperties,
        [MaybeNullWhen( true )] out string errorMessage )
        => throw new NotSupportedException();

    public override int GetHashCode() => 451;

    public override bool Equals( object? obj ) => obj is OpenSourceLicense;
}