// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Maintenance;
using System;

namespace SharpCrafters.Backstage.Commands
{
    internal sealed class CommandServiceProvider : ICommandServiceProviderProvider
    {
        private readonly IApplicationInfo _applicationInfo;
        private readonly BackstageProduct _product;
        private readonly Action<ServiceProviderBuilder>? _addToolsExtractor;
        private readonly Action<ServiceProviderBuilder>? _registerServices;

        public CommandServiceProvider(
            IApplicationInfo applicationInfo,
            BackstageProduct product,
            Action<ServiceProviderBuilder>? addToolsExtractor,
            Action<ServiceProviderBuilder>? registerServices )
        {
            this._applicationInfo = applicationInfo;
            this._product = product;
            this._addToolsExtractor = addToolsExtractor;
            this._registerServices = registerServices;
        }

        public IServiceProvider GetServiceProvider( CommandServiceProviderArgs args )
        {
            // ReSharper disable RedundantTypeArgumentsOfMethod

            var serviceCollection = new ServiceCollection();

            var serviceProviderBuilder = new ServiceProviderBuilder(
                ( type, func ) => serviceCollection.Add( new ServiceDescriptor( type, func, ServiceLifetime.Singleton ) ) );

            serviceProviderBuilder.AddService( typeof(ILoggerFactory), new AnsiConsoleLoggerFactory( args.Console, args.Settings ) );
            var loggerFactory = serviceCollection.BuildServiceProvider().GetLoggerFactory();

            var initializationOptions = new BackstageInitializationOptions( this._applicationInfo, this._product )
            {
                AddLicensing = true,
                AddSupportServices = true,
                DiagnosticsOptions = new DiagnosticsInitializationOptions { CreateLoggingFactory = _ => loggerFactory },
                IsDevelopmentEnvironment = args.Settings.IsDevelopmentEnvironment,
                AddUserInterface = args.Settings.AddUserInterface,
                AddToolsExtractor = this._addToolsExtractor
            };

            initializationOptions = args.TransformOptions( initializationOptions );

            serviceProviderBuilder.AddBackstageServices( initializationOptions );

            // The strategies that Backstage contributes for every product, then those of the host. The collection keeps
            // every registration, and the commands run them in this order.
            serviceProviderBuilder
                .AddBuildServerShutdownStrategy()
                .AddBackstageToolsShutdownStrategy();

            this._registerServices?.Invoke( serviceProviderBuilder );

            return serviceCollection.BuildServiceProvider().InitializeBackstageServices();
        }
    }
}