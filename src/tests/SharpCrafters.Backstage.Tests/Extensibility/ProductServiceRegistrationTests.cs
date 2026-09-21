// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using Microsoft.Extensions.DependencyInjection;
using PostSharp.Backstage;
using PostSharp.Backstage.Licensing;
using PostSharp.Backstage.PostSharp;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing.Audit;
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
    private static IServiceProvider BuildServices(
        SharpCrafters.Backstage.Application.BackstageProduct product,
        IEnvironmentVariableProvider? environmentVariableProvider = null )
    {
        var builder = new ServiceCollectionBuilder();

        builder.AddBackstageServices(
            new BackstageInitializationOptions( new TestApplicationInfo( "Test", true, "1.0", DateTime.Today ), product )
            {
                AddLicensing = false, AddSupportServices = false, AddUserInterface = false, AddRssClient = false
            } );

        if ( environmentVariableProvider != null )
        {
            // Registered after the services of the product, because the later registration is the one that is resolved.
            builder.AddSingleton( environmentVariableProvider );
        }

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

    /// <summary>
    /// A product that says nothing about how an audit is identified gets the default, which throttles by the content
    /// of the report.
    /// </summary>
    [Fact]
    public void TheDefaultAuditKeyProviderThrottlesByTheReport()
        => Assert.IsType<ReportContentLicenseAuditKeyProvider>(
            BuildServices( MetalamaProduct.Instance ).GetRequiredBackstageService<ILicenseAuditKeyProvider>() );

    /// <summary>
    /// A product that says something gets what it said. PostSharp throttles by the identity of the licence, which is
    /// what lets the record be shared with PostSharp 2026.0.
    /// </summary>
    /// <remarks>
    /// This is the case that a default registered after the services of the product would break: the product would
    /// have said something and been overruled, and the two versions would each audit what the other had just audited.
    /// </remarks>
    [Fact]
    public void AProductCanReplaceTheDefaultAuditKeyProvider()
        => Assert.IsType<PostSharpLicenseAuditKeyProvider>(
            BuildServices( PostSharpProduct.Instance ).GetRequiredBackstageService<ILicenseAuditKeyProvider>() );

    /// <summary>
    /// An environment that forbids the registry gives the file-based manager, although the product registered a schema
    /// provider and the platform is Windows.
    /// </summary>
    /// <remarks>
    /// This is for an account that has no access to the registry. Without it the first read of a configuration object
    /// throws, and a build that would otherwise have succeeded fails over a setting.
    /// </remarks>
    [Fact]
    public void AnEnvironmentThatForbidsTheRegistryGivesTheFileManager()
    {
        var environment = new TestEnvironmentVariableProvider();

        environment.Environment[
                PostSharpProduct.Profile.GetEnvironmentVariableName( RegisterConfigurationServices.RegistryAccessDisabledVariableName )]
            = "1";

        var configurationManager = BuildServices( PostSharpProduct.Instance, environment ).GetRequiredBackstageService<IConfigurationManager>();

        Assert.IsType<SharpCrafters.Backstage.Configuration.ConfigurationManager>( configurationManager );
    }

    /// <summary>
    /// The variable is named after the product, so the one of another product does not forbid the registry here. The
    /// two products keep separate settings and one of them being forbidden says nothing about the other.
    /// </summary>
    [Fact]
    public void TheVariableOfAnotherProductDoesNotForbidTheRegistry()
    {
        var environment = new TestEnvironmentVariableProvider();

        environment.Environment[
                MetalamaProduct.Profile.GetEnvironmentVariableName( RegisterConfigurationServices.RegistryAccessDisabledVariableName )]
            = "1";

        var configurationManager = BuildServices( PostSharpProduct.Instance, environment ).GetRequiredBackstageService<IConfigurationManager>();

        if ( RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) )
        {
            Assert.IsType<RegistryConfigurationManager>( configurationManager );
        }
        else
        {
            Assert.IsType<SharpCrafters.Backstage.Configuration.ConfigurationManager>( configurationManager );
        }
    }
}
