// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Diagnostics;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Threading;
using System;

namespace SharpCrafters.Backstage.Telemetry;

/// <summary>
/// The algorithm shared by the implementations of <see cref="IUsageSessionStore"/>, which differ only by where a record
/// is kept.
/// </summary>
/// <remarks>
/// <para>
/// A claim reads the record without a lock first. Once a project has been reported in the current period, which is the
/// case of almost every call, that read is the whole cost of the claim.
/// </para>
/// <para>
/// Otherwise the claim acquires a lock named after the record, reads the record again and writes it. The lock is
/// acquired without waiting: a process that holds it is claiming the same project, and the record it is about to write
/// makes the claim of this process fail anyway. Waiting would only delay the build.
/// </para>
/// </remarks>
public abstract class UsageSessionStore : IUsageSessionStore
{
    private readonly INamedLockService _lockService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsageSessionStore"/> class.
    /// </summary>
    protected UsageSessionStore( IServiceProvider serviceProvider )
    {
        this._lockService = serviceProvider.GetRequiredBackstageService<INamedLockService>();
        this.Logger = serviceProvider.GetLoggerFactory().Telemetry();
    }

    /// <summary>
    /// Gets the logger.
    /// </summary>
    protected ILogger Logger { get; }

    /// <inheritdoc />
    public bool TryClaim( string projectKey, DateTime now, TimeSpan period )
    {
        try
        {
            if ( this.IsRecent( projectKey, now, period ) )
            {
                return false;
            }

            using var releaser = this._lockService.TryWithGlobalLock( this.GetLockResourceName( projectKey ), TimeSpan.Zero );

            if ( releaser == null )
            {
                this.Logger.Trace?.Log( $"The session of project '{projectKey}' is being recorded by another process." );

                return false;
            }

            if ( this.IsRecent( projectKey, now, period ) )
            {
                return false;
            }

            this.WriteLastReportTime( projectKey, now );
        }
        catch ( Exception e )
        {
            this.Logger.LogException( e, $"Cannot record the session of project '{projectKey}'" );

            return false;
        }

        // The record is written, so the claim succeeded whatever happens to the clean-up of the other records.
        try
        {
            this.OnClaimed( projectKey, now, period );
        }
        catch ( Exception e )
        {
            this.Logger.LogException( e, "Cannot delete the expired session records" );
        }

        return true;
    }

    /// <summary>
    /// Runs an action while holding the lock of the record of a project, without waiting for it.
    /// </summary>
    /// <returns><see langword="true"/> if the lock was acquired and the action ran.</returns>
    protected bool TryWithRecordLock( string projectKey, Action action )
    {
        using var releaser = this._lockService.TryWithGlobalLock( this.GetLockResourceName( projectKey ), TimeSpan.Zero );

        if ( releaser == null )
        {
            return false;
        }

        action();

        return true;
    }

    private bool IsRecent( string projectKey, DateTime now, TimeSpan period )
    {
        if ( this.ReadLastReportTime( projectKey ) is { } lastReported && lastReported.Add( period ) > now )
        {
            this.Logger.Trace?.Log( $"The session of project '{projectKey}' was already reported on {lastReported:O}." );

            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the name of the resource protected by the lock of a record. Two keys that differ only by case must give the
    /// same name.
    /// </summary>
    protected abstract string GetLockResourceName( string projectKey );

    /// <summary>
    /// Reads the time at which the usage of a project was last reported, or <see langword="null"/> when there is no
    /// valid record. This method is called without the lock and must tolerate a concurrent writer.
    /// </summary>
    protected abstract DateTime? ReadLastReportTime( string projectKey );

    /// <summary>
    /// Writes the time at which the usage of a project was reported. It is called while the lock of the record is held.
    /// </summary>
    protected abstract void WriteLastReportTime( string projectKey, DateTime time );

    /// <summary>
    /// Called after a successful claim, so that a store that is not cleaned up by another mechanism can remove the
    /// records that have expired. The lock of <paramref name="claimedProjectKey"/> is no longer held. An exception
    /// thrown here is logged and does not change the result of the claim.
    /// </summary>
    protected virtual void OnClaimed( string claimedProjectKey, DateTime now, TimeSpan period ) { }
}
