// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Licensing.Licenses;
using System;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Licensing.Consumption.Sources;

internal sealed class ExplicitLicenseSource : LicenseSourceBase
{
    private readonly string _licenseString;
    private readonly string _licensePropertyName;

    public override string Description => $"the MSBuild property or environment variable named {this._licensePropertyName}";

    public override LicenseSourceKind Kind { get; }

    protected override IEnumerable<string> GetLicenseStrings( Action<LicensingMessage> reportMessage )
    {
        if ( LicenseServerUrl.IsLicenseServerUrl( this._licenseString ) )
        {
            return [this._licenseString];
        }

        // The license key is validated here, and not left to the factory, so that the message names the source rather
        // than quoting the value: the string is typically supplied by a secret of a continuous integration server, a
        // mistyped value is a likely mistake, and the value itself must not reach a build log. See issue #1859.
        if ( !LicenseKeyData.TryDeserialize( this._licenseString, out _, out var errorMessage ) )
        {
            reportMessage( new LicensingMessage( $"The license key set in {this.Description} is invalid. {errorMessage}" ) );

            return [];
        }

        return [this._licenseString];
    }

    public ExplicitLicenseSource( string licenseString, LicenseSourceKind kind, IServiceProvider services )
        : base( services )
    {
        this._licenseString = licenseString;
        this._licensePropertyName = services.GetRequiredBackstageService<ProductProfile>().LicensePropertyName;
        this.Kind = kind;
    }

    public override LicenseSourcePriority Priority => LicenseSourcePriority.Explicit;
}
