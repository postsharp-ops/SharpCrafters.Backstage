// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Telemetry;
using System;
using System.Collections.Immutable;

namespace PostSharp.Backstage.Configuration;

/// <summary>
/// Maps the telemetry settings onto the Feedback key that PostSharp 2026.0 reads and writes, so that the choice a
/// user makes in either version holds for both.
/// </summary>
/// <remarks>
/// The three consents are what the Privacy panel of PostSharp 2026.0 writes, and they are the reason this object is
/// shared at all: a user who has said no once must not be asked again by the other version. The rest of the object
/// is bookkeeping that 2026.0 does not have, and it goes into the same key under names of this version, which 2026.0
/// ignores.
/// </remarks>
internal sealed class PostSharpTelemetryConfigurationSchema : IRegistryConfigurationSchema
{
    /// <summary>
    /// The sub-key that holds the decision taken for each issue, keyed by its hash.
    /// </summary>
    /// <remarks>
    /// This is not PostSharp 2026.0's <c>ReportedIssues</c> key, although the two record the same kind of thing. The
    /// hash identifies a stack trace, and the two versions do not produce the same stack traces, so an entry of one
    /// would never match an issue of the other. Sharing the key would mix two sets of hashes that cannot be told
    /// apart, for no benefit.
    /// </remarks>
    private const string _issuesKeyName = "Issues";

    private const string _issuePromptsKeyName = "IssuePrompts";
    private const string _sessionsKeyName = "Sessions";

    public Type ConfigurationType => typeof(TelemetryConfiguration);

    public RegistryHiveKind Hive => RegistryHiveKind.CurrentUser;

    public string KeyPath => PostSharpRegistry.FeedbackKeyPath;

    public ConfigurationFile Read( IRegistryKey? key )
        => new TelemetryConfiguration
        {
            // The two enumerations agree value by value: what PostSharp 2026.0 calls Ask, Yes and No are 0, 1 and 2,
            // and so are Default, Yes and No here.
            UsageConsent = (TelemetryConsent) (key.GetInt32( PostSharpRegistry.UsageReportingActionValueName ) ?? 0),
            ExceptionConsent = (TelemetryConsent) (key.GetInt32( PostSharpRegistry.ExceptionReportingActionValueName ) ?? 0),
            PerformanceProblemConsent = (TelemetryConsent) (key.GetInt32( PostSharpRegistry.PerformanceReportingActionValueName ) ?? 0),
            DeviceId = ReadGuid( key, PostSharpRegistry.DeviceIdValueName ),
            LastSaltChangeTime = key.GetDateTime( PostSharpRegistry.DeviceIdTimestampValueName ),
            LastUploadTime = key.GetDateTime( PostSharpRegistry.LastUploadTimeValueName ),
            MatomoSalt = key.GetInt64( "MatomoSalt" ),
            UsageTrackingSalt = key.GetInt64( "UsageTrackingSalt" ),
            ExceptionReportingSalt = key.GetInt64( "ExceptionReportingSalt" ),
            LicenseAuditSalt = key.GetInt64( "LicenseAuditSalt" ),
            LastMatomoPostTime = key.GetDateTime( "LastMatomoPostTime" ),
            RetentionPeriodInDays = key.GetInt32( "RetentionPeriodInDays" ),
            Issues = ReadDictionary( key, _issuesKeyName, value => (ReportingStatus) (value as int? ?? 0) ),
            IssuePrompts = ReadDictionary( key, _issuePromptsKeyName, RegistryValueCodec.QWordToDateTime )
                .RemoveNullValues(),
            Sessions = ReadDictionary( key, _sessionsKeyName, RegistryValueCodec.QWordToDateTime ).RemoveNullValues(),
            Version = key.GetInt32( PostSharpRegistry.ConfigurationVersionValueName )
        };

    /// <summary>
    /// Reads a value that holds a GUID in its textual form, which is how PostSharp 2026.0 stores the device
    /// identifier.
    /// </summary>
    private static Guid? ReadGuid( IRegistryKey? key, string name )
        => Guid.TryParse( key.GetString( name ), out var value ) ? value : null;

    /// <summary>
    /// Reads a sub-key as a dictionary, one entry per value.
    /// </summary>
    /// <remarks>
    /// The comparison is case-insensitive, as it is in the file-based store: the names are hashes and session
    /// identifiers, which are written in one case and read in another often enough to matter, and the registry
    /// compares names that way in any case.
    /// </remarks>
    private static ImmutableDictionary<string, T> ReadDictionary<T>( IRegistryKey? key, string subKeyName, Func<object?, T> convert )
    {
        using var subKey = key?.OpenSubKey( subKeyName );

        if ( subKey == null )
        {
            return ImmutableDictionary<string, T>.Empty.WithComparers( StringComparer.OrdinalIgnoreCase );
        }

        var builder = ImmutableDictionary.CreateBuilder<string, T>( StringComparer.OrdinalIgnoreCase );

        foreach ( var name in subKey.GetValueNames() )
        {
            builder[name] = convert( subKey.GetValue( name ) );
        }

        return builder.ToImmutable();
    }

    public void Write( IRegistryKey key, ConfigurationFile value )
    {
        var configuration = (TelemetryConfiguration) value;

        key.SetInt32( PostSharpRegistry.UsageReportingActionValueName, (int) configuration.UsageConsent );
        key.SetInt32( PostSharpRegistry.ExceptionReportingActionValueName, (int) configuration.ExceptionConsent );
        key.SetInt32( PostSharpRegistry.PerformanceReportingActionValueName, (int) configuration.PerformanceProblemConsent );
        key.SetString( PostSharpRegistry.DeviceIdValueName, configuration.DeviceId?.ToString() );
        key.SetDateTime( PostSharpRegistry.DeviceIdTimestampValueName, configuration.LastSaltChangeTime );
        key.SetDateTime( PostSharpRegistry.LastUploadTimeValueName, configuration.LastUploadTime );
        key.SetInt64( "MatomoSalt", configuration.MatomoSalt );
        key.SetInt64( "UsageTrackingSalt", configuration.UsageTrackingSalt );
        key.SetInt64( "ExceptionReportingSalt", configuration.ExceptionReportingSalt );
        key.SetInt64( "LicenseAuditSalt", configuration.LicenseAuditSalt );
        key.SetDateTime( "LastMatomoPostTime", configuration.LastMatomoPostTime );
        key.SetInt32( "RetentionPeriodInDays", configuration.RetentionPeriodInDays );

        WriteDictionary( key, _issuesKeyName, configuration.Issues, ( subKey, name, status ) => subKey.SetInt32( name, (int) status ) );
        WriteDictionary( key, _issuePromptsKeyName, configuration.IssuePrompts, ( subKey, name, date ) => subKey.SetDateTime( name, date ) );
        WriteDictionary( key, _sessionsKeyName, configuration.Sessions, ( subKey, name, date ) => subKey.SetDateTime( name, date ) );

        if ( configuration.Version != null )
        {
            key.SetInt32( PostSharpRegistry.ConfigurationVersionValueName, configuration.Version.Value );
        }
    }

    /// <summary>
    /// Makes the values of a sub-key be exactly the entries of a dictionary.
    /// </summary>
    /// <remarks>
    /// Unlike the values of the key itself, which this version shares with the other one, the whole sub-key belongs
    /// to this version, so an entry it no longer holds is removed. These dictionaries are pruned as they are
    /// written, and a value left behind would grow the key without bound.
    /// </remarks>
    private static void WriteDictionary<T>(
        IRegistryKey key,
        string subKeyName,
        ImmutableDictionary<string, T> entries,
        Action<IRegistryKey, string, T> writeEntry )
    {
        using var subKey = key.CreateSubKey( subKeyName );

        if ( subKey == null )
        {
            return;
        }

        foreach ( var entry in entries )
        {
            writeEntry( subKey, entry.Key, entry.Value );
        }

        foreach ( var name in subKey.GetValueNames() )
        {
            if ( !entries.ContainsKey( name ) )
            {
                subKey.DeleteValue( name );
            }
        }
    }
}

/// <summary>
/// Removes the entries whose value could not be read, so that a value the registry holds in a form this version does
/// not understand is absent rather than being a default that would look like a real one.
/// </summary>
internal static class NullableDictionaryExtensions
{
    public static ImmutableDictionary<string, T> RemoveNullValues<T>( this ImmutableDictionary<string, T?> dictionary )
        where T : struct
    {
        var builder = ImmutableDictionary.CreateBuilder<string, T>( StringComparer.OrdinalIgnoreCase );

        foreach ( var entry in dictionary )
        {
            if ( entry.Value != null )
            {
                builder[entry.Key] = entry.Value.Value;
            }
        }

        return builder.ToImmutable();
    }
}
