// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Licenses;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Licenses;

/// <summary>
/// Tests what <see cref="ILicense.GetRegistrationBlockerAsync"/> reports about a licence that cannot be registered.
/// </summary>
/// <remarks>
/// The blocker carries a <see cref="LicenseRegistrationBlockerKind"/> beside its message so that a test asserts what
/// is wrong rather than matching the wording, which changes. Matching the wording is how a test keeps passing after
/// the reason it was written for has been replaced by a different one.
/// </remarks>
public sealed class LicenseRegistrationBlockerTests : LicensingTestsBase
{
    public LicenseRegistrationBlockerTests( ITestOutputHelper logger ) : base( logger )
    {
        this.UserDeviceDetection.IsInteractiveDevice = true;
    }

    private async Task<LicenseRegistrationBlocker> GetBlockerAsync( string licenseKey )
    {
        var factory = new LicenseFactory( this.ServiceProvider );
        Assert.True( factory.TryCreate( licenseKey, null, out var license, out var errorMessage ), errorMessage );

        return await license.GetRegistrationBlockerAsync();
    }

    /// <summary>
    /// Tests that a licence key a customer bought can be registered. Everything else in this class is a reason to
    /// refuse one, and a rule that refuses too much is worse than no rule at all.
    /// </summary>
    [Fact]
    public async Task ValidLicenseKeyIsNotBlocked()
    {
        var blocker = await this.GetBlockerAsync( LicenseKeyProvider.MetalamaProfessionalBusiness );

        Assert.Equal( LicenseRegistrationBlockerKind.None, blocker.Kind );
        Assert.False( blocker.IsBlocked );
        Assert.Null( blocker.Message );
    }

    /// <summary>
    /// Tests that a licence key which cannot be used at all is blocked as <see cref="LicenseRegistrationBlockerKind.Unusable"/>,
    /// and that the message says which of the many ways of being unusable this one is.
    /// </summary>
    [Fact]
    public async Task UnparsableLicenseKeyIsUnusable()
    {
        var blocker = await this.GetBlockerAsync( "NOT-A-REAL-KEY" );

        Assert.Equal( LicenseRegistrationBlockerKind.Unusable, blocker.Kind );
        Assert.True( blocker.IsBlocked );
        Assert.False( string.IsNullOrEmpty( blocker.Message ) );
    }

    /// <summary>
    /// Tests that a licence key that carries no valid signature of the licensing authority is refused. This is what
    /// makes a licence key worth buying rather than composing.
    /// </summary>
    [Fact]
    public async Task UnsignedLicenseKeyIsUnusable()
    {
        var blocker = await this.GetBlockerAsync( LicenseKeyProvider.MetalamaProfessionalBusinessUnsigned );

        Assert.Equal( LicenseRegistrationBlockerKind.Unusable, blocker.Kind );
    }

    /// <summary>
    /// Tests that a redistribution licence key is told apart from a key that is merely unusable. It is a perfectly
    /// valid key: what it licenses is the code a customer ships, not the machine of the user registering it.
    /// </summary>
    [Fact]
    public async Task RedistributionLicenseKeyIsBlockedAsSuch()
    {
        var blocker = await this.GetBlockerAsync( LicenseKeyProvider.MetalamaProfessionalRedistribution );

        Assert.Equal( LicenseRegistrationBlockerKind.Redistribution, blocker.Kind );
        Assert.True( blocker.IsBlocked );
        Assert.False( string.IsNullOrEmpty( blocker.Message ) );
    }

    /// <summary>
    /// Tests that the default value of the structure is "nothing prevents registration", so that a licence which
    /// returns nothing is registrable rather than blocked for a reason nobody can read.
    /// </summary>
    [Fact]
    public void DefaultBlockerIsNone()
    {
        LicenseRegistrationBlocker blocker = default;

        Assert.Equal( LicenseRegistrationBlockerKind.None, blocker.Kind );
        Assert.False( blocker.IsBlocked );
        Assert.Equal( LicenseRegistrationBlocker.None, blocker );
    }
}
