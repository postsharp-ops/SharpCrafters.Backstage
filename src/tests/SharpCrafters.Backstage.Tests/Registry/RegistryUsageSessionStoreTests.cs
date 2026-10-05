// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage.Configuration;
using SharpCrafters.Backstage.Configuration.Registry;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Threading;
using System;
using System.Linq;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Registry;

/// <summary>
/// Tests the store that keeps the time of the last usage report of each project of PostSharp in a value of its own,
/// under the key where the earlier builds of this version already kept them (issue 2092).
/// </summary>
public sealed class RegistryUsageSessionStoreTests : TestsBase
{
    private static readonly DateTime _now = new( 2026, 9, 15, 10, 30, 0, DateTimeKind.Utc );
    private static readonly TimeSpan _period = TimeSpan.FromDays( 1 );

    private readonly TestRegistryService _registry = new();

    public RegistryUsageSessionStoreTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );
        services.AddService( typeof(IRegistryService), this._registry );
    }

    private RegistryUsageSessionStore CreateStore() => new( this.ServiceProvider, RegistryHiveKind.CurrentUser, PostSharpRegistry.TelemetrySessionsKeyPath );

    private IRegistryKey SessionsKey() => this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, PostSharpRegistry.TelemetrySessionsKeyPath );

    [Fact]
    public void TheRecordsLiveInTheSessionsSubKeyOfTheFeedbackKey()
        => Assert.Equal( @"Software\SharpCrafters\PostSharp 3\Feedback\Sessions", PostSharpRegistry.TelemetrySessionsKeyPath );

    [Fact]
    public void AProjectIsClaimedOncePerPeriod()
    {
        var store = this.CreateStore();

        Assert.True( store.TryClaim( "Project", _now, _period ) );
        Assert.False( store.TryClaim( "Project", _now, _period ) );
        Assert.False( store.TryClaim( "PROJECT", _now, _period ) );
        Assert.True( store.TryClaim( "Project", _now + _period, _period ) );
    }

    [Fact]
    public void EachProjectIsOneValue()
    {
        var store = this.CreateStore();

        Assert.True( store.TryClaim( "Project1", _now, _period ) );
        Assert.True( store.TryClaim( "Project2", _now, _period ) );

        var key = this.SessionsKey();
        Assert.Equal( ["Project1", "Project2"], key.GetValueNames().OrderBy( n => n, StringComparer.Ordinal ) );
        Assert.Equal( _now, key.GetDateTime( "Project1" ) );
    }

    /// <summary>
    /// A value written by an earlier build of this version, through the telemetry configuration, has the same name and
    /// the same encoding, so it is honored without a migration.
    /// </summary>
    [Fact]
    public void AnExistingValueIsHonored()
    {
        this.SessionsKey().SetDateTime( "Project", _now - TimeSpan.FromHours( 1 ) );

        Assert.False( this.CreateStore().TryClaim( "Project", _now, _period ) );
    }

    [Fact]
    public void TheExpiredValuesAreDeletedByAClaim()
    {
        var key = this.SessionsKey();
        key.SetDateTime( "Expired", _now - _period - TimeSpan.FromMinutes( 1 ) );
        key.SetDateTime( "Recent", _now - TimeSpan.FromHours( 1 ) );
        key.SetStringValue( "NotADate", "garbage" );

        Assert.True( this.CreateStore().TryClaim( "Project", _now, _period ) );

        Assert.Equal( ["Project", "Recent"], key.GetValueNames().OrderBy( n => n, StringComparer.Ordinal ) );
    }

    /// <summary>
    /// A project whose lock is held is being claimed by another process, which may be renewing its expired value, so
    /// the clean-up leaves that value alone.
    /// </summary>
    [Fact]
    public void AnExpiredValueOfAProjectBeingClaimedIsNotDeleted()
    {
        var key = this.SessionsKey();
        key.SetDateTime( "Expired", _now - _period - TimeSpan.FromMinutes( 1 ) );

        using ( this.Locks.Pin( this.GetLockName( "Expired" ) ) )
        {
            Assert.True( this.CreateStore().TryClaim( "Project", _now, _period ) );
        }

        Assert.Equal( ["Expired", "Project"], key.GetValueNames().OrderBy( n => n, StringComparer.Ordinal ) );
    }

    /// <summary>
    /// The record of the claimed project is written before the clean-up, so a clean-up that fails does not make the
    /// claim fail: that would suppress the report and every later one of the period.
    /// </summary>
    [Fact]
    public void AFailedCleanUpDoesNotFailTheClaim()
    {
        var key = this.SessionsKey();
        key.SetDateTime( "Expired", _now - _period - TimeSpan.FromMinutes( 1 ) );
        this.Locks.ArmException( this.GetLockName( "Expired" ), () => new UnauthorizedAccessException() );

        var store = this.CreateStore();

        Assert.True( store.TryClaim( "Project", _now, _period ) );
        Assert.False( store.TryClaim( "Project", _now, _period ) );
    }

    private string GetLockName( string projectKey )
        => this.Locks.GetGlobalLockName(
            this._registry.GetDisplayPath( RegistryHiveKind.CurrentUser, PostSharpRegistry.TelemetrySessionsKeyPath ) + "!" + projectKey.ToUpperInvariant() );

    [Fact]
    public void AClaimTouchesNothingOutsideTheSessionsKey()
    {
        var feedbackKey = this._registry.GetOrCreateKey( RegistryHiveKind.CurrentUser, PostSharpRegistry.FeedbackKeyPath );
        feedbackKey.SetDWordValue( "UsageReportingAction", 1 );

        Assert.True( this.CreateStore().TryClaim( "Project", _now, _period ) );

        Assert.Equal( ["UsageReportingAction"], feedbackKey.GetValueNames() );
        Assert.Equal( ["Sessions"], feedbackKey.GetSubKeyNames() );
    }

    [Fact]
    public void AProjectThatAnotherProcessIsClaimingIsNotClaimed()
    {
        var store = this.CreateStore();

        using ( this.Locks.Pin( this.GetLockName( "Project" ) ) )
        {
            Assert.False( store.TryClaim( "Project", _now, _period ) );
        }

        Assert.True( store.TryClaim( "Project", _now, _period ) );
    }

    [Fact]
    public void ARegistryThatCannotBeWrittenYieldsNoClaim()
    {
        this._registry.IsSupported = false;

        Assert.False( this.CreateStore().TryClaim( "Project", _now, _period ) );
    }
}
