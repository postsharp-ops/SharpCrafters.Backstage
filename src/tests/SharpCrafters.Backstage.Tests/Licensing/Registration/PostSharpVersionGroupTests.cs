// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using PostSharp.Backstage;
using SharpCrafters.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Licenses;
using SharpCrafters.Backstage.Licensing.Registration;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Registration;

/// <summary>
/// Tests which of the three places a registered PostSharp license key is put in: the value that PostSharp 3.0 reads,
/// the flat list that every version reads, or the group named after the earliest version that can read the key.
/// </summary>
/// <remarks>
/// <para>
/// The choice matters only on a machine where more than one version is installed, which for PostSharp is the normal
/// case rather than the exceptional one. Every installed version reads the same registry key, so a license key in
/// the flat list is read by all of them, including the ones released before the key's format existed. Those report
/// it as invalid, which sends the user to support about a license that is in fact perfectly good.
/// </para>
/// <para>
/// <see cref="LicenseGroupTests"/> covers the same question for Metalama, where the groups are read and written
/// through <c>licensing.json</c>. This covers the PostSharp side, where the products co-exist and the declaration
/// that decides the group is one PostSharp has carried since 6.9.3.
/// </para>
/// </remarks>
public sealed class PostSharpVersionGroupTests : LicensingTestsBase
{
    public PostSharpVersionGroupTests( ITestOutputHelper logger ) : base( logger, product: PostSharpProduct.Instance, version: PostSharpVersion ) { }

    /// <summary>
    /// Registers a license key into an empty configuration and returns what the configuration then holds.
    /// </summary>
    private LicensingConfiguration Register( string licenseKey )
    {
        Assert.True( LicenseKeyData.TryDeserialize( licenseKey, out var licenseKeyData, out var errorMessage ), errorMessage );

        return new LicensingConfiguration().SetLicense( licenseKeyData.ToLicenseRegistrationProperties( this.Catalog ), this.Catalog );
    }

    /// <summary>
    /// A key of the current generation carries a field of the tolerant format, so it declares 6.9.3 and the versions
    /// before that cannot read it.
    /// </summary>
    [Fact]
    public void AKeyOfTheCurrentGenerationDeclaresTheTolerantVersion()
    {
        Assert.True( LicenseKeyData.TryDeserialize( LicenseKeyProvider.PostSharpUltimate, out var licenseKeyData, out var errorMessage ), errorMessage );

        Assert.True( licenseKeyData.HasLengthPrefixedField );
        Assert.Equal( new Version( 6, 9, 3 ), licenseKeyData.MinPostSharpVersion );
        Assert.Equal( new Version( 6, 9, 3 ), licenseKeyData.GetMinPostSharpVersion() );
    }

    /// <summary>
    /// Such a key is put in the group named after the version it declares, which is where PostSharp 2026.0 puts it
    /// too, and not in the flat list that every version reads.
    /// </summary>
    /// <remarks>
    /// PostSharp puts a key in a group whenever the version it needs is 5.0 or later, and every key of the current
    /// generation needs 6.9.3. Leaving such a key in the flat list is invisible between the recent versions, which
    /// all read both places, and shows on a machine that also carries a PostSharp older than 6.9.3: that version
    /// reads the flat list, cannot parse the key, and reports a license the user has paid for as invalid.
    /// </remarks>
    [Fact]
    public void AKeyOfTheCurrentGenerationIsPutInTheGroupOfItsVersion()
    {
        var configuration = this.Register( LicenseKeyProvider.PostSharpUltimate );

        var group = Assert.Single( configuration.LicensesByMinimalVersion! );
        Assert.Equal( "6.9.3", group.Key );
        Assert.Equal<string?[]>( [LicenseKeyProvider.PostSharpUltimate], group.Value.ToArray() );
        Assert.Empty( configuration.Licenses );
        Assert.Null( configuration.LegacyLicense );
    }

    /// <summary>
    /// A key of the legacy encoding, which every released version can read, is left in the flat list. Naming a version
    /// for it would hide it from the versions that want it and gain nothing.
    /// </summary>
    /// <remarks>
    /// The legacy encoding is what makes a key readable by PostSharp 3.0, and it is the only encoding that reaches
    /// the flat list: a key naming one of the products introduced by 6.6 needs 6.6 whatever else it carries.
    /// </remarks>
    [Fact]
#pragma warning disable CS0618 // Type or member is obsolete: the legacy encoding is the subject of this test.
    public void AKeyEveryVersionCanReadIsPutInTheFlatList()
    {
        var builder = new LicenseKeyDataBuilder
        {
            LicenseId = 921, Product = LicenseProduct.PostSharpUltimate1, LicenseType = LicenseType.Business
        };
#pragma warning restore CS0618

        var licenseKey = builder.SignAndSerialize( TestLicensingAuthorityProvider.DsaTestAuthority );

        Assert.True( LicenseKeyData.TryDeserialize( licenseKey, out var licenseKeyData, out var errorMessage ), errorMessage );
        Assert.False( licenseKeyData.HasLengthPrefixedField );
        Assert.True( licenseKeyData.GetMinPostSharpVersion() < new Version( 5, 0, 0 ) );

        var configuration = this.Register( licenseKey );

        Assert.Equal<string?[]>( [licenseKey], configuration.Licenses.ToArray() );
        Assert.Null( configuration.LicensesByMinimalVersion );
    }

    /// <summary>
    /// A key that was registered can be read back. The group is named after a version, and a version that named a
    /// group above its own would write a key it then cannot see, so that the user registers a license and is told
    /// that none is registered.
    /// </summary>
    [Fact]
    public async Task AKeyThatWasRegisteredIsHeld()
    {
        Assert.True( (await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.PostSharpUltimate )).IsSuccess );

        var registered = this.LicenseRegistrationService.RegisteredLicenses.Select( l => l.LicenseString ).ToArray();

        Assert.Contains( LicenseKeyProvider.PostSharpUltimate, registered );
    }

    /// <summary>
    /// The version that PostSharp reports is at or above every group its own keys are put in, so it always reads back
    /// what it has just written.
    /// </summary>
    [Fact]
    public void ThisVersionReadsEveryGroupItWrites()
    {
        var licenseKeys = new[]
        {
            LicenseKeyProvider.PostSharpUltimate, LicenseKeyProvider.PostSharpFramework, LicenseKeyProvider.PostSharpLogging,
            LicenseKeyProvider.PostSharpEssentials
        };

        foreach ( var licenseKey in licenseKeys )
        {
            Assert.True( LicenseKeyData.TryDeserialize( licenseKey, out var licenseKeyData, out var errorMessage ), errorMessage );

            var minimalVersion = this.Catalog.GetMinimalVersion( licenseKeyData );

            Assert.True(
                minimalVersion == null || minimalVersion <= this.CurrentVersion,
                $"A key is put in the group {minimalVersion}, which this version ({this.CurrentVersion}) does not read." );
        }
    }

    /// <summary>
    /// A registered license server is not put in a group at all, so that every installed version of PostSharp sees
    /// the server the user configured.
    /// </summary>
    /// <remarks>
    /// A URL is not a license key and has no content to judge, so the family answers for it rather than the key: the
    /// licence the server leases today is not the one it will lease tomorrow. PostSharp answers that there is no
    /// floor, which is also what PostSharp 2026.0 does — it stores the URL as a plain value beside the keys, because
    /// the code that chooses a group runs only for something that parsed as a license key.
    /// </remarks>
    [Fact]
    public void ALicenseServerIsNotPutInAGroup() => Assert.Null( this.Catalog.MinimalLicenseServerVersion );

    /// <summary>
    /// A key signed by the Elliptic Curve DSA authority is put in the group of the version that introduces that
    /// authority, because no earlier version can verify its signature whatever else it carries.
    /// </summary>
    [Fact]
    public void AKeyOfTheNewAuthorityIsPutInItsGroup()
    {
        var licenseKey = new LicenseKeyDataBuilder
        {
            LicenseId = 920,
            Product = LicenseProduct.PostSharpUltimate,
            LicenseType = LicenseType.Business,
            Generation = LicenseGeneration.Current,
            SubscriptionEndDate = LicenseKeyProvider.DefaultSubscriptionExpirationDate
        }.SignAndSerialize( TestLicensingAuthorityProvider.ECDsaTestAuthority );

        var configuration = this.Register( licenseKey );

        var group = Assert.Single( configuration.LicensesByMinimalVersion! );
        Assert.Equal( "2027.0", group.Key );
        Assert.Equal<string?[]>( [licenseKey], group.Value.ToArray() );
        Assert.Empty( configuration.Licenses );
    }

    /// <summary>
    /// The free edition of PostSharp joins the group beside a paid key rather than replacing it, which is what lets a
    /// user hold a pattern library licence and the free edition at once.
    /// </summary>
    /// <remarks>
    /// This is the case that a group carrying one key at a time would get wrong. Metalama puts at most one key in a
    /// group, so replacing the group was enough for it; PostSharp puts every key of a user in one, so a group has to
    /// accumulate them exactly as the flat list does.
    /// </remarks>
    [Fact]
    public void TheFreeEditionJoinsTheGroup()
    {
        var configuration = this.Register( LicenseKeyProvider.PostSharpLogging );

        configuration = configuration.SetLicense( this.ToProperties( LicenseKeyProvider.PostSharpEssentials ), this.Catalog );

        var group = Assert.Single( configuration.LicensesByMinimalVersion! );
        Assert.Equal( "6.9.3", group.Key );
        Assert.Equal( 2, group.Value.Length );
        Assert.Contains( LicenseKeyProvider.PostSharpLogging, group.Value );
        Assert.Contains( LicenseKeyProvider.PostSharpEssentials, group.Value );
        Assert.Empty( configuration.Licenses );
    }

    /// <summary>
    /// Registering a key that replaces the others empties the group of them, rather than leaving them beside it.
    /// </summary>
    [Fact]
    public void RegisteringUltimateEmptiesTheGroupOfTheOthers()
    {
        var configuration = this.Register( LicenseKeyProvider.PostSharpLogging );
        configuration = configuration.SetLicense( this.ToProperties( LicenseKeyProvider.PostSharpUltimate ), this.Catalog );

        var group = Assert.Single( configuration.LicensesByMinimalVersion! );
        Assert.Equal<string?[]>( [LicenseKeyProvider.PostSharpUltimate], group.Value.ToArray() );
    }

    /// <summary>
    /// A group that nothing survives in is dropped rather than left behind empty.
    /// </summary>
    [Fact]
    public void AGroupThatIsEmptiedIsDropped()
    {
        var configuration = this.Register( LicenseKeyProvider.PostSharpLogging ).RemoveAllLicenses();

        Assert.Null( configuration.LicensesByMinimalVersion );
        Assert.Empty( configuration.Licenses );
    }

    private LicenseRegistrationProperties ToProperties( string licenseKey )
    {
        Assert.True( LicenseKeyData.TryDeserialize( licenseKey, out var licenseKeyData, out var errorMessage ), errorMessage );

        return licenseKeyData.ToLicenseRegistrationProperties( this.Catalog );
    }
}
