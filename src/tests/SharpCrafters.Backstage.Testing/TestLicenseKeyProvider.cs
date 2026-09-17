// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

// ReSharper disable StringLiteralTypo

using JetBrains.Annotations;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Utilities;
using System;
using System.Collections.Concurrent;

#pragma warning disable SA1203

namespace SharpCrafters.Backstage.Testing;

[PublicAPI]
public sealed class TestLicenseKeyProvider
{
    private readonly ConcurrentDictionary<string, string> _cachedLicenses = new();

    /// <summary>
    /// Gets the authority that signs the license keys of the current provider. It is the same authority as the one
    /// that a service provider of the current process uses to verify their signature.
    /// </summary>
    public LicensingAuthority Authority { get; } = TestLicensingAuthorityProvider.DsaTestAuthority;

    public const string NamespaceConstraint = "TestNamespace";

    private string GenerateLicenseKey( int id, Action<LicenseKeyDataBuilder> action, bool sign = true, bool endSubscription = true )
    {
        var builder = new LicenseKeyDataBuilder { LicenseId = id };

        if ( endSubscription )
        {
            builder.SubscriptionEndDate = this.DefaultSubscriptionExpirationDate;
        }

        action( builder );

        // Ensure we always return the same license key for the same input because subsequent signing of the same thing
        // do not return the same signature.
        var hash = HashUtilities.HashToString( builder.GetSignedBuffer() ) + sign;

        return this._cachedLicenses.GetOrAdd(
            hash,
            _ =>
            {
                if ( sign && builder.RequiresSignature() )
                {
                    return builder.SignAndSerialize( this.Authority );
                }
                else
                {
                    return builder.SerializeToLicenseString();
                }
            } );
    }

    private string GenerateLicenseKey(
        int id,
        LicenseProduct product,
        LicenseType type = LicenseType.Business,
        LicenseGeneration generation = LicenseGeneration.Current,
        ServicingPhase servicing = ServicingPhase.Current,
        bool sign = true,
        bool endSubscription = true )
        => this.GenerateLicenseKey(
            id,
            license =>
            {
                license.Product = product;
                license.LicenseType = type;

                if ( generation != LicenseGeneration.None )
                {
                    license.Generation = generation;
                }

                if ( servicing != ServicingPhase.Current )
                {
                    license.ServicingPhase = servicing;
                }
            },
            sign,
            endSubscription );

    public string PostSharpEssentials => this.GenerateLicenseKey( 1, LicenseProduct.PostSharpUltimate, LicenseType.Community, endSubscription: false );

    public string PostSharpFramework => this.GenerateLicenseKey( 2, LicenseProduct.PostSharpFramework );

    public string PostSharpUltimate => this.GenerateLicenseKey( 3, LicenseProduct.PostSharpUltimate );

    public string PostSharpUltimateWithLongTermSupport => this.GenerateLicenseKey( 3, LicenseProduct.PostSharpUltimate, servicing: ServicingPhase.LongTerm );

    public const string PostSharpUltimateOpenSourceRedistributionNamespace = "Oss";

    /// <summary>
    /// Gets a redistribution license key, which licenses the code that a customer ships rather than the machine of a
    /// user and which is therefore never registered in a user profile. It carries a namespace, without which it would
    /// be refused earlier and for a different reason.
    /// </summary>
    public string MetalamaProfessionalRedistribution
        => this.GenerateLicenseKey(
            30,
            license =>
            {
                license.Product = LicenseProduct.MetalamaProfessional;
#pragma warning disable CS0618 // Redistribution license types are obsolete, which is exactly what this key tests.
                license.LicenseType = LicenseType.CommercialRedistribution;
#pragma warning restore CS0618
                license.Generation = LicenseGeneration.Current;
                license.Namespace = NamespaceConstraint;
            } );

    public string MetalamaProfessionalPersonal => this.GenerateLicenseKey( 4, LicenseProduct.MetalamaProfessional, LicenseType.Personal );

    public string MetalamaProfessionalBusiness => this.GenerateLicenseKey( 5, LicenseProduct.MetalamaProfessional );

    public string MetalamaProfessionalBusinessNoGeneration
        => this.GenerateLicenseKey( 5, LicenseProduct.MetalamaProfessional, generation: LicenseGeneration.None );

    public string MetalamaProfessionalBusinessUnsigned => this.GenerateLicenseKey( 5, LicenseProduct.MetalamaProfessional, sign: false );

    public string MetalamaEnterprise => this.GenerateLicenseKey( 5, LicenseProduct.MetalamaEnterprise, servicing: ServicingPhase.LongTerm );

#pragma warning disable CA1822
    public string InvalidLicenseKey => "001-invalid";
#pragma warning restore CA1822

    public string MetalamaCommunity => this.GenerateLicenseKey( 6, LicenseProduct.MetalamaCommunity, LicenseType.Community, endSubscription: false );

    [Obsolete]
    public string MetalamaUltimatePersonal => this.GenerateLicenseKey( 7, LicenseProduct.MetalamaUltimate, LicenseType.Personal, LicenseGeneration.None );

    [Obsolete]
    public string MetalamaUltimateBusiness => this.GenerateLicenseKey( 8, LicenseProduct.MetalamaUltimate, generation: LicenseGeneration.None );

    public string MetalamaProfessionalBusinessNotAuditable
        => this.GenerateLicenseKey(
            9,
            key =>
            {
                key.Product = LicenseProduct.MetalamaProfessional;
                key.LicenseType = LicenseType.Business;
                key.Auditable = false;
                key.Generation = LicenseGeneration.Current;
            } );

    public string NotYetValid
        => this.GenerateLicenseKey(
            10,
            builder =>
            {
                builder.Product = LicenseProduct.MetalamaProfessional;
                builder.LicenseType = LicenseType.Evaluation;
                builder.ValidFrom = new DateTime( 2060, 1, 1 );
                builder.ValidTo = new DateTime( 2061, 1, 1 );
                builder.Generation = LicenseGeneration.Current;
            } );

    public string NoLongerValid
        => this.GenerateLicenseKey(
            11,
            builder =>
            {
                builder.Product = LicenseProduct.MetalamaProfessional;
                builder.LicenseType = LicenseType.Evaluation;
                builder.ValidFrom = new DateTime( 2010, 1, 1 );
                builder.ValidTo = new DateTime( 2011, 1, 1 );
                builder.Generation = LicenseGeneration.Current;
            } );

    public string ExpiredSubscription
        => this.GenerateLicenseKey(
            12,
            builder =>
            {
                builder.Product = LicenseProduct.MetalamaProfessional;
                builder.LicenseType = LicenseType.Business;
                builder.SubscriptionEndDate = this.ExpiredSubscriptionEndDate;
                builder.Generation = LicenseGeneration.Current;
            } );

    public string ExpiredSubscriptionLegacyGeneration
        => this.GenerateLicenseKey(
            13,
            builder =>
            {
                builder.Product = LicenseProduct.MetalamaProfessional;
                builder.LicenseType = LicenseType.Business;
                builder.SubscriptionEndDate = this.ExpiredSubscriptionEndDate;
            } );

    public string MetalamaProfessionalEvaluationNamespaceConstrained
        => this.GenerateLicenseKey(
            9,
            key =>
            {
                key.Product = LicenseProduct.MetalamaProfessional;
                key.LicenseType = LicenseType.Evaluation;
                key.Auditable = false;
                key.Generation = LicenseGeneration.Current;
                key.Namespace = NamespaceConstraint;
            } );

    [Obsolete]
    public string MetalamaStarter => this.GenerateLicenseKey( 12, LicenseProduct.MetalamaStarter, generation: LicenseGeneration.None );

    [Obsolete]
    public string MetalamaFree => this.GenerateLicenseKey( 13, LicenseProduct.MetalamaFree, generation: LicenseGeneration.None );

    // The license keys below exercise the rule that decides whether a license server may lease a key. The rule is the
    // key own LicenseServerEligible field when it has one, then a refusal for a per-usage key, then an identifier in
    // the range issued before 5.0 RTM.

    /// <summary>
    /// Gets a license key that a license server may lease because its own field says so, although its identifier is
    /// above the range that would otherwise make it eligible.
    /// </summary>
    public string MetalamaEnterpriseLicenseServerEligible
        => this.GenerateLicenseKey(
            200000,
            key =>
            {
                key.Product = LicenseProduct.MetalamaEnterprise;
                key.LicenseType = LicenseType.Business;
                key.Generation = LicenseGeneration.Current;
                key.ServicingPhase = ServicingPhase.LongTerm;
                key.LicenseServerEligible = true;
            } );

    /// <summary>
    /// Gets a license key that a license server may not lease because its own field says so, although its identifier
    /// is inside the range that would otherwise make it eligible.
    /// </summary>
    public string MetalamaProfessionalNotLicenseServerEligible
        => this.GenerateLicenseKey(
            14,
            key =>
            {
                key.Product = LicenseProduct.MetalamaProfessional;
                key.LicenseType = LicenseType.Business;
                key.Generation = LicenseGeneration.Current;
                key.LicenseServerEligible = false;
            } );

    /// <summary>
    /// Gets a license key that a license server may lease because its identifier is the last one of the range issued
    /// before 5.0 RTM, and that carries no explicit field.
    /// </summary>
    public string MetalamaProfessionalEligibleByIdUpperBound => this.GenerateLicenseKey( 100802, LicenseProduct.MetalamaProfessional );

    /// <summary>
    /// Gets a license key that a license server may not lease because its identifier is one above that range, and that
    /// carries no explicit field.
    /// </summary>
    public string MetalamaProfessionalIneligibleByIdAboveBound => this.GenerateLicenseKey( 100803, LicenseProduct.MetalamaProfessional );

    public DateTime ExpiredSubscriptionEndDate { get; } = new( 2025, 1, 1, 0, 0, 0, DateTimeKind.Utc );

    public DateTime DefaultSubscriptionExpirationDate { get; } = new( 2050, 1, 1, 0, 0, 0, DateTimeKind.Utc );

    public string GetLicenseKey( string licenseKeyName )
    {
        var propertyInfo = this.GetType().GetProperty( licenseKeyName )
                           ?? throw new ArgumentOutOfRangeException();

        return (string) propertyInfo.GetValue( this, null )!;
    }
}