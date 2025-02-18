// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

// ReSharper disable StringLiteralTypo

using JetBrains.Annotations;
using Metalama.Backstage.Licensing;
using Metalama.Backstage.Licensing.Licenses;
using Metalama.Backstage.Utilities;
using System;
using System.Collections.Concurrent;

#pragma warning disable SA1203

namespace Metalama.Backstage.Testing;

[PublicAPI]
public sealed class TestLicenseKeyProvider
{
    private readonly ConcurrentDictionary<string, string> _cachedLicenses = new();

    public LicensingAuthority Authority { get; } = LicensingAuthority.GetTestAuthority();

    private string GenerateLicenseKey( int id, Action<LicenseKeyDataBuilder> action )
    {
        var builder = new LicenseKeyDataBuilder { LicenseId = id, SubscriptionEndDate = this.SubscriptionExpirationDate };
        action( builder );

        // Ensure we always return the same license key for the same input because subsequent signing of the same thing
        // do not return the same signature.
        var hash = HashUtilities.HashToString( builder.GetSignedBuffer() );

        return this._cachedLicenses.GetOrAdd( hash, _ => builder.SignAndSerialize( this.Authority ) );
    }

    private string GenerateLicenseKey( int id, LicensedProduct product, LicenseType type = LicenseType.Business )
        => this.GenerateLicenseKey(
            id,
            license =>
            {
                license.Product = product;
                license.LicenseType = type;
                license.Generation = LicenseGeneration.V20251;
            } );

    public string PostSharpEssentials => this.GenerateLicenseKey( 1, LicensedProduct.PostSharpUltimate, LicenseType.Community );

    public string PostSharpFramework => this.GenerateLicenseKey( 2, LicensedProduct.PostSharpFramework );

    public string PostSharpUltimate => this.GenerateLicenseKey( 3, LicensedProduct.PostSharpUltimate );

    public const string PostSharpUltimateOpenSourceRedistributionNamespace = "Oss";

    public string MetalamaProfessionalPersonal => this.GenerateLicenseKey( 4, LicensedProduct.MetalamaProfessional, LicenseType.Personal );

    public string MetalamaProfessionalBusiness => this.GenerateLicenseKey( 5, LicensedProduct.MetalamaProfessional );

    public string MetalamaCommunity => this.GenerateLicenseKey( 6, LicensedProduct.MetalamaCommunity, LicenseType.Community );

    [Obsolete]
    public string MetalamaUltimatePersonal => this.GenerateLicenseKey( 7, LicensedProduct.MetalamaUltimate, LicenseType.Personal );

    [Obsolete]
    public string MetalamaUltimateBusiness => this.GenerateLicenseKey( 8, LicensedProduct.MetalamaUltimate );

    public string MetalamaProfessionalBusinessNotAuditable
        => this.GenerateLicenseKey(
            9,
            key =>
            {
                key.Product = LicensedProduct.MetalamaProfessional;
                key.LicenseType = LicenseType.Business;
                key.Auditable = false;
            } );

    public DateTime SubscriptionExpirationDate { get; } = new( 2050, 1, 1, 0, 0, 0, DateTimeKind.Utc );

    public string GetLicenseKey( string licenseKeyName )
    {
        var propertyInfo = this.GetType().GetProperty( licenseKeyName )
                           ?? throw new ArgumentOutOfRangeException();

        return (string) propertyInfo.GetValue( this, null )!;
    }
}