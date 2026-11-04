// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.UserInterface;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.Application.Composite;

/// <summary>
/// Builds the <see cref="BackstageProduct"/> that <see cref="BackstageProduct.CreateComposite"/> returns, and registers
/// the services that answer for all of its products.
/// </summary>
/// <remarks>
/// <see cref="BackstageProduct"/> is a sealed record, so a composite product is an ordinary instance of it: it carries
/// the values of the primary product, its license product catalog included, and a
/// <see cref="BackstageProduct.OverrideServices"/> action that this class implements.
/// </remarks>
internal sealed class CompositeBackstageProduct
{
    private CompositeBackstageProduct( ImmutableArray<BackstageProduct> products )
    {
        this.Products = products;
    }

    /// <summary>
    /// Gets the products, the primary product first.
    /// </summary>
    public ImmutableArray<BackstageProduct> Products { get; }

    public static BackstageProduct Create( BackstageProduct primary, BackstageProduct[] others )
    {
        if ( primary == null )
        {
            throw new ArgumentNullException( nameof(primary) );
        }

        if ( others == null )
        {
            throw new ArgumentNullException( nameof(others) );
        }

        var products = ImmutableArray.Create( primary ).AddRange( others );

        foreach ( var product in products )
        {
            if ( product == null )
            {
                throw new ArgumentNullException( nameof(others) );
            }

            if ( product.OverrideServices != null )
            {
                throw new ArgumentException( "A composite product cannot hold another composite product.", nameof(others) );
            }
        }

        // Two products of the same family would share one configuration store, and the composite services would update
        // it twice for each change.
        if ( products.Select( p => p.Profile.Name ).Distinct( StringComparer.OrdinalIgnoreCase ).Count() != products.Length )
        {
            throw new ArgumentException( "Each product of a composite product must be of a different product family.", nameof(others) );
        }

        var composite = new CompositeBackstageProduct( products );

        return primary with { OverrideServices = composite.OverrideServices };
    }

    private void OverrideServices( ServiceProviderBuilder serviceProviderBuilder, BackstageInitializationOptions options )
    {
        serviceProviderBuilder.AddSingleton( serviceProvider => new CompositeProductMembers( serviceProvider, this.Products, options ) );

        if ( options.AddLicensing )
        {
            serviceProviderBuilder.AddSingleton<ILicenseRegistrationService>(
                serviceProvider => new CompositeLicenseRegistrationService( serviceProvider.GetRequiredBackstageService<CompositeProductMembers>() ) );

            serviceProviderBuilder.AddSingleton<ILicenseConsumptionService>(
                serviceProvider => new CompositeLicenseConsumptionService( serviceProvider.GetRequiredBackstageService<CompositeProductMembers>() ) );
        }

        if ( options.AddSupportServices )
        {
            serviceProviderBuilder.AddSingleton<ITelemetryConfigurationService>(
                serviceProvider => new CompositeTelemetryConfigurationService( serviceProvider.GetRequiredBackstageService<CompositeProductMembers>() ) );
        }

        // The status is product-neutral, but earlier versions of each product read it from the configuration store of
        // that product, so the service writes it to the store of every product. The standard service is registered
        // with the user interface, and on Windows only.
        if ( options.AddUserInterface && RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            serviceProviderBuilder.AddSingleton<IIdeExtensionStatusService>(
                serviceProvider =>
                {
                    var members = serviceProvider.GetRequiredBackstageService<CompositeProductMembers>();

                    return new IdeExtensionStatusService(
                        serviceProvider,
                        () => members.Members.Select( m => m.ServiceProvider.GetRequiredBackstageService<IConfigurationManager>() ) );
                } );
        }
    }
}
