// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.Testing;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.LicenseServer;

/// <summary>
/// Tests a license server from end to end: registering its URL, consuming the licence it leases, listing it, and
/// unregistering it.
/// </summary>
public sealed class LicenseServerEndToEndTests : LicenseServerTestsBase
{
    public LicenseServerEndToEndTests( ITestOutputHelper logger ) : base( logger ) { }

    /// <summary>
    /// Asserts that a registration succeeded, naming the reason when it did not. The message of a result cannot be
    /// read unless it failed, so it is read only on the failing branch.
    /// </summary>
    private static void AssertSucceeded( LicenseRegistrationResult result )
        => Assert.True( result.IsSuccess, result.IsSuccess ? null : result.ErrorMessage );

    private ILicenseConsumptionService ConsumptionService
        => this.ServiceProvider.GetRequiredBackstageService<ILicenseConsumptionService>();

    private async Task<bool> TryConsumeAsync( LicenseConsumptionOptions? options = null )
    {
        var consumer = await this.ConsumptionService.CreateConsumerAsync( options, this.Messages.Add );

        // The messages are collected from both calls: the consumer reports what it found while it was built, and the
        // requirement reports what acquiring a licence on demand found.
        return await consumer.TryConsumeAsync( LicenseRequirement.Any, this.Messages.Add );
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Registration.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests that registering a license server contacts it, so that the user learns at once whether the URL is right
    /// and whether the server has a licence for them.
    /// </summary>
    [Fact]
    public async Task RegisteringAServerContactsIt()
    {
        var server = this.CreateServer();

        var result = await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        AssertSucceeded( result );
        server.AssertContacted();
    }

    /// <summary>
    /// Tests that what is registered is the URL and not the licence key that the server leases today, which expires
    /// and which the server may replace.
    /// </summary>
    [Fact]
    public async Task RegisteredLicenseIsTheUrl()
    {
        var server = this.CreateServer();

        var result = await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        Assert.Equal( server.Url, result.RegisteredLicense!.LicenseString );
        Assert.Equal( server.Url, result.RegisteredLicense.LicenseServerUrl );
        Assert.NotNull( result.RegisteredLicense.Lease );
    }

    [Fact]
    public async Task RegisteredServerIsListed()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        var registered = Assert.Single( this.LicenseRegistrationService.RegisteredLicenses );
        Assert.Equal( server.Url, registered.LicenseServerUrl );
        Assert.NotNull( registered.Lease );
    }

    /// <summary>
    /// Tests that listing the registered licences contacts nothing: it reads the stored lease, so that a command that
    /// shows what is registered never waits for a network.
    /// </summary>
    [Fact]
    public async Task ListingContactsNothing()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        server.ClearRequests();

        _ = this.LicenseRegistrationService.RegisteredLicenses.ToList();

        server.AssertNotContacted();
    }

    /// <summary>
    /// Tests that a server which is registered but has leased nothing yet is still listed, so that the user sees what
    /// they configured rather than an empty list.
    /// </summary>
    [Fact]
    public async Task ServerWithoutALeaseIsStillListed()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        this.LeaseStore.RemoveAllLeases();

        var registered = Assert.Single( this.LicenseRegistrationService.RegisteredLicenses );
        Assert.Equal( server.Url, registered.LicenseServerUrl );
        Assert.Null( registered.Lease );
    }

    [Theory]
    [InlineData( LicenseServerFault.Unreachable )]
    [InlineData( LicenseServerFault.Forbidden )]
    [InlineData( LicenseServerFault.GarbageResponse )]
    public async Task RegisteringAFailingServerStoresNothing( LicenseServerFault fault )
    {
        var server = this.CreateServer();
        server.FaultMode = fault;

        var result = await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        Assert.False( result.IsSuccess );
        Assert.Empty( this.LicenseRegistrationService.RegisteredLicenses );
    }

    /// <summary>
    /// Tests that the explanation the server gives for a denial reaches the user, which is the only thing that tells
    /// them what to do about it.
    /// </summary>
    [Fact]
    public async Task DenialOfTheServerReachesTheUser()
    {
        var server = this.CreateServer();
        server.FaultMode = LicenseServerFault.Forbidden;
        server.DenialMessage = "No license with free capacity. Ask your administrator for a seat.";

        var result = await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        Assert.False( result.IsSuccess );
        Assert.Equal( server.DenialMessage, result.ErrorMessage );
    }

    [Fact]
    public async Task RegisteringAServerRemovesAPreviouslyRegisteredKey()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.MetalamaProfessionalBusiness );
        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        var registered = Assert.Single( this.LicenseRegistrationService.RegisteredLicenses );
        Assert.Equal( server.Url, registered.LicenseServerUrl );
    }

    [Fact]
    public async Task RegisteringAKeyRemovesAPreviouslyRegisteredServer()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.MetalamaProfessionalBusiness );

        var registered = Assert.Single( this.LicenseRegistrationService.RegisteredLicenses );
        Assert.Null( registered.LicenseServerUrl );
    }

    /// <summary>
    /// Tests that unregistering removes the lease as well as the URL, so that the product really stops using a licence
    /// it was told to forget instead of keeping it until the lease ends.
    /// </summary>
    [Fact]
    public async Task UnregisteringRemovesTheLeaseAsWell()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        Assert.True( this.LeaseStore.TryGetLease( server.Url, out _ ) );

        this.LicenseRegistrationService.RemoveLicenses();

        Assert.Empty( this.LicenseRegistrationService.RegisteredLicenses );
        Assert.False( this.LeaseStore.TryGetLease( server.Url, out _ ) );
    }

    /// <summary>
    /// Tests that testing a server reports what it would lease without registering anything, which is what makes it
    /// usable for diagnosing a server.
    /// </summary>
    [Fact]
    public async Task TestingAServerRegistersNothing()
    {
        var server = this.CreateServer();

        var result = await this.LicenseRegistrationService.TestLicenseServerAsync( server.Url );

        AssertSucceeded( result );
        Assert.Equal( server.Url, result.RegisteredLicense!.LicenseServerUrl );
        Assert.Empty( this.LicenseRegistrationService.RegisteredLicenses );
    }

    [Theory]
    [InlineData( "https://license.test?x=1", "query string" )]
    [InlineData( "ftp://license.test", "HTTP and HTTPS" )]
    [InlineData( "not a url", "Invalid URL" )]
    public async Task TestingAMalformedUrlReportsTheReason( string url, string expectedMessageSubstring )
    {
        var result = await this.LicenseRegistrationService.TestLicenseServerAsync( url );

        Assert.False( result.IsSuccess );
        Assert.Contains( expectedMessageSubstring, result.ErrorMessage, StringComparison.Ordinal );
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Consumption.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests the doctrine that a licence is consumed only when it is really used: building the consumer contacts no
    /// license server, because no requirement has asked for a licence yet. A seat belongs to a licence that is used,
    /// not to one that might have been.
    /// </summary>
    [Fact]
    public async Task BuildingTheConsumerContactsNoServer()
    {
        var server = this.CreateServer();

        _ = await this.ConsumptionService.CreateConsumerAsync(
            new LicenseConsumptionOptions { ProjectLicenseKey = server.Url, IgnoredLicenseSources = LicenseSourceKind.UserProfile },
            this.Messages.Add );

        server.AssertNotContacted();
    }

    /// <summary>
    /// Tests that the server is contacted by the requirement that needs it, and only once however many requirements
    /// follow.
    /// </summary>
    [Fact]
    public async Task ServerIsContactedOnceByTheRequirementThatNeedsIt()
    {
        var server = this.CreateServer();

        var consumer = await this.ConsumptionService.CreateConsumerAsync(
            new LicenseConsumptionOptions { ProjectLicenseKey = server.Url, IgnoredLicenseSources = LicenseSourceKind.UserProfile },
            this.Messages.Add );

        server.AssertNotContacted();

        Assert.True( await consumer.TryConsumeAsync( LicenseRequirement.Any ) );
        server.AssertContacted();

        Assert.True( await consumer.TryConsumeAsync( LicenseRequirement.Any ) );
        server.AssertContacted();
    }

    /// <summary>
    /// Tests the case the whole deferral exists for: a licence key that satisfies the requirement means the server is
    /// never contacted, so no seat is taken from the pool of the team.
    /// </summary>
    [Fact]
    public async Task EligibleKeyMeansTheServerIsNeverContacted()
    {
        var server = this.CreateServer();

        var consumer = await this.ConsumptionService.CreateConsumerAsync(
            new LicenseConsumptionOptions { ProjectLicenseKey = LicenseKeyProvider.MetalamaProfessionalBusiness },
            this.Messages.Add );

        Assert.True( await consumer.TryConsumeAsync( LicenseRequirement.Any ) );

        server.AssertNotContacted();
        Assert.Equal( 0, server.OccupiedSeatCount );
    }

    /// <summary>
    /// Tests the whole point of the feature: a registered license server licenses a build.
    /// </summary>
    [Fact]
    public async Task RegisteredServerLicensesTheBuild()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        Assert.True( await this.TryConsumeAsync() );
    }

    /// <summary>
    /// Tests that a build which already holds a valid lease contacts nothing. A compilation must not wait for a
    /// network when it has a licence.
    /// </summary>
    [Fact]
    public async Task BuildWithAValidLeaseContactsNothing()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        server.ClearRequests();

        Assert.True( await this.TryConsumeAsync() );
        server.AssertNotContacted();
    }

    /// <summary>
    /// Tests that a stored lease licenses a build through a consumption service that never acquired it, which is what
    /// the store exists for: a later run reads the lease of an earlier one instead of leasing a second seat.
    /// </summary>
    /// <remarks>
    /// The second service is built over the same configuration rather than in a second service provider, because the
    /// in-memory configuration manager of a test is per provider, so a cloned provider would start from an empty user
    /// profile and would test nothing.
    /// </remarks>
    [Fact]
    public async Task StoredLeaseLicensesABuildThatNeverAcquiredIt()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        server.ClearRequests();

        var secondService = new LicenseConsumptionService(
            this.ServiceProvider,
            [new UserProfileLicenseSource( this.ServiceProvider )] );

        var consumer = await secondService.CreateConsumerAsync();

        Assert.True( await consumer.TryConsumeAsync( LicenseRequirement.Any ) );
        server.AssertNotContacted();
    }

    [Fact]
    public async Task UnreachableServerLeavesTheBuildUnlicensed()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        this.LeaseStore.RemoveAllLeases();
        server.FaultMode = LicenseServerFault.Unreachable;

        Assert.False( await this.TryConsumeAsync() );
        Assert.Contains( this.Messages, m => m.Text.Contains( "Cannot get a lease", StringComparison.Ordinal ) );
    }

    /// <summary>
    /// Tests that a registered licence key is preferred to a license server, and that the server is not contacted at
    /// all: a build that a key can license must cost neither a request nor a seat.
    /// </summary>
    [Fact]
    public async Task ProjectLicenseKeyIsPreferredToAServer()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        this.LeaseStore.RemoveAllLeases();
        server.ClearRequests();

        var canConsume = await this.TryConsumeAsync(
            new LicenseConsumptionOptions { ProjectLicenseKey = LicenseKeyProvider.MetalamaProfessionalBusiness } );

        Assert.True( canConsume );
        server.AssertNotContacted();
    }

    /// <summary>
    /// Tests that a license server reached through the MSBuild property or the environment variable licenses the
    /// build. That source parsed its value as a licence key before this feature, so a URL was rejected.
    /// </summary>
    [Fact]
    public async Task ServerFromTheBuildPropertyLicensesTheBuild()
    {
        var server = this.CreateServer();

        var canConsume = await this.TryConsumeAsync(
            new LicenseConsumptionOptions
            {
                ProjectLicenseKey = server.Url, IgnoredLicenseSources = LicenseSourceKind.UserProfile
            } );

        Assert.True( canConsume );
        server.AssertContacted();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Eligibility.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests the rule that decides whether a license server may lease a licence key at all: its own field when it has
    /// one, then a refusal for a per-usage key, then an identifier in the range issued before 5.0 RTM.
    /// </summary>
    [Theory]
    [InlineData( nameof(TestLicenseKeyProvider.MetalamaEnterpriseLicenseServerEligible), true )]
    [InlineData( nameof(TestLicenseKeyProvider.MetalamaProfessionalNotLicenseServerEligible), false )]
    [InlineData( nameof(TestLicenseKeyProvider.MetalamaProfessionalEligibleByIdUpperBound), true )]
    [InlineData( nameof(TestLicenseKeyProvider.MetalamaProfessionalIneligibleByIdAboveBound), false )]
    public async Task OnlyAnEligibleKeyIsLeased( string licenseKeyName, bool expectedEligible )
    {
        var server = this.CreateServer();
        server.LicenseKey = LicenseKeyProvider.GetLicenseKey( licenseKeyName );

        var result = await this.LicenseRegistrationService.TestLicenseServerAsync( server.Url );

        Assert.Equal( expectedEligible, result.IsSuccess );

        if ( !expectedEligible )
        {
            Assert.Contains( "not eligible for a license server", result.ErrorMessage, StringComparison.Ordinal );
        }
    }

    /// <summary>
    /// Tests that an ineligible leased key leaves the build unlicensed and reports the reason, rather than raising.
    /// </summary>
    [Fact]
    public async Task IneligibleLeasedKeyLeavesTheBuildUnlicensed()
    {
        var server = this.CreateServer();
        server.LicenseKey = LicenseKeyProvider.MetalamaProfessionalNotLicenseServerEligible;

        var canConsume = await this.TryConsumeAsync(
            new LicenseConsumptionOptions { ProjectLicenseKey = server.Url, IgnoredLicenseSources = LicenseSourceKind.UserProfile } );

        Assert.False( canConsume );
        Assert.Contains( this.Messages, m => m.Text.Contains( "not eligible for a license server", StringComparison.Ordinal ) );
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The policy on an insecure server.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests that an <c>http://</c> server is used by default, because the policy warns rather than refusing: an
    /// organisation that has always run its server over HTTP must not have its builds broken by an upgrade.
    /// </summary>
    [Fact]
    public async Task InsecureServerIsUsedByDefault()
    {
        var server = this.CreateServer( "http://license.test" );

        var canConsume = await this.TryConsumeAsync(
            new LicenseConsumptionOptions { ProjectLicenseKey = server.Url, IgnoredLicenseSources = LicenseSourceKind.UserProfile } );

        Assert.True( canConsume );
        server.AssertContacted();
    }

    /// <summary>
    /// Tests that the policy set to refuse leaves the build unlicensed and, above all, sends nothing: the whole point
    /// is that the user name and the machine name do not travel in cleartext.
    /// </summary>
    [Fact]
    public async Task InsecureServerIsRefusedAndNotContactedUnderTheErrorPolicy()
    {
        var server = this.CreateServer( "http://license.test" );

        var canConsume = await this.TryConsumeAsync(
            new LicenseConsumptionOptions
            {
                ProjectLicenseKey = server.Url,
                IgnoredLicenseSources = LicenseSourceKind.UserProfile,
                InsecureLicenseServerHandling = InsecureLicenseServerHandling.Error
            } );

        Assert.False( canConsume );
        server.AssertNotContacted();
        Assert.Contains( this.Messages, m => m.Text.Contains( "cleartext", StringComparison.Ordinal ) );
    }

    [Fact]
    public async Task InsecureServerIsSilentUnderTheAllowPolicy()
    {
        var server = this.CreateServer( "http://license.test" );

        var canConsume = await this.TryConsumeAsync(
            new LicenseConsumptionOptions
            {
                ProjectLicenseKey = server.Url,
                IgnoredLicenseSources = LicenseSourceKind.UserProfile,
                InsecureLicenseServerHandling = InsecureLicenseServerHandling.Allow
            } );

        Assert.True( canConsume );
        Assert.DoesNotContain( this.Messages, m => m.Text.Contains( "cleartext", StringComparison.Ordinal ) );
    }

    /// <summary>
    /// Tests that a secure server is never reported, whatever the policy.
    /// </summary>
    [Theory]
    [InlineData( InsecureLicenseServerHandling.Warning )]
    [InlineData( InsecureLicenseServerHandling.Error )]
    [InlineData( InsecureLicenseServerHandling.Allow )]
    public async Task SecureServerIsNeverReported( InsecureLicenseServerHandling handling )
    {
        var server = this.CreateServer( "https://license.test" );

        var canConsume = await this.TryConsumeAsync(
            new LicenseConsumptionOptions
            {
                ProjectLicenseKey = server.Url, IgnoredLicenseSources = LicenseSourceKind.UserProfile, InsecureLicenseServerHandling = handling
            } );

        Assert.True( canConsume );
        Assert.DoesNotContain( this.Messages, m => m.Text.Contains( "cleartext", StringComparison.Ordinal ) );
    }

    /// <summary>
    /// Tests that the environment variable applies when the build sets no policy of its own, which is what makes the
    /// setting reachable from a command line and from an integrated development environment.
    /// </summary>
    [Fact]
    public async Task EnvironmentVariableAppliesWhenTheBuildSetsNoPolicy()
    {
        var server = this.CreateServer( "http://license.test" );

        this.EnvironmentVariableProvider.Environment["METALAMA_ALLOW_INSECURE_LICENSE_SERVER"] = "Error";

        var canConsume = await this.TryConsumeAsync(
            new LicenseConsumptionOptions { ProjectLicenseKey = server.Url, IgnoredLicenseSources = LicenseSourceKind.UserProfile } );

        Assert.False( canConsume );
        server.AssertNotContacted();
    }

    /// <summary>
    /// Tests that the setting of the build wins over the environment variable, so that a build script can override
    /// what the machine says.
    /// </summary>
    [Fact]
    public async Task BuildPolicyWinsOverTheEnvironmentVariable()
    {
        var server = this.CreateServer( "http://license.test" );

        this.EnvironmentVariableProvider.Environment["METALAMA_ALLOW_INSECURE_LICENSE_SERVER"] = "Error";

        var canConsume = await this.TryConsumeAsync(
            new LicenseConsumptionOptions
            {
                ProjectLicenseKey = server.Url,
                IgnoredLicenseSources = LicenseSourceKind.UserProfile,
                InsecureLicenseServerHandling = InsecureLicenseServerHandling.Allow
            } );

        Assert.True( canConsume );
        server.AssertContacted();
    }

    [Theory]
    [InlineData( null, InsecureLicenseServerHandling.Warning )]
    [InlineData( "", InsecureLicenseServerHandling.Warning )]
    [InlineData( "   ", InsecureLicenseServerHandling.Warning )]
    [InlineData( "Warning", InsecureLicenseServerHandling.Warning )]
    [InlineData( "nonsense", InsecureLicenseServerHandling.Warning )]
    [InlineData( "Error", InsecureLicenseServerHandling.Error )]
    [InlineData( "error", InsecureLicenseServerHandling.Error )]
    [InlineData( "False", InsecureLicenseServerHandling.Error )]
    [InlineData( "FALSE", InsecureLicenseServerHandling.Error )]
    [InlineData( "Allow", InsecureLicenseServerHandling.Allow )]
    [InlineData( "true", InsecureLicenseServerHandling.Allow )]
    [InlineData( " True ", InsecureLicenseServerHandling.Allow )]
    public void PolicyIsParsedWithThePostSharpSynonyms( string? value, InsecureLicenseServerHandling expected )
    {
        Assert.Equal( expected, InsecureLicenseServerHandlingParser.Parse( value ) );
    }

    [Theory]
    [InlineData( "http://license.test", true )]
    [InlineData( "http://localhost:8080", true )]
    [InlineData( "https://license.test", false )]
    [InlineData( "not a url", false )]
    public void InsecurityIsDecidedByTheScheme( string url, bool expectedInsecure )
    {
        Assert.Equal( expectedInsecure, InsecureLicenseServerHandlingParser.IsInsecure( url ) );
    }
}
