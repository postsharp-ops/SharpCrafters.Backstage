// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Win32;
using SharpCrafters.Backstage.Configuration.Registry;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Registry;

/// <summary>
/// Tests the adapter that reads and writes the registry of the machine, against a key of its own that is deleted
/// afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Everything else about the registry is tested against a hive kept in memory, which is a model of what the registry
/// is believed to do. These tests are what check the belief, and they are the only ones that can: that a Boolean is
/// stored as a <c>DWORD</c> and a date as a <c>QWORD</c>, that a number comes back with the width it was written
/// with, that the unnamed value of a key is a value like any other, and that a change is notified more than once.
/// Each of those is something PostSharp 2026.0 depends on, and each would be silently wrong if the model were.
/// </para>
/// <para>
/// The key lives under <c>HKEY_CURRENT_USER</c>, is named after a fresh identifier so that two runs cannot collide,
/// and is deleted whether the test passes or fails.
/// </para>
/// </remarks>
#pragma warning disable CA1416 // Every test is guarded by a platform check, and so is the clean-up.
public sealed class WindowsRegistryServiceTests : IDisposable
{
    private const string _skipReason = "The registry exists on Windows only.";

    private static bool IsWindows => RuntimeInformation.IsOSPlatform( OSPlatform.Windows );

    /// <summary>
    /// The key that the tests own. It is under the key of the vendor rather than at the root of the software key, so
    /// that anything left behind by a run that was killed is recognizable.
    /// </summary>
    private readonly string _keyPath = @"Software\SharpCrafters\Tests\" + Guid.NewGuid().ToString( "N" );

    private readonly IRegistryService _service = WindowsRegistryService.Instance;

    public void Dispose()
    {
        if ( !IsWindows )
        {
            return;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey( RegistryHive.CurrentUser, RegistryView.Registry32 );
            baseKey.DeleteSubKeyTree( this._keyPath, false );
        }
        catch ( Exception )
        {
            // A key that cannot be deleted is not worth failing a test that has already run.
        }
    }

    /// <summary>
    /// Opens the key with the API of the framework rather than with the adapter, so that what the adapter wrote is
    /// read by something other than itself.
    /// </summary>
    private RegistryKey OpenKeyDirectly()
    {
        var baseKey = RegistryKey.OpenBaseKey( RegistryHive.CurrentUser, RegistryView.Registry32 );

        try
        {
            return baseKey.OpenSubKey( this._keyPath, false )
                   ?? throw new InvalidOperationException( $"The key '{this._keyPath}' was not created." );
        }
        finally
        {
            baseKey.Dispose();
        }
    }

    /// <summary>
    /// Every value is written with the kind that PostSharp 2026.0 reads it with. That version casts what it reads,
    /// so a value of another kind makes it fall back to its default without saying anything.
    /// </summary>
    [SkippableFact]
    public void AValueIsWrittenWithTheKindTheOtherVersionExpects()
    {
        Skip.IfNot( IsWindows, _skipReason );

        using ( var key = this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath ) )
        {
            Assert.NotNull( key );
            key.SetStringValue( "AString", "a value" );
            key.SetDWordValue( "ANumber", 42 );
            key.SetQWordValue( "ADate", 1234567890123 );
        }

        using var directKey = this.OpenKeyDirectly();

        Assert.Equal( RegistryValueKind.String, directKey.GetValueKind( "AString" ) );
        Assert.Equal( RegistryValueKind.DWord, directKey.GetValueKind( "ANumber" ) );
        Assert.Equal( RegistryValueKind.QWord, directKey.GetValueKind( "ADate" ) );
    }

    /// <summary>
    /// A number comes back with the width it was written with, which is what
    /// <see cref="RegistryValueCodec"/> assumes: it reads a date as a <see cref="long"/> and a Boolean as an
    /// <see cref="int"/>, and treats anything else as absent.
    /// </summary>
    [SkippableFact]
    public void AValueComesBackAsTheTypeTheCodecExpects()
    {
        Skip.IfNot( IsWindows, _skipReason );

        using var key = this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath );
        Assert.NotNull( key );

        key.SetStringValue( "AString", "a value" );
        key.SetDWordValue( "ANumber", 42 );
        key.SetQWordValue( "ADate", 1234567890123 );

        Assert.Equal( "a value", Assert.IsType<string>( key.GetValue( "AString" ) ) );
        Assert.Equal( 42, Assert.IsType<int>( key.GetValue( "ANumber" ) ) );
        Assert.Equal( 1234567890123, Assert.IsType<long>( key.GetValue( "ADate" ) ) );
    }

    /// <summary>
    /// A date survives a round trip through the real registry, which is what a trial start date and a lease
    /// expiration depend on.
    /// </summary>
    [SkippableFact]
    public void ADateSurvivesTheRealRegistry()
    {
        Skip.IfNot( IsWindows, _skipReason );

        var date = new DateTime( 2026, 9, 18, 14, 35, 46, DateTimeKind.Utc );

        using var key = this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath );
        Assert.NotNull( key );

        key.SetQWordValue( "ADate", RegistryValueCodec.DateTimeToQWord( date ) );

        Assert.Equal( date, RegistryValueCodec.QWordToDateTime( key.GetValue( "ADate" ) )!.Value.ToUniversalTime() );
    }

    /// <summary>
    /// The unnamed value of a key is a value like any other, which is where the lease of a license server is kept.
    /// </summary>
    [SkippableFact]
    public void TheUnnamedValueIsAValue()
    {
        Skip.IfNot( IsWindows, _skipReason );

        using var key = this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath );
        Assert.NotNull( key );

        key.SetStringValue( "", "the default value" );

        Assert.Equal( "the default value", key.GetValue( "" ) );

        using var directKey = this.OpenKeyDirectly();
        Assert.Equal( "the default value", directKey.GetValue( null ) );
    }

    [SkippableFact]
    public void AnAbsentValueIsNull()
    {
        Skip.IfNot( IsWindows, _skipReason );

        using var key = this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath );
        Assert.NotNull( key );

        Assert.Null( key.GetValue( "NothingIsHere" ) );
    }

    /// <summary>
    /// Deleting a value that is not there does nothing rather than throwing, which the schemas rely on: they delete
    /// the value of a member that has become absent without first asking whether it was ever written.
    /// </summary>
    [SkippableFact]
    public void DeletingAValueThatIsNotThereDoesNothing()
    {
        Skip.IfNot( IsWindows, _skipReason );

        using var key = this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath );
        Assert.NotNull( key );

        key.DeleteValue( "NothingIsHere" );

        key.SetStringValue( "AString", "a value" );
        key.DeleteValue( "AString" );

        Assert.Null( key.GetValue( "AString" ) );
    }

    /// <summary>
    /// A key that does not exist is reported as absent rather than created, which is how a product that has never
    /// run is told apart from one that has.
    /// </summary>
    [SkippableFact]
    public void OpeningAKeyThatIsNotThereGivesNothing()
    {
        Skip.IfNot( IsWindows, _skipReason );

        Assert.Null( this._service.OpenKey( RegistryHiveKind.CurrentUser, this._keyPath ) );
    }

    /// <summary>
    /// Creating a key creates every key above it, which is what lets a schema name a path several levels deep.
    /// </summary>
    [SkippableFact]
    public void CreatingAKeyCreatesThePathAboveIt()
    {
        Skip.IfNot( IsWindows, _skipReason );

        using ( var key = this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath + @"\One\Two" ) )
        {
            Assert.NotNull( key );
            key.SetStringValue( "AString", "a value" );
        }

        using var directKey = this.OpenKeyDirectly();
        using var subKey = directKey.OpenSubKey( @"One\Two" );

        Assert.NotNull( subKey );
        Assert.Equal( "a value", subKey.GetValue( "AString" ) );
    }

    [SkippableFact]
    public void TheNamesOfTheValuesAndOfTheSubKeysAreEnumerated()
    {
        Skip.IfNot( IsWindows, _skipReason );

        using var key = this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath );
        Assert.NotNull( key );

        key.SetStringValue( "First", "1" );
        key.SetStringValue( "Second", "2" );
        key.CreateSubKey( "ASubKey" )?.Dispose();

        Assert.Equal( ["First", "Second"], key.GetValueNames().OrderBy( n => n, StringComparer.Ordinal ) );
        Assert.Equal( ["ASubKey"], key.GetSubKeyNames() );
    }

    /// <summary>
    /// A sub-key is deleted with everything below it, which is what clearing a cache of leases does.
    /// </summary>
    [SkippableFact]
    public void ASubKeyIsDeletedWithEverythingBelowIt()
    {
        Skip.IfNot( IsWindows, _skipReason );

        this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath + @"\One\Two" )?.Dispose();

        using var key = this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath );
        Assert.NotNull( key );

        key.DeleteSubKeyTree( "One" );
        Assert.Empty( key.GetSubKeyNames() );

        // Deleting one that is not there does nothing, as deleting an absent value does.
        key.DeleteSubKeyTree( "One" );
    }

    /// <summary>
    /// A change is notified, and so is the one after it.
    /// </summary>
    /// <remarks>
    /// The second change is the point of the test. The notification of the registry is one-shot and has to be asked
    /// for again, and it is removed when the thread that asked for it ends — which is a thread of the pool here,
    /// returned to the pool at once — unless <c>REG_NOTIFY_THREAD_AGNOSTIC</c> is passed. Either mistake leaves the
    /// first change notified and every later one lost, with nothing to show for it.
    /// </remarks>
    [SkippableFact]
    public void AChangeIsNotifiedAndSoIsTheNextOne()
    {
        Skip.IfNot( IsWindows, _skipReason );

        this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath )?.Dispose();

        using var changed = new SemaphoreSlim( 0 );

        using var watcher = this._service.WatchChanges( RegistryHiveKind.CurrentUser, this._keyPath, () => changed.Release() );
        Assert.NotNull( watcher );

        for ( var change = 1; change <= 3; change++ )
        {
            using ( var key = this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath ) )
            {
                key!.SetDWordValue( "ANumber", change );
            }

            Assert.True(
                changed.Wait( TimeSpan.FromSeconds( 10 ) ),
                $"The change {change} was not notified, so the watch stopped after {change - 1}." );
        }
    }

    /// <summary>
    /// A change below the key is a change of the key, which is what lets one watch cover a configuration object that
    /// spreads over a key and its sub-keys.
    /// </summary>
    [SkippableFact]
    public void AChangeBelowTheKeyIsNotified()
    {
        Skip.IfNot( IsWindows, _skipReason );

        this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath )?.Dispose();

        using var changed = new SemaphoreSlim( 0 );

        using var watcher = this._service.WatchChanges( RegistryHiveKind.CurrentUser, this._keyPath, () => changed.Release() );
        Assert.NotNull( watcher );

        using ( var subKey = this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath + @"\ASubKey" ) )
        {
            subKey!.SetStringValue( "AString", "a value" );
        }

        Assert.True( changed.Wait( TimeSpan.FromSeconds( 10 ) ), "A change below the key was not notified." );
    }

    /// <summary>
    /// The watch stops when it is given up, so that a long-running process that has finished with a key is not
    /// woken by it and does not hold its handle.
    /// </summary>
    [SkippableFact]
    public void TheWatchStopsWhenItIsGivenUp()
    {
        Skip.IfNot( IsWindows, _skipReason );

        this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath )?.Dispose();

        using var changed = new SemaphoreSlim( 0 );

        var watcher = this._service.WatchChanges( RegistryHiveKind.CurrentUser, this._keyPath, () => changed.Release() );
        Assert.NotNull( watcher );

        watcher.Dispose();

        using ( var key = this._service.CreateKey( RegistryHiveKind.CurrentUser, this._keyPath ) )
        {
            key!.SetDWordValue( "ANumber", 1 );
        }

        // A short wait: the point is that nothing arrives, and a long one would only make the suite slower.
        Assert.False( changed.Wait( TimeSpan.FromSeconds( 1 ) ), "A change was notified after the watch was given up." );
    }
}
#pragma warning restore CA1416
