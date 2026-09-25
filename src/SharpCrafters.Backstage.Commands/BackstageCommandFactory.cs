// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Commands.Configuration;
using SharpCrafters.Backstage.Commands.Licensing;
using SharpCrafters.Backstage.Commands.Maintenance;
using SharpCrafters.Backstage.Commands.Misc;
using SharpCrafters.Backstage.Commands.Rss;
using SharpCrafters.Backstage.Commands.Telemetry;
using SharpCrafters.Backstage.Commands.UserInterface;
using Spectre.Console.Cli;
using System.Linq;
using System;

namespace SharpCrafters.Backstage.Commands;

public static class BackstageCommandFactory
{
    public static void ConfigureCommandApp(
        CommandApp app,
        BackstageCommandOptions options,
        Action<IConfigurator>? configureMoreCommands = null,
        Action<string, IConfigurator<CommandSettings>>? configureBranch = null )
    {
        var productName = options.ProductProfile.Name;

        app.Configure(
            appConfig =>
            {
                appConfig.AddBranch(
                    "license",
                    license =>
                    {
                        license.SetDescription( $"Manages license keys and switch between {productName} editions." );

                        license.AddCommand<ListLicensesCommand>( "list" )
                            .WithData( options )
                            .WithDescription( "Lists all registered licenses." );

                        // One command per edition that the product family gives away and that a user registers by
                        // hand, named by the alias the family gave it. The trial is one of them. An edition that
                        // another program registers on the user's behalf declares that it is not offered here, so the
                        // help does not describe something nobody obtains this way; and a family that gives away
                        // nothing would otherwise advertise a command whose only outcome is an error.
                        var editions = options.Product.LicenseProductCatalog.SelfRegisteredEditions
                            .Where( e => e.IsAvailableFromCommandLine )
                            .ToList();

                        // The register command takes only a key or a URL, so its help names the commands that register
                        // an edition without a key, by the aliases of this product family.
                        var registerDescription = "Registers a new license key or license server URL.";

                        if ( editions.Count > 0 )
                        {
                            registerDescription +=
                                $" To register an edition that requires no license key, use {string.Join( " or ", editions.Select( e => $"'license {e.Alias}'" ) )}.";
                        }

                        license.AddCommand<RegisterLicenseCommand>( "register" )
                            .WithData( options )
                            .WithDescription( registerDescription );

                        license.AddCommand<UnregisterCommand>( "unregister" )
                            .WithData( options )
                            .WithDescription( "Unregisters all license keys and license servers." );

                        license.AddCommand<AcquireLeaseCommand>( "acquire-lease" )
                            .WithData( options )
                            .WithDescription( "Acquires a lease from the registered license server and prints the license it leases." );

                        foreach ( var edition in editions )
                        {
                            license.AddCommand<RegisterEditionCommand>( edition.Alias )
                                .WithData( options )
                                .WithDescription( edition.Description );
                        }

                        configureBranch?.Invoke( "license", license );
                    } );

                appConfig.AddBranch(
                    "config",
                    config =>
                    {
                        config.SetDescription( $"Manages {productName} configuration settings." );

                        config.AddCommand<ListConfigurationsCommand>( "list" )
                            .WithData( options )
                            .WithDescription( "Lists all supported JSON configuration files." );

                        config.AddCommand<EditConfigurationCommand>( "edit" )
                            .WithData( options )
                            .WithDescription( "Opens a JSON configuration file in the default text editor." );

                        config.AddCommand<ResetConfigurationCommand>( "reset" )
                            .WithData( options )
                            .WithDescription( "Resets a configuration file to its default values." );

                        config.AddCommand<PrintConfigurationCommand>( "print" )
                            .WithData( options )
                            .WithDescription( "Displays the contents of a configuration file in the console." );

                        config.AddCommand<DumpConfigurationCommand>( "dump" )
                            .WithData( options )
                            .WithDescription( "Writes every configuration, or one of them, to the console as a single JSON document." );

                        config.AddCommand<ValidateConfigurationCommand>( "validate" )
                            .WithData( options )
                            .WithDescription( "Validates a configuration file against its schema." );

                        configureBranch?.Invoke( "config", config );
                    } );

                appConfig.AddCommand<CleanUpCommand>( "cleanup" )
                    .WithData( options )
                    .WithDescription( $"Removes temporary files and cache generated by {productName}." );

                appConfig.AddCommand<ShutdownCommand>( "shutdown" )
                    .WithData( options )
                    .WithDescription(
                        $"Stops the processes that keep {productName} files locked after a build: asks them to exit, waits for them, and reports the ones that remain." );

                appConfig.AddCommand<KillCommand>( "kill" )
                    .WithData( options )
                    .WithDescription( $"Ends the processes that keep {productName} files locked after a build. The same as 'shutdown --force'." );

                appConfig.AddBranch(
                    "telemetry",
                    telemetry =>
                    {
                        telemetry.SetDescription( "Manages telemetry settings and upload queued data." );

                        telemetry.AddCommand<EnableTelemetryCommand>( "enable" )
                            .WithData( options )
                            .WithDescription( "Enables telemetry for a scenario (usage, exception, performance or all)." );

                        telemetry.AddCommand<DisableTelemetryCommand>( "disable" )
                            .WithData( options )
                            .WithDescription( "Disables telemetry for a scenario (usage, exception, performance or all)." );

                        telemetry.AddCommand<ResetTelemetryCommand>( "reset" )
                            .WithData( options )
                            .WithDescription(
                                "Resets telemetry for a scenario (usage, exception, performance or all) to its default state "
                                + "(review-first for exceptions and performance problems)." );

                        telemetry.AddCommand<ResetDeviceIdCommand>( "reset-device-id" )
                            .WithDescription( "Generates a new anonymous device identifier for telemetry." );

                        telemetry.AddCommand<ResetDedupCommand>( "reset-dedup" )
                            .WithData( options )
                            .WithDescription( "Clears the record of already-reported issues so they are captured again (testing aid)." );

                        telemetry.AddCommand<UploadTelemetryCommand>( "upload" )
                            .WithData( options )
                            .WithDescription( "Uploads all queued telemetry data immediately." );

                        telemetry.AddCommand<TelemetryStatusCommand>( "status" )
                            .WithData( options )
                            .WithDescription( "Displays current telemetry configuration and status." );

                        configureBranch?.Invoke( "telemetry", telemetry );
                    } );

                appConfig.AddCommand<DocsCommand>( "docs" ).WithData( options ).WithDescription( $"Opens the {productName} documentation in your browser." );
                appConfig.AddCommand<OpenUICommand>( "ui" ).WithData( options ).WithDescription( "Opens the browser-based configuration interface." );

                appConfig.AddBranch(
                    "news",
                    rss =>
                    {
                        rss.SetDescription( $"Manages toast notifications for new {productName} updates." );

                        rss.AddCommand<DisplayLatestNewsCommand>( "notify" )
                            .WithData( options )
                            .WithDescription( $"Displays a notification with the latest {productName} news." );

                        rss.AddCommand<DisableRssClientCommand>( "disable" )
                            .WithData( options )
                            .WithDescription( $"Disables automatic notifications for new {productName} updates." );

                        rss.AddCommand<EnableRssClientCommand>( "enable" )
                            .WithData( options )
                            .WithDescription( $"Enables automatic notifications for new {productName} updates." );

                        rss.AddCommand<RssStatusCommand>( "status" )
                            .WithData( options )
                            .WithDescription( "Displays the current news notification settings." );
                    } );

                appConfig.AddCommand<VersionCommand>( "version" ).WithData( options ).WithDescription( "Prints version information." );

                configureMoreCommands?.Invoke( appConfig );
            } );
    }
}