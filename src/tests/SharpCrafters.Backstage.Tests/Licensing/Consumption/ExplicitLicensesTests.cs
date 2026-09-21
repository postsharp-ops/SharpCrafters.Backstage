// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Microsoft.Extensions.DependencyInjection;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Licensing.Consumption.Sources;
using SharpCrafters.Backstage.Tests.Extensibility;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Consumption;

/// <summary>
/// The licenses that an application supplies for one project when it reads them from more than one place, and the
/// description that names the place each one came from.
/// </summary>
/// <remarks>
/// What is under test is which license is considered, in which order, and what a message says. The license string of a
/// source that cannot be read must never appear in a message, because an application typically takes it from a secret
/// of a continuous integration server and the message goes to a build log.
/// </remarks>
public sealed class ExplicitLicensesTests : LicensingTestsBase
{
    private static string FirstLicense => LicenseKeyProvider.MetalamaProfessionalBusiness;

    private static string SecondLicense => LicenseKeyProvider.MetalamaProfessionalPersonal;

    private const string _invalidLicense = "this-is-not-a-license-key";

    public ExplicitLicensesTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services ) { }

    /// <summary>
    /// Creates a consumer over the given options and no license source of its own, so that the licenses under test are
    /// the only ones it has.
    /// </summary>
    private async Task<ILicenseConsumer> CreateConsumerAsync( LicenseConsumptionOptions options, Action<LicensingMessage>? reportMessage = null )
    {
        var serviceCollection = this.CloneServiceCollection();

        new ServiceCollectionBuilder( serviceCollection ).AddSingleton<ILicenseConsumptionService>(
            sp => new LicenseConsumptionService( sp, new List<ILicenseSource>() ) );

        var service = serviceCollection.BuildServiceProvider().GetRequiredBackstageService<ILicenseConsumptionService>();

        return await service.CreateConsumerAsync( options, reportMessage );
    }

    private static LicenseConsumptionOptions Explicitly( params (string LicenseString, string Description)[] licenses )
        => new() { ExplicitLicenses = [..licenses.Select( l => new ExplicitLicense( l.LicenseString, l.Description ) )] };

    /// <summary>
    /// Gets the license strings the consumer holds, in the order in which it considers them. A requirement that is
    /// eligible for nothing is offered every license, so the predicate sees them all.
    /// </summary>
    private static IReadOnlyList<string> GetConsideredLicenses( ILicenseConsumer consumer )
    {
        var considered = new List<string>();

        consumer.TryConsume(
            new DelegateLicenseRequirement(
                context =>
                {
                    considered.Add( context.License.LicenseString ?? "" );

                    return false;
                } ),
            showsToastNotification: false );

        return considered;
    }

    /// <summary>
    /// A license the application supplied is used, which is what the list is for.
    /// </summary>
    [Fact]
    public async Task AnExplicitLicenseIsConsumed()
    {
        var consumer = await this.CreateConsumerAsync( Explicitly( (FirstLicense, "the first place") ) );

        Assert.True( consumer.TryConsume( new DelegateLicenseRequirement( context => context.License.LicenseString == FirstLicense ) ) );
    }

    /// <summary>
    /// The order of the list is the order in which the licenses are considered. The application decides that order and
    /// nothing reorders it, because the application is what knows which of its places wins.
    /// </summary>
    [Fact]
    public async Task TheOrderOfTheListIsKept()
    {
        var consumer = await this.CreateConsumerAsync( Explicitly( (FirstLicense, "the first place"), (SecondLicense, "the second place") ) );

        Assert.Equal( new[] { FirstLicense, SecondLicense }, GetConsideredLicenses( consumer ) );
    }

    /// <summary>
    /// <see cref="LicenseConsumptionOptions.ProjectLicenseKey"/> is the short form of the list, so it comes after the
    /// list rather than instead of it. An application moving from one to the other may set both.
    /// </summary>
    [Fact]
    public async Task TheProjectLicenseKeyComesAfterTheList()
    {
        var options = Explicitly( (FirstLicense, "the first place") ) with { ProjectLicenseKey = SecondLicense };

        var consumer = await this.CreateConsumerAsync( options );

        Assert.Equal( new[] { FirstLicense, SecondLicense }, GetConsideredLicenses( consumer ) );
    }

    /// <summary>
    /// The message about a license that cannot be read names the place the application read it from, and does not
    /// quote the license string.
    /// </summary>
    [Fact]
    public async Task AMessageNamesTheDescriptionAndNotTheLicenseString()
    {
        var messages = new List<string>();

        await this.CreateConsumerAsync(
            Explicitly( (_invalidLicense, "the LicenseKey element of the file 'x.config'") ),
            m => messages.Add( m.Text ) );

        var message = Assert.Single( messages );
        Assert.Contains( "the LicenseKey element of the file 'x.config'", message, StringComparison.Ordinal );
        Assert.DoesNotContain( _invalidLicense, message, StringComparison.Ordinal );
    }

    /// <summary>
    /// Every license of the list is read, so one that cannot be used does not hide the ones after it.
    /// </summary>
    [Fact]
    public async Task AnUnusableLicenseDoesNotHideTheNextOne()
    {
        var consumer = await this.CreateConsumerAsync(
            Explicitly( (_invalidLicense, "the first place"), (SecondLicense, "the second place") ),
            _ => { } );

        Assert.Equal( new[] { SecondLicense }, GetConsideredLicenses( consumer ) );
    }

    /// <summary>
    /// What the consumer reports it holds is what it considers, in the same order. An application shows this to the
    /// user when it has to say which license it found, so a list that did not match what a requirement is checked
    /// against would name a license that had nothing to do with the failure.
    /// </summary>
    [Fact]
    public async Task TheReportedLicensesAreTheOnesConsidered()
    {
        var consumer = await this.CreateConsumerAsync(
            Explicitly( (_invalidLicense, "the first place"), (FirstLicense, "the second place"), (SecondLicense, "the third place") ),
            _ => { } );

        Assert.Equal( GetConsideredLicenses( consumer ), consumer.Licenses.Select( l => l.LicenseString ) );
        Assert.Equal( new[] { FirstLicense, SecondLicense }, consumer.Licenses.Select( l => l.LicenseString ) );
    }

    /// <summary>
    /// The property is an <see cref="ImmutableArray{T}"/>, whose default value is not an empty array but an array with
    /// no storage, which throws when it is enumerated. An application that assigns <c>default</c> therefore gets no
    /// license rather than an exception.
    /// </summary>
    [Fact]
    public async Task ADefaultListIsTreatedAsEmpty()
    {
        var consumer = await this.CreateConsumerAsync( new LicenseConsumptionOptions { ExplicitLicenses = default } );

        Assert.Empty( GetConsideredLicenses( consumer ) );
    }

    /// <summary>
    /// The message about a requirement that none of the licenses satisfies names them by their display name, which is
    /// the product and the identifier, and never by their string.
    /// </summary>
    /// <remarks>
    /// This message is reported as a compiler diagnostic, so it reaches build logs, continuous integration output and
    /// bug reports. A license key is a credential, and an application typically takes it from a secret of a build
    /// server, so naming the keys here would disclose every key the user holds to everyone who can read a build log.
    /// </remarks>
    [Fact]
    public async Task AMessageAboutAnUnsatisfiedRequirementNamesTheLicensesAndNotTheirStrings()
    {
        var messages = new List<string>();

        var consumer = await this.CreateConsumerAsync( Explicitly( (FirstLicense, "the first place"), (SecondLicense, "the second place") ) );

        Assert.False(
            consumer.TryConsume(
                new DelegateLicenseRequirement( _ => false ),
                m => messages.Add( m.Text ),
                showsToastNotification: false ) );

        var message = Assert.Single( messages );

        foreach ( var license in consumer.Licenses )
        {
            Assert.Contains( license.DisplayName, message, StringComparison.Ordinal );
            Assert.DoesNotContain( license.LicenseString!, message, StringComparison.Ordinal );
        }
    }
}
