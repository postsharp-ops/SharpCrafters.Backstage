// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using System;

namespace SharpCrafters.Backstage.Tests.Registry;

/// <summary>
/// A configuration object of every kind of member that a schema has to map, so that the rules of the registry-backed
/// manager can be tested without depending on the layout of a particular product.
/// </summary>
[ConfigurationFile( "testRegistry.json" )]
internal sealed record TestRegistryConfiguration : ConfigurationFile
{
    public string? Text { get; init; }

    public DateTime? Date { get; init; }

    /// <summary>
    /// Gets a value that is on unless it was turned off, which several settings of PostSharp 2026.0 are.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    public int Count { get; init; }
}

/// <summary>
/// The key that holds <see cref="TestRegistryConfiguration"/>, and the correspondence between its members and the
/// values of that key.
/// </summary>
internal sealed class TestRegistryConfigurationSchema : IRegistryConfigurationSchema
{
    /// <summary>
    /// The value that carries <see cref="ConfigurationFile.Version"/>. PostSharp 2026.0 has no such value; it is
    /// one of those that this version adds to a key it shares, and that the earlier version ignores.
    /// </summary>
    public const string VersionValueName = "ConfigurationVersion";

    public Type ConfigurationType => typeof(TestRegistryConfiguration);

    public RegistryHiveKind Hive => RegistryHiveKind.CurrentUser;

    public string KeyPath => @"Software\SharpCrafters\Test";

    public ConfigurationFile Read( IRegistryKey? key )
        => new TestRegistryConfiguration
        {
            Text = key.GetString( "Text" ),
            Date = key.GetDateTime( "Date" ),
            IsEnabled = key.GetBoolean( "IsEnabled", true ),
            Count = key.GetInt32( "Count" ) ?? 0,
            Version = key.GetInt32( VersionValueName )
        };

    public void Write( IRegistryKey key, ConfigurationFile value )
    {
        var configuration = (TestRegistryConfiguration) value;

        key.SetString( "Text", configuration.Text );
        key.SetDateTime( "Date", configuration.Date );
        key.SetBoolean( "IsEnabled", configuration.IsEnabled );
        key.SetInt32( "Count", configuration.Count );

        if ( configuration.Version != null )
        {
            key.SetInt32( VersionValueName, configuration.Version.Value );
        }
    }
}
