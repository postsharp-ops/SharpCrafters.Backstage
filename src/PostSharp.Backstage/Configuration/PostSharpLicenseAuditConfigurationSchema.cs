// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Licensing.Audit;
using System;
using System.Globalization;
using System.Collections.Immutable;

namespace PostSharp.Backstage.Configuration;

/// <summary>
/// Maps the record of the license audits onto the key that PostSharp 2026.0 keeps, so that a license audited by
/// either version is not audited again by the other on the same day.
/// </summary>
/// <remarks>
/// Sharing this record is only meaningful because PostSharp keys it the way that version keys it, which is by the
/// identity of the license: see <see cref="PostSharpLicenseAuditKeyProvider"/>. Keyed by anything else, the two
/// versions would write into one key and neither would read what the other wrote.
/// </remarks>
internal sealed class PostSharpLicenseAuditConfigurationSchema : RegistryConfigurationSchema<LicenseAuditConfiguration>
{
    private const string _licenseAuditKeyName = "LicenseAudit";

    /// <summary>
    /// The value that holds the moment of the last aggregate audit. PostSharp 2026.0 has no such value, because it
    /// sends no aggregate report.
    /// </summary>
    private const string _lastAggregateAuditValueName = "LastAggregateAuditTime";

    public override string KeyPath => PostSharpRegistry.RootKeyPath + "\\" + _licenseAuditKeyName;

    protected override LicenseAuditConfiguration Read( IRegistryKey? key )
    {
        // PostSharp 2026.0 names a value after the identity of the licence, which is its globally unique identifier
        // when it has one and its number otherwise. Both forms are present in a key that has been in use for a while,
        // so both are read, each into the dictionary that holds its form.
        var configuration = new LicenseAuditConfiguration
        {
            LastMatomoAuditTime = key.GetDateTime( _lastAggregateAuditValueName ),
            Version = key.GetInt32( PostSharpRegistry.ConfigurationVersionValueName )
        };

        if ( key != null )
        {
            foreach ( var name in key.GetValueNames() )
            {
                // The values of this version live in the same key, so the one that is not an audit is skipped.
                if ( string.Equals( name, _lastAggregateAuditValueName, StringComparison.OrdinalIgnoreCase )
                     || string.Equals( name, PostSharpRegistry.ConfigurationVersionValueName, StringComparison.OrdinalIgnoreCase ) )
                {
                    continue;
                }

                if ( key.GetDateTime( name ) is { } lastAuditTime )
                {
                    // Every name is read as text, including the ones that are numbers. A licence of this product is
                    // identified by its number when its key carries no globally unique identifier, and that number
                    // identifies a licence and not a report, so it does not belong among the report hashes that the
                    // other record holds. See PostSharpLicenseAuditKeyProvider.
                    configuration = configuration.SetLastAuditTime( LicenseAuditKey.FromText( name ), lastAuditTime );
                }
            }
        }

        return configuration;
    }

    protected override void Write( IRegistryKey key, LicenseAuditConfiguration configuration )
    {
        foreach ( var lastAuditTime in configuration.LastAuditTimesByString ?? ImmutableDictionary<string, DateTime>.Empty )
        {
            key.SetDateTime( lastAuditTime.Key, lastAuditTime.Value );
        }

        // The record of the report hashes is written too, although this product never adds to it: a registry key has
        // one namespace, so an entry that reached it before this product stopped filing numbers there would otherwise
        // be dropped on the next write. It is read back as text, so it moves to the other record and stays there.
        foreach ( var lastAuditTime in configuration.LastAuditTimesByLong )
        {
            key.SetDateTime( lastAuditTime.Key.ToString( CultureInfo.InvariantCulture ), lastAuditTime.Value );
        }

        key.SetDateTime( _lastAggregateAuditValueName, configuration.LastMatomoAuditTime );

        if ( configuration.Version != null )
        {
            key.SetInt32( PostSharpRegistry.ConfigurationVersionValueName, configuration.Version.Value );
        }

        // An entry the object no longer holds is left where it is, unlike the entries of the other dictionaries this
        // product keeps. The record is shared, and an entry this version does not hold may be one the other version
        // has just written; removing it would make that version audit the license again. The entries are small and
        // bounded by the number of licenses a user has registered, so nothing needs to collect them.
    }
}
