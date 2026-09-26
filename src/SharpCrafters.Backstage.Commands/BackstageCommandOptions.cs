// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Commands.Configuration;
using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Audit;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.UserInterface.Rss;
using SharpCrafters.Backstage.UserInterface.Toasts;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;

namespace SharpCrafters.Backstage.Commands;

public sealed class BackstageCommandOptions
{
    private readonly AnsiSupport _ansiSupport;
    private readonly Dictionary<string, ConfigurationFileCommandAdapter> _configurationFileCommandAdapters = [];
    private readonly Dictionary<string, ConfigurationFileCommandAdapter> _configurationsToDump = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="BackstageCommandOptions"/> class for a given product.
    /// </summary>
    /// <param name="applicationInfo">The description of the host process.</param>
    /// <param name="product">The product family that hosts the commands.</param>
    /// <param name="addToolsExtractor">
    /// Registers the extractor of the tool applications of the product, which the product ships in a package of its
    /// own, for instance <c>builder => builder.AddTools()</c>. It is <see langword="null"/> when the host has no such
    /// package, and the commands that start a tool application then report that the tool is unavailable.
    /// </param>
    /// <param name="registerServices">
    /// Registers the services that the host of the commands contributes, after the services of Backstage. A host registers
    /// there the <see cref="SharpCrafters.Backstage.Maintenance.IProcessShutdownStrategy"/> implementations of its product,
    /// with <see cref="SharpCrafters.Backstage.Maintenance.ProcessShutdownServiceExtensions.AddProcessShutdownStrategy"/>,
    /// which the <c>shutdown</c> command, also named <c>kill</c>, and the <c>cleanup</c> command run after those of Backstage.
    /// </param>
    public BackstageCommandOptions(
        IApplicationInfo applicationInfo,
        BackstageProduct product,
        Action<ServiceProviderBuilder>? addToolsExtractor = null,
        TextWriter? standardOutput = null,
        TextWriter? errorOutput = null,
        AnsiSupport ansiSupport = AnsiSupport.Detect,
        Action<ServiceProviderBuilder>? registerServices = null ) : this(
        new CommandServiceProvider( applicationInfo, product, addToolsExtractor, registerServices ),
        product,
        standardOutput,
        errorOutput,
        ansiSupport ) { }

    /// <summary>
    /// Gets the product family that hosts the commands.
    /// </summary>
    public BackstageProduct Product { get; }

    /// <summary>
    /// Gets the profile of the product, whose name appears in the descriptions of the commands.
    /// </summary>
    public ProductProfile ProductProfile => this.Product.Profile;

    internal BackstageCommandOptions(
        ICommandServiceProviderProvider serviceProvider,
        BackstageProduct product,
        TextWriter? standardOutput = null,
        TextWriter? errorOutput = null,
        AnsiSupport ansiSupport = AnsiSupport.Detect )
    {
        this._ansiSupport = ansiSupport;
        this.Product = product;
        this.ServiceProvider = serviceProvider;
        this.StandardOutput = standardOutput ?? Console.Out;
        this.ErrorOutput = errorOutput ?? Console.Error;
        this.AddConfigurationFileAdapter<DiagnosticsConfiguration>();
        this.AddConfigurationFileAdapter<ToastNotificationsConfiguration>();
        this.AddConfigurationFileAdapter<RssClientConfiguration>();
        this.AddConfigurationFileAdapter<TelemetryConfiguration>();

        // The licensing configurations are shown but not offered for editing. What they hold is written by the
        // 'license' commands, which keep several stores consistent with each other, so a user who edited them by hand
        // would be editing one half of something. They are still worth showing: on Windows they are the configurations
        // that share a registry key with PostSharp 2026.0, so a dump of them beside an export of that key is what says
        // whether the two versions agree about what is stored there.
        this.AddConfigurationToDump<LicensingConfiguration>();
        this.AddConfigurationToDump<LicenseServerConfiguration>();
        this.AddConfigurationToDump<LicenseAuditConfiguration>();
    }

    internal void ConfigureConsole( AnsiConsoleSettings settings )
    {
        settings.Ansi = this._ansiSupport;
        settings.Interactive = InteractionSupport.No;
    }

    internal ICommandServiceProviderProvider ServiceProvider { get; }

    internal TextWriter StandardOutput { get; }

    internal TextWriter ErrorOutput { get; }

    internal IReadOnlyDictionary<string, ConfigurationFileCommandAdapter> ConfigurationFileCommandAdapters => this._configurationFileCommandAdapters;

    /// <summary>
    /// Gets every configuration that <c>config dump</c> writes, which is the ones offered for editing and the ones
    /// that are only shown.
    /// </summary>
    internal IReadOnlyDictionary<string, ConfigurationFileCommandAdapter> ConfigurationsToDump
    {
        get
        {
            var all = new Dictionary<string, ConfigurationFileCommandAdapter>( this._configurationFileCommandAdapters, StringComparer.OrdinalIgnoreCase );

            foreach ( var adapter in this._configurationsToDump )
            {
                all[adapter.Key] = adapter.Value;
            }

            return all;
        }
    }

    [PublicAPI]
    public void AddConfigurationFileAdapter<T>()
        where T : ConfigurationFile, new()
    {
        var adapter = new ConfigurationFileCommandAdapter<T>();
        this._configurationFileCommandAdapters.Add( adapter.Alias, adapter );
    }

    /// <summary>
    /// Adds a configuration that <c>config dump</c> writes and that the other <c>config</c> commands do not offer.
    /// </summary>
    [PublicAPI]
    public void AddConfigurationToDump<T>()
        where T : ConfigurationFile, new()
    {
        var adapter = new ConfigurationFileCommandAdapter<T>();
        this._configurationsToDump.Add( adapter.Alias, adapter );
    }
}
