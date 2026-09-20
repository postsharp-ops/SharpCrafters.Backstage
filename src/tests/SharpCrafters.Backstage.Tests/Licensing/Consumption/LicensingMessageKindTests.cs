// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Extensions.DependencyInjection;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Testing;
using SharpCrafters.Backstage.Tests.Extensibility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Consumption;

/// <summary>
/// What each licensing message is about, which is what an application maps to a diagnostic of its own.
/// </summary>
/// <remarks>
/// A wrong kind is silent: the message still says the right thing, and the application files it under the wrong
/// diagnostic, so a user who suppressed one situation suppresses another. Each case here is one situation a user can
/// be in, and the assertion is the kind and not the text, because the text is free to change.
/// </remarks>
public sealed class LicensingMessageKindTests : LicensingTestsBase
{
    /// <summary>
    /// A production license key that has been revoked. It is a real key rather than a generated one, because the rule
    /// applies to keys signed by a production authority, which the test authority is not.
    /// </summary>
    // ReSharper disable once StringLiteralTypo
    private const string _revokedLicense =
        "1-ZEQQQQQQZTQEQCRCE4UW3UFEB4URXMHRB8KQBJJSB64LX7EAEJZWKEM8SCXJK6KJLFD92CAJFQKCGC67A9NVYA2JGNEHLB8QQG4JAF94J58KUJQZW8ZQQDTFJJPA";

    public LicensingMessageKindTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services ) { }

    private ILicenseConsumptionService CreateService()
    {
        var serviceCollection = this.CloneServiceCollection();

        new ServiceCollectionBuilder( serviceCollection ).AddSingleton<ILicenseConsumptionService>(
            sp => new LicenseConsumptionService( sp, new List<ILicenseSource>() ) );

        return serviceCollection.BuildServiceProvider().GetRequiredBackstageService<ILicenseConsumptionService>();
    }

    /// <summary>
    /// Creates a consumer over the given license strings and returns the messages that creating it produced.
    /// </summary>
    private async Task<(ILicenseConsumer Consumer, IReadOnlyList<LicensingMessage> Messages)> CreateConsumerAsync(
        string? licenseString = null,
        string projectName = "AProject" )
    {
        var messages = new List<LicensingMessage>();

        var options = new LicenseConsumptionOptions
        {
            ProjectName = projectName,
            ExplicitLicenses = licenseString == null ? [] : [new ExplicitLicense( licenseString, "the place under test" )]
        };

        var consumer = await this.CreateService().CreateConsumerAsync( options, messages.Add );

        foreach ( var message in messages )
        {
            this.Logger.WriteLine( $"{message.Kind}: {message}" );
        }

        return (consumer, messages);
    }

    /// <summary>
    /// Asserts that creating a consumer over a license string produces one message of the given kind.
    /// </summary>
    private async Task AssertCreationReportsAsync( string licenseString, LicensingMessageKind expectedKind )
    {
        var (_, messages) = await this.CreateConsumerAsync( licenseString );

        Assert.Equal( expectedKind, Assert.Single( messages ).Kind );
    }

    /// <summary>
    /// A string that is not a license key at all. The source refuses it before the factory sees it, so the message
    /// names the place it came from and not the string.
    /// </summary>
    [Fact]
    public Task AStringThatIsNotALicenseKey() => this.AssertCreationReportsAsync( "SomeInvalidLicenseString", LicensingMessageKind.InvalidLicenseKey );

    /// <summary>
    /// A key that reads as a license key and whose signature does not verify. It is unusable whatever it is used for,
    /// which is the same kind as a string that is not a key.
    /// </summary>
    [Fact]
    public Task AKeyWhoseSignatureDoesNotVerify()
        => this.AssertCreationReportsAsync( LicenseKeyProvider.MetalamaProfessionalBusinessUnsigned, LicensingMessageKind.InvalidLicenseKey );

    /// <summary>
    /// A key whose subscription ended before the version being built with was released. The key was valid and the
    /// user renews it, which is why it is not reported as an invalid key.
    /// </summary>
    [Fact]
    public Task AKeyWhoseSubscriptionEnded()
    {
        // The subscription is compared against the build date of the product, so the product has to have been built
        // after the subscription ended for the key to be the expired one rather than a usable one.
        ((TestApplicationInfo) this.ApplicationInfo).BuildDate = LicenseKeyProvider.ExpiredSubscriptionEndDate.AddDays( 1 );

        return this.AssertCreationReportsAsync( LicenseKeyProvider.ExpiredSubscription, LicensingMessageKind.Expired );
    }

    /// <summary>
    /// A key that has been revoked. Nothing the user does to the key makes it usable again, so it is neither an
    /// invalid key nor an expired one.
    /// </summary>
    [Fact]
    public Task ARevokedKey() => this.AssertCreationReportsAsync( _revokedLicense, LicensingMessageKind.Revoked );

    /// <summary>
    /// A key of another family of products. It is a valid key that the user holds legitimately, and it licenses
    /// something else.
    /// </summary>
    [Fact]
    public Task AKeyOfAnotherProductFamily()
        => this.AssertCreationReportsAsync( LicenseKeyProvider.PostSharpCaching, LicensingMessageKind.WrongProductFamily );

    /// <summary>
    /// A key constrained to a namespace, used on a project that is not inside it. The key is usable, so nothing is
    /// reported while the consumer is created; the message comes when a requirement is checked against it.
    /// </summary>
    [Fact]
    public async Task AKeyConstrainedToAnotherNamespace()
    {
        var (consumer, creationMessages) = await this.CreateConsumerAsync(
            LicenseKeyProvider.MetalamaProfessionalEvaluationNamespaceConstrained,
            "AProjectOutsideTheConstraint" );

        Assert.Empty( creationMessages );

        var messages = new List<LicensingMessage>();
        Assert.False( consumer.TryConsume( new DelegateLicenseRequirement( _ => true ), messages.Add, showsToastNotification: false ) );

        Assert.Contains( messages, m => m.Kind == LicensingMessageKind.NamespaceMismatch );
    }

    /// <summary>
    /// A feature requires a license and the user holds none, so they are asked to register one.
    /// </summary>
    [Fact]
    public async Task ARequirementWithNoLicenseAtAll()
    {
        var (consumer, _) = await this.CreateConsumerAsync();

        var messages = new List<LicensingMessage>();
        Assert.False( consumer.TryConsume( new DelegateLicenseRequirement( _ => false ), messages.Add, showsToastNotification: false ) );

        Assert.Equal( LicensingMessageKind.NoLicense, Assert.Single( messages ).Kind );
    }

    /// <summary>
    /// A feature requires a license that the one the user holds does not grant. They are told which products grant
    /// it, and not asked to register a license they already have.
    /// </summary>
    [Fact]
    public async Task ARequirementThatTheLicenseDoesNotGrant()
    {
        var (consumer, creationMessages) = await this.CreateConsumerAsync( LicenseKeyProvider.MetalamaProfessionalBusiness );

        Assert.Empty( creationMessages );

        var messages = new List<LicensingMessage>();
        Assert.False( consumer.TryConsume( new DelegateLicenseRequirement( _ => false ), messages.Add, showsToastNotification: false ) );

        Assert.Equal( LicensingMessageKind.RequirementNotSatisfied, Assert.Single( messages ).Kind );
    }

    /// <summary>
    /// A license server addressed over HTTP. The URL is used, because refusing it would fail a build over a
    /// deployment the developer did not choose, so what the user gets is a message of its own kind.
    /// </summary>
    [Fact]
    public async Task ALicenseServerReachedOverHttp()
    {
        var (_, messages) = await this.CreateConsumerAsync( "http://licenses.example.com/" );

        Assert.Contains( messages, m => m.Kind == LicensingMessageKind.InsecureLicenseServer );
    }

    /// <summary>
    /// A valid key produces no message at all, which is what makes the kinds above worth asserting: a message is
    /// something the user is meant to act on.
    /// </summary>
    [Fact]
    public async Task AValidKeyIsSilent()
    {
        var (consumer, messages) = await this.CreateConsumerAsync( LicenseKeyProvider.MetalamaProfessionalBusiness );

        Assert.Empty( messages );
        Assert.Single( consumer.Licenses );
    }

    /// <summary>
    /// Every kind other than the general one is produced by something. A kind that nothing sets is a diagnostic an
    /// application maps and never reports, which is worse than not having the kind.
    /// </summary>
    /// <remarks>
    /// Every kind is produced by a test of this fixture except the lease failure, which needs a license server that
    /// answers and is therefore asserted by <c>LicenseServerEndToEndTests</c>, where such a server exists.
    /// </remarks>
    [Fact]
    public void EveryKindIsCoveredSomewhere()
    {
        var covered = new[]
        {
            LicensingMessageKind.InvalidLicenseKey, LicensingMessageKind.Expired, LicensingMessageKind.Revoked,
            LicensingMessageKind.WrongProductFamily, LicensingMessageKind.NamespaceMismatch, LicensingMessageKind.InsecureLicenseServer,
            LicensingMessageKind.RequirementNotSatisfied, LicensingMessageKind.NoLicense,

            // Asserted by LicenseServerEndToEndTests.
            LicensingMessageKind.LicenseServerLeaseFailed
        };

        var uncovered = Enum.GetValues( typeof(LicensingMessageKind) )
            .Cast<LicensingMessageKind>()
            .Where( k => k != LicensingMessageKind.Generic )
            .Except( covered )
            .ToList();

        Assert.True( uncovered.Count == 0, "These kinds are produced by nothing that any test asserts: " + string.Join( ", ", uncovered ) );
    }
}
