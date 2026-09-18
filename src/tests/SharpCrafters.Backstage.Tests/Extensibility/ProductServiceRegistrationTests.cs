// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using Microsoft.Extensions.DependencyInjection;
using PostSharp.Backstage;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Testing;
using System;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Extensibility;

/// <summary>
/// Tests the services that a product contributes, and what follows from them.
/// </summary>
/// <remarks>
/// A product says the things only it knows by registering services, and the one that decides the most is
/// <see cref="IRegistryConfigurationSchemaProvider"/>: registering it is what puts the configuration objects a product
/// shares with its earlier versions into the registry instead of into files of its own. A host that got this wrong
/// would read and write a store that the other version never looks at, and the two would silently stop sharing
/// anything — which is the reason the services are declared on the product rather than at each entry point.
/// </remarks>
public sealed class ProductServiceRegistrationTests
{
    private static IServiceProvider BuildServices( SharpCrafters.Backstage.Application.BackstageProduct product )
    {
        var builder = new ServiceCollectionBuilder();

        builder.AddBackstageServices(
            new BackstageInitializationOptions( new TestApplicationInfo( "Test", true, "1.0", DateTime.Today ), product )
            {
                AddLicensing = false, AddSupportServices = false, AddUserInterface = false, AddRssClient = false
            } );

        return builder.ServiceCollection.BuildServiceProvider();
    }

    /// <summary>
    /// A product that registers a schema provider gets it, and it is the one the product registered.
    /// </summary>
    [Fact]
    public void TheServicesOfTheProductAreRegistered()
    {
        var schemaProvider = BuildServices( PostSharpProduct.Instance ).GetBackstageService<IRegistryConfigurationSchemaProvider>();

        Assert.NotNull( schemaProvider );
        Assert.IsType<PostSharp.Backstage.Configuration.PostSharpRegistryConfigurationSchemaProvider>( schemaProvider );
        Assert.NotEmpty( schemaProvider.GetSchemas() );
    }

    /// <summary>
    /// A product that registers none has none, rather than inheriting one from somewhere.
    /// </summary>
    [Fact]
    public void AProductThatRegistersNoSchemaProviderHasNone()
        => Assert.Null( BuildServices( MetalamaProduct.Instance ).GetBackstageService<IRegistryConfigurationSchemaProvider>() );

    /// <summary>
    /// Registering a schema provider is what puts the configuration objects in the registry, and registering none is
    /// what keeps them in files.
    /// </summary>
    /// <remarks>
    /// On a platform without a registry there is nothing to share through, so the file-based manager is what both
    /// products get and nothing of the registry layer is constructed.
    /// </remarks>
    [Fact]
    public void TheSchemaProviderDecidesWhereTheConfigurationsLive()
    {
        var postSharp = BuildServices( PostSharpProduct.Instance ).GetRequiredBackstageService<IConfigurationManager>();
        var metalama = BuildServices( MetalamaProduct.Instance ).GetRequiredBackstageService<IConfigurationManager>();

        Assert.IsType<SharpCrafters.Backstage.Configuration.ConfigurationManager>( metalama );

        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            Assert.IsType<RegistryConfigurationManager>( postSharp );
        }
        else
        {
            Assert.IsType<SharpCrafters.Backstage.Configuration.ConfigurationManager>( postSharp );
        }
    }

    /// <summary>
    /// The registry service itself exists on Windows only, so nothing of that layer is reachable elsewhere.
    /// </summary>
    [Fact]
    public void TheRegistryServiceExistsOnWindowsOnly()
    {
        var registryService = BuildServices( PostSharpProduct.Instance ).GetBackstageService<IRegistryService>();

        Assert.Equal( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ), registryService != null );
    }
}
