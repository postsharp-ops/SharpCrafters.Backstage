// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace SharpCrafters.Backstage.Licensing.LicenseServer;

/// <summary>
/// Keeps the lease held from each license server between two runs of the product, so that a build that has a valid
/// lease makes no request.
/// </summary>
internal sealed class LicenseLeaseStore : IBackstageService
{
    private readonly IConfigurationManager _configurationManager;
    private readonly ILogger _logger;

    public LicenseLeaseStore( IServiceProvider serviceProvider )
    {
        this._configurationManager = serviceProvider.GetRequiredBackstageService<IConfigurationManager>();
        this._logger = serviceProvider.GetLoggerFactory().Licensing();
    }

    /// <summary>
    /// Gets the lease currently stored for a license server, whether or not it is still valid. The caller decides
    /// what to do with an expired one.
    /// </summary>
    /// <param name="licenseServerUrl">The URL of the license server.</param>
    /// <param name="lease">The stored lease.</param>
    /// <returns><see langword="true"/> if a lease is stored for that server.</returns>
    public bool TryGetLease( string licenseServerUrl, [MaybeNullWhen( false )] out LicenseLease lease )
    {
        var configuration = this._configurationManager.Get<LicenseServerConfiguration>();

        if ( configuration.Leases.TryGetValue( LicenseServerUrl.GetStoreKey( licenseServerUrl ), out var storedLease )
             && !string.IsNullOrEmpty( storedLease.LicenseKey ) )
        {
            lease = storedLease.ToLicenseLease();

            return true;
        }

        lease = null;

        return false;
    }

    /// <summary>
    /// Stores the lease held from a license server, replacing the previous one.
    /// </summary>
    /// <param name="licenseServerUrl">The URL of the license server.</param>
    /// <param name="lease">The lease to store.</param>
    public void SetLease( string licenseServerUrl, LicenseLease lease )
    {
        var key = LicenseServerUrl.GetStoreKey( licenseServerUrl );

        if ( !this.CanWrite( key ) )
        {
            return;
        }

        this.Update(
            key,
            configuration =>
            {
                var newLease = LeaseConfiguration.FromLicenseLease( lease );

                // The members that a later version of the product wrote for this server belong to the server and not
                // to the lease, so they survive a renewal.
                configuration.Leases.TryGetValue( key, out var previousLease );
                newLease.CopyUnknownMembersFrom( previousLease );

                return configuration with { Leases = configuration.Leases.SetItem( key, newLease ) };
            } );
    }

    /// <summary>
    /// Removes the lease held from a license server, which is what an expired lease and an unregistered server both
    /// call for.
    /// </summary>
    /// <param name="licenseServerUrl">The URL of the license server.</param>
    public void RemoveLease( string licenseServerUrl )
    {
        var key = LicenseServerUrl.GetStoreKey( licenseServerUrl );

        if ( !this.CanWrite( key ) )
        {
            return;
        }

        this.Update( key, configuration => configuration.Leases.ContainsKey( key ) ? configuration with { Leases = configuration.Leases.Remove( key ) } : null );
    }

    /// <summary>
    /// Removes every lease, so that unregistering the licenses really stops the product from contacting any server.
    /// </summary>
    public void RemoveAllLeases()
    {
        if ( !this.CanWrite( "all license servers" ) )
        {
            return;
        }

        this.Update(
            "all license servers",
            configuration => configuration.Leases.IsEmpty ? null : configuration with { Leases = _noLeases } );
    }

    /// <summary>
    /// The empty set of leases, with the comparer that the member declares, so that clearing the leases does not
    /// silently replace a case-insensitive dictionary by a case-sensitive one.
    /// </summary>
    private static readonly ImmutableDictionary<string, LeaseConfiguration> _noLeases =
        ImmutableDictionary<string, LeaseConfiguration>.Empty.WithComparers( StringComparer.OrdinalIgnoreCase );

    /// <summary>
    /// Determines whether the store may write, which it may not while the current thread is running the
    /// transformation of another configuration file.
    /// </summary>
    /// <remarks>
    /// A lease is written as a side effect of consuming a license, so the write can be reached from anywhere,
    /// including from a handler that itself runs inside an update. Updating a second file from there raises an
    /// exception, because the thread would then hold two of the locks that protect the files. Skipping the write
    /// costs one request to the license server the next time a lease is needed; raising would fail whatever
    /// operation happened to trigger it.
    /// </remarks>
    private bool CanWrite( string key )
    {
        if ( !ConfigurationUpdateScope.IsUpdating )
        {
            return true;
        }

        this._logger.Warning?.Log( $"Not storing the lease of '{key}': the current thread is updating another configuration file." );

        return false;
    }

    private void Update( string key, Func<LicenseServerConfiguration, LicenseServerConfiguration?> transform )
    {
        var outcome = this._configurationManager.Update( typeof(LicenseServerConfiguration), configuration => transform( (LicenseServerConfiguration) configuration ) );

        // A lease is derived state: failing to store it costs one request later, so no outcome of the update is an
        // error. The message is a trace and not a warning for the outcomes that mean the file already says what we
        // wanted to write.
        switch ( outcome )
        {
            case ConfigurationUpdateOutcome.Updated:
            case ConfigurationUpdateOutcome.NoChange:
            case ConfigurationUpdateOutcome.Declined:
                this._logger.Trace?.Log( $"Storing the lease of '{key}': {outcome}." );

                break;

            default:
                this._logger.Warning?.Log( $"Could not store the lease of '{key}': {outcome}." );

                break;
        }
    }
}
