// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Audit;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.UserInterface;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;

namespace SharpCrafters.Backstage.Vsx;

/// <summary>
/// Builds a <see cref="BackstageProduct"/> that hosts several products in one process, and registers the services that
/// answer for all of its products.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BackstageProduct"/> is a sealed record, so a composite product is an ordinary instance of it: it carries
/// the values of its host, and a <see cref="BackstageProduct.OverrideServices"/> action that this class implements.
/// </para>
/// <para>
/// The host gives the identity under which the process reports its usage and its exceptions. It keeps the
/// configuration store of the primary member, the first one, whose services are created from the outer service
/// provider. Every other member gets an inner service provider of its own.
/// </para>
/// </remarks>
internal sealed class CompositeBackstageProduct
{
    private CompositeBackstageProduct( ImmutableArray<BackstageProduct> members )
    {
        this.Members = members;
    }

    /// <summary>
    /// Gets the products that the composite product hosts, the primary member first.
    /// </summary>
    public ImmutableArray<BackstageProduct> Members { get; }

    /// <summary>
    /// Creates a composite product.
    /// </summary>
    /// <param name="host">The product whose profile, web links, telemetry endpoints, user interface addresses and
    /// license product catalog the composite product carries. It must keep the configuration store of the primary
    /// member.</param>
    /// <param name="members">The hosted products, the primary member first. Each one keeps its own configuration store.</param>
    public static BackstageProduct Create( BackstageProduct host, params BackstageProduct[] members )
    {
        if ( host == null )
        {
            throw new ArgumentNullException( nameof(host) );
        }

        if ( members == null || members.Length == 0 )
        {
            throw new ArgumentException( "A composite product must host at least one product.", nameof(members) );
        }

        foreach ( var product in members.Prepend( host ) )
        {
            if ( product == null )
            {
                throw new ArgumentNullException( nameof(members) );
            }

            if ( product.OverrideServices != null )
            {
                throw new ArgumentException( "A composite product cannot hold another composite product.", nameof(members) );
            }
        }

        // The services of the primary member are created from the outer provider, so they read the store of the host.
        if ( !string.Equals( host.Profile.DataDirectoryName, members[0].Profile.DataDirectoryName, StringComparison.OrdinalIgnoreCase ) )
        {
            throw new ArgumentException( "The host of a composite product must keep the configuration store of the primary member.", nameof(host) );
        }

        // Two products of the same family would share one configuration store, and the composite services would update
        // it twice for each change.
        if ( members.Select( p => p.Profile.Name ).Distinct( StringComparer.OrdinalIgnoreCase ).Count() != members.Length )
        {
            throw new ArgumentException( "Each product of a composite product must be of a different product family.", nameof(members) );
        }

        var composite = new CompositeBackstageProduct( members.ToImmutableArray() );

        return host with { OverrideServices = composite.OverrideServices };
    }

    private void OverrideServices( ServiceProviderBuilder serviceProviderBuilder, BackstageInitializationOptions options )
    {
        serviceProviderBuilder.AddSingleton( serviceProvider => new CompositeProductMembers( serviceProvider, this.Members, options ) );

        if ( options.AddLicensing )
        {
            serviceProviderBuilder.AddSingleton<ILicenseRegistrationService>(
                serviceProvider => new CompositeLicenseRegistrationService( serviceProvider.GetRequiredBackstageService<CompositeProductMembers>() ) );

            serviceProviderBuilder.AddSingleton<ILicenseConsumptionService>(
                serviceProvider => new CompositeLicenseConsumptionService( serviceProvider.GetRequiredBackstageService<CompositeProductMembers>() ) );

            // The outer provider audits the licenses of the primary member, so the analytics events of its audit are
            // those of the primary member and not of the host. The report itself goes to the upload queue of the host,
            // which is the queue of the primary member because both keep the same store.
            if ( options.ApplicationInfo.IsLicenseAuditEnabled )
            {
                var primary = this.Members[0];

                serviceProviderBuilder.AddSingleton<ILicenseAuditManager>(
                    serviceProvider => new LicenseAuditManager(
                        serviceProvider,
                        new MatomoUploader( serviceProvider, primary.Profile.Name, primary.TelemetryOptions.AnalyticsUri ) ) );
            }
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
