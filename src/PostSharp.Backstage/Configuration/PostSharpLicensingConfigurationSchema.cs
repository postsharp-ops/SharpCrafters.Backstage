// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Infrastructure;
using SharpCrafters.Backstage.Licensing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace PostSharp.Backstage.Configuration;

/// <summary>
/// Maps the registered license keys onto the registry keys that PostSharp 2026.0 reads and writes, so that a license
/// registered in either version is seen by the other.
/// </summary>
internal sealed class PostSharpLicensingConfigurationSchema : RegistryConfigurationSchema<LicensingConfiguration>
{
    private readonly IDateTimeProvider _dateTimeProvider;

    public PostSharpLicensingConfigurationSchema( IDateTimeProvider dateTimeProvider )
    {
        this._dateTimeProvider = dateTimeProvider;
    }

    public override string KeyPath => PostSharpRegistry.RootKeyPath;

    protected override LicensingConfiguration Read( IRegistryKey? key )
    {
        var licenses = ImmutableArray<string?>.Empty;
        ImmutableDictionary<string, ImmutableArray<string?>>? licensesByMinimalVersion = null;

        using ( var licenseKeysKey = key?.OpenSubKey( PostSharpRegistry.LicenseKeysKeyName ) )
        {
            if ( licenseKeysKey != null )
            {
                licenses = ReadLicenseStrings( licenseKeysKey );

                var groups = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string?>>();

                foreach ( var subKeyName in licenseKeysKey.GetSubKeyNames() )
                {
                    // A sub-key whose name is not a version is not a group of this product, so it is left alone.
                    if ( !Version.TryParse( subKeyName, out _ ) )
                    {
                        continue;
                    }

                    using var versionKey = licenseKeysKey.OpenSubKey( subKeyName );

                    if ( versionKey == null )
                    {
                        continue;
                    }

                    var versionLicenses = ReadLicenseStrings( versionKey );

                    if ( !versionLicenses.IsEmpty )
                    {
                        groups[subKeyName] = versionLicenses;
                    }
                }

                if ( groups.Count > 0 )
                {
                    licensesByMinimalVersion = groups.ToImmutable();
                }
            }
        }

        return new LicensingConfiguration
        {
            LegacyLicense = key.GetString( PostSharpRegistry.LegacyLicenseValueName ),
            Licenses = licenses,
            LicensesByMinimalVersion = licensesByMinimalVersion,
            LastEvaluationStartDate = key.GetDateTime( PostSharpRegistry.EvaluationValueName ),
            AllowInsecureLicenseServer = key.GetBoolean( PostSharpRegistry.AllowInsecureLicenseServerValueName ),
            CommunityLicenseReason = (CommunityLicenseReason) (key.GetInt32( PostSharpRegistry.CommunityLicenseReasonValueName ) ?? 0),
            Version = key.GetInt32( PostSharpRegistry.ConfigurationVersionValueName )
        };
    }

    /// <summary>
    /// Reads the license strings of a key, in the order of their names where those are decimal indices, which is
    /// how PostSharp 2026.0 names them.
    /// </summary>
    /// <remarks>
    /// Any value name is read, not only an index, because PostSharp has always read them all and a user may have
    /// added one by hand. A blank value is skipped: it is what a lease that has expired leaves behind.
    /// </remarks>
    private static ImmutableArray<string?> ReadLicenseStrings( IRegistryKey key )
        => key.GetValueNames()
            .Select( name => (Name: name, Index: int.TryParse( name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index ) ? index : int.MaxValue) )
            .OrderBy( value => value.Index )
            .ThenBy( value => value.Name, StringComparer.OrdinalIgnoreCase )
            .Select( value => key.GetString( value.Name ) )
            .Where( licenseString => !string.IsNullOrWhiteSpace( licenseString ) )
            .ToImmutableArray();

    protected override void Write( IRegistryKey key, LicensingConfiguration configuration )
    {
        var hasChanged = key.SetString( PostSharpRegistry.LegacyLicenseValueName, configuration.LegacyLicense );
        hasChanged |= key.SetDateTime( PostSharpRegistry.EvaluationValueName, configuration.LastEvaluationStartDate );
        hasChanged |= key.SetBoolean( PostSharpRegistry.AllowInsecureLicenseServerValueName, configuration.AllowInsecureLicenseServer );
        hasChanged |= key.SetInt32( PostSharpRegistry.CommunityLicenseReasonValueName, (int) configuration.CommunityLicenseReason );

        using ( var licenseKeysKey = key.CreateSubKey( PostSharpRegistry.LicenseKeysKeyName ) )
        {
            if ( licenseKeysKey != null )
            {
                hasChanged |= WriteLicenseStrings( licenseKeysKey, configuration.Licenses );

                var groups = configuration.LicensesByMinimalVersion ?? ImmutableDictionary<string, ImmutableArray<string?>>.Empty;

                // The names of the sub-keys that the object holds, which are the names as this schema writes them
                // and not as the object spells them. The two differ, and comparing the versions rather than the
                // names would not settle it either: Version treats an unspecified component as lower than zero, so
                // the group named 2027.0 and the sub-key named 2027.0.0 are two different versions.
                var writtenSubKeyNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

                foreach ( var group in groups )
                {
                    if ( !Version.TryParse( group.Key, out var version ) )
                    {
                        continue;
                    }

                    // PostSharp 2026.0 names a group with three components, and reads no other spelling.
                    var subKeyName = FormatVersion( version );
                    writtenSubKeyNames.Add( subKeyName );

                    using var versionKey = licenseKeysKey.CreateSubKey( subKeyName );

                    if ( versionKey != null )
                    {
                        hasChanged |= WriteLicenseStrings( versionKey, group.Value );
                    }
                }

                // A group that the object no longer holds is emptied rather than left behind, because its license
                // keys would otherwise keep being read by both versions. The sub-key itself is left, because
                // deleting one is not something the other version expects of a writer.
                foreach ( var subKeyName in licenseKeysKey.GetSubKeyNames() )
                {
                    if ( !Version.TryParse( subKeyName, out _ ) || writtenSubKeyNames.Contains( subKeyName ) )
                    {
                        continue;
                    }

                    using var versionKey = licenseKeysKey.OpenSubKey( subKeyName, true );

                    if ( versionKey != null )
                    {
                        hasChanged |= WriteLicenseStrings( versionKey, ImmutableArray<string?>.Empty );
                    }
                }
            }
        }

        if ( configuration.Version != null )
        {
            key.SetInt32( PostSharpRegistry.ConfigurationVersionValueName, configuration.Version.Value );
        }

        if ( hasChanged )
        {
            this.BumpLicenseTimestamp( key );
        }
    }

    /// <summary>
    /// Formats a version the way PostSharp 2026.0 names a group of license keys, which is with exactly three
    /// components.
    /// </summary>
    /// <remarks>
    /// Built component by component rather than with <see cref="Version.ToString(int)"/>, which throws when the
    /// version has fewer components than are asked of it. The versions this product names are written by hand, and
    /// <c>LicensingConstants.MinimalLicenseServerVersion</c> has two.
    /// </remarks>
    private static string FormatVersion( Version version )
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0}.{1}.{2}",
            Math.Max( version.Major, 0 ),
            Math.Max( version.Minor, 0 ),
            Math.Max( version.Build, 0 ) );

    /// <summary>
    /// Makes the values of a key be exactly a given set of license strings, keeping the name of a string that is
    /// already there.
    /// </summary>
    /// <returns><see langword="true"/> if the key was touched.</returns>
    /// <remarks>
    /// The names are preserved rather than rewritten from zero, so that registering a second license key does not
    /// move the first one. PostSharp 2026.0 finds a key by its content and not by its name, but moving a value
    /// rewrites it, and a rewrite is what this design is at pains to avoid.
    /// </remarks>
    private static bool WriteLicenseStrings( IRegistryKey key, ImmutableArray<string?> licenseStrings )
    {
        var wanted = licenseStrings.Where( licenseString => !string.IsNullOrWhiteSpace( licenseString ) ).ToList();
        var stored = new Dictionary<string, string>( StringComparer.Ordinal );

        foreach ( var name in key.GetValueNames() )
        {
            var storedValue = key.GetString( name );

            if ( storedValue != null && !stored.ContainsKey( storedValue ) )
            {
                stored.Add( storedValue, name );
            }
        }

        var hasChanged = false;
        var namesToKeep = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

        foreach ( var licenseString in wanted )
        {
            if ( stored.TryGetValue( licenseString!, out var existingName ) )
            {
                namesToKeep.Add( existingName );

                continue;
            }

            var name = GetFreeValueName( key, namesToKeep );
            key.SetStringValue( name, licenseString! );
            namesToKeep.Add( name );
            hasChanged = true;
        }

        foreach ( var name in key.GetValueNames() )
        {
            if ( !namesToKeep.Contains( name ) )
            {
                key.DeleteValue( name );
                hasChanged = true;
            }
        }

        return hasChanged;
    }

    /// <summary>
    /// Gets the first decimal index that the key does not already use, which is how PostSharp 2026.0 names a value
    /// it adds.
    /// </summary>
    private static string GetFreeValueName( IRegistryKey key, HashSet<string> reservedNames )
    {
        for ( var index = 0; ; index++ )
        {
            var name = index.ToString( CultureInfo.InvariantCulture );

            if ( key.GetValue( name ) == null && !reservedNames.Contains( name ) )
            {
                return name;
            }
        }
    }

    /// <summary>
    /// Writes the timestamp that PostSharp 2026.0 watches, so that a running instance of it learns that the
    /// registered licenses have changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value only ever moves forward, because the watcher compares it with the one it last saw: a clock that
    /// went back, or two changes within the resolution of the clock, would otherwise leave the second change
    /// unnoticed.
    /// </para>
    /// <para>
    /// The comparison is made on the stored numbers rather than on the dates they stand for. The encoding counts
    /// whole milliseconds, so a date is truncated on its way in; comparing the dates would find the stored one
    /// lower than the current one, write the current one, and produce the same number again.
    /// </para>
    /// </remarks>
    private void BumpLicenseTimestamp( IRegistryKey key )
    {
        var timestamp = RegistryValueCodec.DateTimeToQWord( this._dateTimeProvider.UtcNow );

        if ( key.GetValue( PostSharpRegistry.LicenseTimestampValueName ) is long storedTimestamp && storedTimestamp >= timestamp )
        {
            timestamp = storedTimestamp + 1;
        }

        key.SetQWordValue( PostSharpRegistry.LicenseTimestampValueName, timestamp );
    }
}
