// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Configuration;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Licensing.LicenseServer;
using SharpCrafters.Backstage.Licensing.Registration;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Tests.Licensing.Consumption;
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

    /// <summary>
    /// Silences the warning about a license server reached over HTTP, as a user would by editing their licensing
    /// configuration.
    /// </summary>
    private void AllowInsecureLicenseServer()
        => this.ConfigurationManager!.Update<SharpCrafters.Backstage.Licensing.LicensingConfiguration>(
            configuration => configuration with { AllowInsecureLicenseServer = true } );

    private ILicenseConsumptionService ConsumptionService
        => this.ServiceProvider.GetRequiredBackstageService<ILicenseConsumptionService>();

    private async Task<bool> TryConsumeAsync( LicenseConsumptionOptions? options = null )
    {
        var consumer = await this.ConsumptionService.CreateConsumerAsync( options, this.Messages.Add );

        // The messages are collected from both calls: the consumer reports what it found while it was built, and the
        // requirement reports why nothing satisfied it.
        return consumer.TryConsume( LicenseRequirement.Any, this.Messages.Add );
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

    /// <summary>
    /// Tests that a user who registered a license server sees it in the list of what they registered. Anything the
    /// product uses to license a build must be visible to the person answering for it.
    /// </summary>
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
    /// Tests that the user is told which product their license server grants them, and not merely that a server is
    /// configured. Which product they get is the question they registered the server to have answered.
    /// </summary>
    [Fact]
    public async Task ListedServerNamesTheProductItLeases()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        var registered = Assert.Single( this.LicenseRegistrationService.RegisteredLicenses );
        Assert.Equal( SharpCrafters.Backstage.Licensing.LicenseProduct.MetalamaEnterprise, registered.Product );
    }

    /// <summary>
    /// Tests that the licence key a server leases is never presented as something the user registered. They did not
    /// choose it, it expires in days, and they can do nothing with it; showing it invites them to keep it, and
    /// support to ask them for it.
    /// </summary>
    [Fact]
    public async Task LeasedLicenseKeyIsNotPresentedAsTheRegisteredLicense()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        var registered = Assert.Single( this.LicenseRegistrationService.RegisteredLicenses );
        Assert.Equal( server.Url, registered.LicenseString );
        Assert.NotEqual( server.LicenseKey, registered.LicenseString );
    }

    /// <summary>
    /// Tests that a licence key which only a later version of the product understands does not hide the license
    /// server the user registered. Several versions share one configuration, so a user who registers a key with a
    /// newer version must still see, and still be licensed by, the server the older one uses. See issue #1922.
    /// </summary>
    [Fact]
    public async Task ServerSurvivesAGroupThatThisVersionCannotRead()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        this.AddLicenseGroup( "2099.0", "999-THIS-LICENSE-KEY-REQUIRES-A-LATER-VERSION" );

        var registered = Assert.Single( this.LicenseRegistrationService.RegisteredLicenses );
        Assert.Equal( server.Url, registered.LicenseServerUrl );

        Assert.Equal( new Version( 2099, 0 ), Assert.Single( this.LicenseRegistrationService.UnsupportedRegisteredLicenseVersions ) );
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

    /// <summary>
    /// Tests that a server which cannot be reached, refuses, or answers nonsense is not registered. Storing it
    /// would leave the user believing they are licensed and finding out on their next build.
    /// </summary>
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

    /// <summary>
    /// Tests that a customer who moves from licence keys to a license server ends up using the server. Leaving the
    /// old key behind would keep licensing their builds from it and would make the move look as though it had not
    /// happened.
    /// </summary>
    [Fact]
    public async Task RegisteringAServerRemovesAPreviouslyRegisteredKey()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.MetalamaProfessionalBusiness );
        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        var registered = Assert.Single( this.LicenseRegistrationService.RegisteredLicenses );
        Assert.Equal( server.Url, registered.LicenseServerUrl );
    }

    /// <summary>
    /// Tests the move in the other direction, which is what a customer does when they leave a license server
    /// behind: the key they have just registered is what licenses their builds, and the server is not contacted
    /// again.
    /// </summary>
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
    /// Tests that a URL which cannot be a license server is refused with the reason it cannot be one, rather than
    /// with the reason it is not a valid license key either.
    /// </summary>
    /// <remarks>
    /// A string that is not a well-formed absolute URI, such as <c>nonsense</c>, is not covered here: it is a license
    /// key as far as the factory is concerned, and it is reported as an unparsable key, which is the right answer.
    /// </remarks>
    [Theory]
    [InlineData( "https://license.test?x=1", "query string" )]
    [InlineData( "ftp://license.test", "HTTP and HTTPS" )]
    [InlineData( "https://alice:secret@license.test", "user name" )]
    public async Task RegisteringAMalformedUrlReportsTheReason( string url, string expectedMessageSubstring )
    {
        var result = await this.LicenseRegistrationService.RegisterLicenseAsync( url );

        Assert.False( result.IsSuccess );
        Assert.Contains( expectedMessageSubstring, result.ErrorMessage, StringComparison.Ordinal );
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Acquiring a lease on demand.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests that acquiring a lease without a registered license server says so, rather than failing in a way that
    /// reads as a problem with a server.
    /// </summary>
    [Fact]
    public async Task AcquiringWithoutARegisteredServerSaysSo()
    {
        var result = await this.LicenseRegistrationService.AcquireLeaseAsync();

        Assert.False( result.IsSuccess );
        Assert.Contains( "No license server is registered", result.ErrorMessage, StringComparison.Ordinal );
    }

    /// <summary>
    /// Tests that the user is told which licence their license server grants them, and until when. Which product a
    /// server hands out is the first thing a developer wants to know and the first thing support asks.
    /// </summary>
    [Fact]
    public async Task AcquiringReportsTheLeasedLicense()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        var result = await this.LicenseRegistrationService.AcquireLeaseAsync();

        AssertSucceeded( result );
        Assert.Equal( server.Url, result.RegisteredLicense!.LicenseServerUrl );
        Assert.NotNull( result.RegisteredLicense.Lease );
    }

    /// <summary>
    /// Tests that acquiring costs what a build costs: a lease that is still valid and not yet due for renewal is used
    /// as it stands and nothing is sent, so running the command does not take a further seat.
    /// </summary>
    [Fact]
    public async Task AcquiringHonoursAValidLease()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        server.ClearRequests();

        AssertSucceeded( await this.LicenseRegistrationService.AcquireLeaseAsync() );

        server.AssertNotContacted();
    }

    /// <summary>
    /// Tests that the force flag renews a lease that is not due, which is what makes the command usable for
    /// diagnosing a server: without it, a machine that already holds a lease would never contact the server and the
    /// command would report nothing about it.
    /// </summary>
    [Fact]
    public async Task ForcedAcquisitionRenewsALeaseThatIsNotDue()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        server.ClearRequests();

        AssertSucceeded( await this.LicenseRegistrationService.AcquireLeaseAsync( true ) );

        server.AssertContacted();
    }

    /// <summary>
    /// Tests that acquiring stores the lease, so that the build which follows the command uses what the command
    /// obtained instead of contacting the server again.
    /// </summary>
    [Fact]
    public async Task ForcedAcquisitionStoresTheNewLease()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        this.Time.AddTime( TimeSpan.FromHours( 1 ) );

        AssertSucceeded( await this.LicenseRegistrationService.AcquireLeaseAsync( true ) );

        Assert.True( this.LeaseStore.TryGetLease( server.Url, out var lease ) );
        Assert.Equal( this.Time.UtcNow + server.LeaseDuration, lease.EndTime );
    }

    /// <summary>
    /// Tests that a user diagnosing a server that is down is told so, which is the answer they ran the command to
    /// get.
    /// </summary>
    [Fact]
    public async Task AcquiringFromAnUnreachableServerReportsTheFailure()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        server.FaultMode = LicenseServerFault.Unreachable;

        var result = await this.LicenseRegistrationService.AcquireLeaseAsync( true );

        Assert.False( result.IsSuccess );
        Assert.Contains( "Cannot get a lease", result.ErrorMessage, StringComparison.Ordinal );
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Consumption.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests that the lease is acquired once, while the consumer is built, and that consuming a requirement
    /// afterwards waits for nothing. The licences of a consumer are resolved by the time it exists, which is what
    /// keeps <see cref="ILicenseConsumer.TryConsume"/> synchronous on the critical path of a compilation.
    /// </summary>
    [Fact]
    public async Task ServerIsContactedOnceWhileTheConsumerIsBuilt()
    {
        var server = this.CreateServer();

        var consumer = await this.ConsumptionService.CreateConsumerAsync(
            new LicenseConsumptionOptions { ProjectLicenseKey = server.Url, IgnoredLicenseSources = LicenseSourceKind.UserProfile },
            this.Messages.Add );

        server.AssertContacted();

        Assert.True( consumer.TryConsume( LicenseRequirement.Any ) );
        Assert.True( consumer.TryConsume( LicenseRequirement.Any ) );

        server.AssertContacted();
    }

    /// <summary>
    /// Tests that a build which names no license server contacts none, which is the case of the overwhelming majority
    /// of builds and the reason a license server costs nothing to the customers who do not run one.
    /// </summary>
    [Fact]
    public async Task BuildWithoutAServerContactsNothing()
    {
        var server = this.CreateServer();

        var consumer = await this.ConsumptionService.CreateConsumerAsync(
            new LicenseConsumptionOptions { ProjectLicenseKey = LicenseKeyProvider.MetalamaProfessionalBusiness },
            this.Messages.Add );

        Assert.True( consumer.TryConsume( LicenseRequirement.Any ) );

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

        Assert.True( consumer.TryConsume( LicenseRequirement.Any ) );
        server.AssertNotContacted();
    }

    /// <summary>
    /// Tests that a developer whose license server is unreachable, and who holds no lease, is told that their build
    /// has no licence and why. Failing without naming the server would send them looking at their own project.
    /// </summary>
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
    /// Tests that a licence key is preferred to a license server, because the source of a key has a higher priority
    /// than the user profile, which is where a server is registered.
    /// </summary>
    /// <remarks>
    /// The server is still contacted. Every licence of a consumer is resolved while the consumer is built, whether or
    /// not a requirement ends up using it, exactly as a licence key is read whether or not it is used. What the
    /// priority decides is which licence satisfies the requirement, and therefore which one is audited and which
    /// ledger accounts for the build.
    /// </remarks>
    [Fact]
    public async Task ProjectLicenseKeyIsPreferredToAServer()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        var consumer = await this.ConsumptionService.CreateConsumerAsync(
            new LicenseConsumptionOptions { ProjectLicenseKey = LicenseKeyProvider.MetalamaProfessionalBusiness },
            this.Messages.Add );

        Assert.True(
            consumer.TryConsume(
                new DelegateLicenseRequirement( context => context.License.LicenseString == LicenseKeyProvider.MetalamaProfessionalBusiness ) ) );
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
    // An unattended process never leases.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Makes the current process unattended, as a build server is. It has to be done before the services are built,
    /// because the application information is read once.
    /// </summary>
    private void MakeTheProcessUnattended()
        => this.ApplicationInfo = new TestApplicationInfo(
            "License Server Test App",
            false,
            "2027.0.1",
            new DateTime( 2026, 1, 15, 0, 0, 0, DateTimeKind.Utc ) ) { IsUnattendedProcess = true };

    /// <summary>
    /// Tests the rule that matters most to the pool of a team: an unattended process is licensed by the unattended
    /// license, and never contacts the license server. A build server builds far more often than a developer, and a
    /// build server that leased would hold seats that the people who need them cannot get.
    /// </summary>
    [Fact]
    public async Task UnattendedLicenseWinsAndTheServerIsNeverContacted()
    {
        this.MakeTheProcessUnattended();

        var server = this.CreateServer();

        // Registering is an attended act, and it is what a developer does before handing the build over.
        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        this.LeaseStore.RemoveAllLeases();
        server.ClearRequests();

        // The service of this test base ignores the unattended source, so the sources are built by hand to have both.
        var service = new LicenseConsumptionService(
            this.ServiceProvider,
            [new UnattendedLicenseSource( this.ServiceProvider ), new UserProfileLicenseSource( this.ServiceProvider )] );

        var consumer = await service.CreateConsumerAsync( null, this.Messages.Add );

        Assert.True( consumer.TryConsume( new DelegateLicenseRequirement( context => context.License.LicenseType == SharpCrafters.Backstage.Licensing.LicenseType.Unattended ) ) );
        server.AssertNotContacted();
    }

    /// <summary>
    /// Tests that the rule holds even when there is no unattended license to fall back on, because it is about what an
    /// unattended process may take rather than about what it already has.
    /// </summary>
    [Fact]
    public async Task UnattendedProcessDoesNotLeaseEvenWithoutAnUnattendedLicense()
    {
        this.MakeTheProcessUnattended();

        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        this.LeaseStore.RemoveAllLeases();
        server.ClearRequests();

        Assert.False( await this.TryConsumeAsync() );
        server.AssertNotContacted();
    }

    /// <summary>
    /// Tests that the rule is not reported. What the user configured is right, and the process it does not apply to is
    /// not the one whose user could act on a message; saying it would mean a warning on every build of a continuous
    /// integration server, for ever.
    /// </summary>
    [Fact]
    public async Task UnattendedProcessReportsNothingAboutTheServerItSkips()
    {
        this.MakeTheProcessUnattended();

        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        this.Messages.Clear();

        _ = await this.TryConsumeAsync();

        Assert.DoesNotContain( this.Messages, m => m.Text.Contains( server.Url, StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// Tests that a license server named by the build itself is skipped as well. The rule is about the process, not
    /// about where the URL came from.
    /// </summary>
    [Fact]
    public async Task UnattendedProcessSkipsAServerGivenByTheBuild()
    {
        this.MakeTheProcessUnattended();

        var server = this.CreateServer();

        var canConsume = await this.TryConsumeAsync(
            new LicenseConsumptionOptions { ProjectLicenseKey = server.Url, IgnoredLicenseSources = LicenseSourceKind.UserProfile } );

        Assert.False( canConsume );
        server.AssertNotContacted();
    }

    /// <summary>
    /// Tests that acquiring a lease by hand requires an interactive session, for the same reason: the command is how a
    /// person asks for a seat, and a build server must not be able to ask by running it.
    /// </summary>
    [Fact]
    public async Task AcquiringRequiresAnAttendedSession()
    {
        var server = this.CreateServer();

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        this.UserDeviceDetection.IsInteractiveDevice = false;
        server.ClearRequests();

        var result = await this.LicenseRegistrationService.AcquireLeaseAsync( true );

        Assert.False( result.IsSuccess );
        Assert.Contains( "interactive session", result.ErrorMessage, StringComparison.Ordinal );
        server.AssertNotContacted();
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

        var result = await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

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
    // The warning about an insecure server.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tests that an <c>http://</c> server warns and is used anyway. It is a warning and never an error: an
    /// organization that has always run its server over HTTP must not have its builds broken by an upgrade, and the
    /// developer whose build would break is not the person who can change the URL.
    /// </summary>
    [Fact]
    public async Task InsecureServerWarnsAndIsUsed()
    {
        var server = this.CreateServer( "http://license.test" );

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        Assert.True( await this.TryConsumeAsync() );
        Assert.Contains( this.Messages, m => m.Text.Contains( "cleartext", StringComparison.Ordinal ) );
        Assert.DoesNotContain( this.Messages, m => m.IsError && m.Text.Contains( "cleartext", StringComparison.Ordinal ) );
    }

    /// <summary>
    /// Tests that the warning names the configuration setting that silences it, because a warning a user cannot act
    /// upon is a warning they learn to ignore.
    /// </summary>
    [Fact]
    public async Task InsecureServerWarningNamesTheSetting()
    {
        var server = this.CreateServer( "http://license.test" );

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );
        _ = await this.TryConsumeAsync();

        Assert.Contains( this.Messages, m => m.Text.Contains( "allowInsecureLicenseServer", StringComparison.Ordinal ) );
    }

    /// <summary>
    /// Tests that the configuration setting silences the warning, which is the whole of the configuration this policy
    /// needs: the decision belongs to whoever registered the server and does not change from one build to the next.
    /// </summary>
    [Fact]
    public async Task InsecureServerWarningIsSilencedByTheConfiguration()
    {
        var server = this.CreateServer( "http://license.test" );

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        this.AllowInsecureLicenseServer();

        Assert.True( await this.TryConsumeAsync() );
        Assert.DoesNotContain( this.Messages, m => m.Text.Contains( "cleartext", StringComparison.Ordinal ) );
    }

    /// <summary>
    /// Tests that an <c>https://</c> server is never reported, whatever the setting.
    /// </summary>
    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task SecureServerIsNeverReported( bool allowInsecure )
    {
        var server = this.CreateServer( "https://license.test" );

        await this.LicenseRegistrationService.RegisterLicenseAsync( server.Url );

        if ( allowInsecure )
        {
            this.AllowInsecureLicenseServer();
        }

        Assert.True( await this.TryConsumeAsync() );
        Assert.DoesNotContain( this.Messages, m => m.Text.Contains( "cleartext", StringComparison.Ordinal ) );
    }

    /// <summary>
    /// Tests that a licence key never produces the warning.
    /// </summary>
    [Fact]
    public async Task LicenseKeyIsNeverReportedAsInsecure()
    {
        await this.LicenseRegistrationService.RegisterLicenseAsync( LicenseKeyProvider.MetalamaProfessionalBusiness );

        Assert.True( await this.TryConsumeAsync() );
        Assert.DoesNotContain( this.Messages, m => m.Text.Contains( "cleartext", StringComparison.Ordinal ) );
    }

    /// <summary>
    /// Tests what counts as a server that exposes the names of the user and of their machine: any address reached
    /// without encryption, including one on the machine itself, which may be a tunnel to somewhere else.
    /// </summary>
    [Theory]
    [InlineData( "http://license.test", true )]
    [InlineData( "http://localhost:8080", true )]
    [InlineData( "https://license.test", false )]
    [InlineData( "not a url", false )]
    [InlineData( null, false )]
    public void InsecurityIsDecidedByTheScheme( string? url, bool expectedInsecure )
    {
        Assert.Equal( expectedInsecure, LicenseServerUrl.IsInsecure( url ) );
    }
}
