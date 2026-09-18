// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using System;
using System.Collections.Immutable;

namespace PostSharp.Backstage.Configuration;

/// <summary>
/// Maps the leases held from license servers onto the cache that PostSharp 2026.0 keeps, so that the two versions of
/// the product on one machine hold one lease between them rather than one each.
/// </summary>
/// <remarks>
/// This is the one shared setting whose sharing the user can be charged for. A license server allocates a seat per
/// lease, and a developer who builds with both versions would otherwise take two.
/// </remarks>
internal sealed class PostSharpLicenseServerConfigurationSchema : IRegistryConfigurationSchema
{
    /// <summary>
    /// The sub-key under which each server has a sub-key of its own, named after its address.
    /// </summary>
    private const string _leasedLicensesKeyName = "LeasedLicenses";

    /// <summary>
    /// The name of the value that holds the lease of a license that no version of the product singles out, which is
    /// the default value of the key.
    /// </summary>
    /// <remarks>
    /// PostSharp 2026.0 uses a value named after a version instead when the leased license requires one. No product
    /// of the PostSharp family requires it, so this version writes and reads the default value only. A lease that
    /// the other version put in a version-specific value is therefore not found here, and one more lease is
    /// acquired; a server allocates a seat per user and machine, so that costs a request rather than a seat.
    /// </remarks>
    private const string _leaseValueName = "";

    public Type ConfigurationType => typeof(LicenseServerConfiguration);

    public RegistryHiveKind Hive => RegistryHiveKind.CurrentUser;

    public string KeyPath => PostSharpRegistry.RootKeyPath + "\\" + _leasedLicensesKeyName;

    public ConfigurationFile Read( IRegistryKey? key )
    {
        var leases = ImmutableDictionary.CreateBuilder<string, LeaseConfiguration>( StringComparer.Ordinal );

        if ( key != null )
        {
            foreach ( var serverKeyName in key.GetSubKeyNames() )
            {
                using var serverKey = key.OpenSubKey( serverKeyName );

                var lease = PostSharpLeaseSerializer.Deserialize( serverKey.GetString( _leaseValueName ) );

                if ( lease != null )
                {
                    // Keyed the way the rest of this product keys a server, so that the spelling the user
                    // registered and the spelling the key is named with reach the same entry.
                    leases[LicenseServerUrl.GetStoreKey( serverKeyName )] = lease;
                }
            }
        }

        return new LicenseServerConfiguration
        {
            Leases = leases.ToImmutable(), Version = key.GetInt32( PostSharpRegistry.ConfigurationVersionValueName )
        };
    }

    public void Write( IRegistryKey key, ConfigurationFile value )
    {
        var configuration = (LicenseServerConfiguration) value;

        foreach ( var lease in configuration.Leases )
        {
            using var serverKey = key.CreateSubKey( lease.Key );

            serverKey?.SetString( _leaseValueName, PostSharpLeaseSerializer.Serialize( lease.Value ) );
        }

        // A lease the object no longer holds is blanked rather than having its key deleted, which is what PostSharp
        // 2026.0 does to a lease that has expired. Deleting the key of a server would also lose the
        // version-specific values that the other version may keep beside the default one.
        foreach ( var serverKeyName in key.GetSubKeyNames() )
        {
            if ( configuration.Leases.ContainsKey( LicenseServerUrl.GetStoreKey( serverKeyName ) ) )
            {
                continue;
            }

            using var serverKey = key.OpenSubKey( serverKeyName, true );

            if ( serverKey != null && serverKey.GetString( _leaseValueName ) != null )
            {
                serverKey.SetStringValue( _leaseValueName, "" );
            }
        }

        if ( configuration.Version != null )
        {
            key.SetInt32( PostSharpRegistry.ConfigurationVersionValueName, configuration.Version.Value );
        }
    }
}
