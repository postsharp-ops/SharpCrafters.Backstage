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

    public override string Description { get; }

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
            reportMessage(
                new LicensingMessage(
                    $"The license key set in {this.Description} is invalid. {errorMessage}",
                    LicensingMessageKind.InvalidLicenseKey ) );

            return [];
        }

        return [this._licenseString];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExplicitLicenseSource"/> class that describes itself as the license
    /// property of the product, which is where an application that has one license property reads its license from.
    /// </summary>
    public ExplicitLicenseSource( string licenseString, LicenseSourceKind kind, IServiceProvider services )
        : this(
            licenseString,
            $"the MSBuild property or environment variable named {services.GetRequiredBackstageService<ProductProfile>().LicensePropertyName}",
            kind,
            services ) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExplicitLicenseSource"/> class with the description that the
    /// application supplied, which is what an application that reads its licenses from several places gives for each
    /// of them.
    /// </summary>
    public ExplicitLicenseSource( string licenseString, string description, LicenseSourceKind kind, IServiceProvider services )
        : base( services )
    {
        this._licenseString = licenseString;
        this.Description = description;
        this.Kind = kind;
    }

    public override LicenseSourcePriority Priority => LicenseSourcePriority.Explicit;
}
