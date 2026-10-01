// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using System;

namespace SharpCrafters.Backstage.Telemetry;

/// <summary>
/// An <see cref="IUsageSessionStore"/> that keeps one value per project in a registry key. The value is named after the
/// project and holds the time of the last report, in the encoding of <see cref="RegistryValueConverters.DateTimeToQWord"/>.
/// </summary>
/// <remarks>
/// <para>
/// A claim reads and writes the value of its own project only. It never reads or writes the other values of the key,
/// nor the parent key, which also holds the telemetry configuration.
/// </para>
/// <para>
/// No maintenance pass cleans the registry, so a successful claim deletes the values that have expired. That clean-up
/// runs without the locks of the other projects. If it runs while another process renews an expired value, it can delete
/// the renewed value, and that project is then reported a second time in the same period. A duplicate report is
/// harmless, and excluding it would require a lock shared by every project, which this store exists to avoid.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class RegistryUsageSessionStore : UsageSessionStore
{
    private readonly IRegistryService _registryService;
    private readonly RegistryHiveKind _hive;
    private readonly string _keyPath;
    private readonly string _displayPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistryUsageSessionStore"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="hive">The hive of the key that holds the records.</param>
    /// <param name="keyPath">The path of the key that holds the records. It must hold nothing else.</param>
    public RegistryUsageSessionStore( IServiceProvider serviceProvider, RegistryHiveKind hive, string keyPath ) : base( serviceProvider )
    {
        this._registryService = serviceProvider.GetRequiredBackstageService<IRegistryService>();
        this._hive = hive;
        this._keyPath = keyPath;
        this._displayPath = this._registryService.GetDisplayPath( hive, keyPath );
    }

    /// <inheritdoc />
    /// <remarks>
    /// The registry compares the names of values without case, so the lock name is in upper case for the same reason.
    /// </remarks>
    protected override string GetLockResourceName( string projectKey ) => this._displayPath + "!" + projectKey.ToUpperInvariant();

    /// <inheritdoc />
    protected override DateTime? ReadLastReportTime( string projectKey )
    {
        using var key = this._registryService.OpenKey( this._hive, this._keyPath );

        return key.GetDateTime( projectKey );
    }

    /// <inheritdoc />
    protected override void WriteLastReportTime( string projectKey, DateTime time )
    {
        using var key = this._registryService.CreateKey( this._hive, this._keyPath )
                        ?? throw new InvalidOperationException( $"Cannot open '{this._displayPath}' for writing." );

        key.SetDateTime( projectKey, time );
    }

    /// <inheritdoc />
    protected override void OnClaimed( DateTime now, TimeSpan period )
    {
        using var key = this._registryService.OpenKey( this._hive, this._keyPath, writable: true );

        if ( key == null )
        {
            return;
        }

        foreach ( var name in key.GetValueNames() )
        {
            // A value that cannot be read as a date is deleted as well: this key holds nothing else.
            if ( key.GetDateTime( name ) is not { } lastReported || lastReported.Add( period ) <= now )
            {
                key.DeleteValue( name );
            }
        }
    }
}
