// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage.Licensing;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Testing;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Consumption;

public sealed class NamespaceBoundLicenseTests : LicenseConsumptionServiceTestsBase
{
    public NamespaceBoundLicenseTests( ITestOutputHelper logger ) : base( logger ) { }

    [Theory]
    [InlineData( TestLicenseKeyProvider.NamespaceConstraint, true )]
    [InlineData( TestLicenseKeyProvider.NamespaceConstraint + ".Yes", true )]
    [InlineData( null, false )]
    [InlineData( "", false )]
    [InlineData( "AnotherNamespace", false )]
    public async Task TestWithProjectName( string? projectName, bool expectedResult )
    {
        var consumer = await this.CreateConsumptionService( LicenseKeyProvider.MetalamaProfessionalEvaluationNamespaceConstrained )
            .CreateConsumerAsync( new LicenseConsumptionOptions() { ProjectName = projectName } );

        Assert.Equal( expectedResult, consumer.TryConsume( new MetalamaExtensionLicenseRequirement( "<ComponentName>" ) ) );
    }

    /// <summary>
    /// A project may be known under more than one name, and any of them being inside the constrained namespace is
    /// enough. PostSharp offers the names of the repositories the project is in, so that a key sold to an
    /// organization covers a project whose assembly is named after something else.
    /// </summary>
    [Theory]
    [InlineData( TestLicenseKeyProvider.NamespaceConstraint, true )]
    [InlineData( TestLicenseKeyProvider.NamespaceConstraint + ".Yes", true )]
    [InlineData( "AnotherNamespace", false )]
    [InlineData( "", false )]
    public async Task AFurtherNameOfTheProjectIsEnough( string additionalProjectName, bool expectedResult )
    {
        var consumer = await this.CreateConsumptionService( LicenseKeyProvider.MetalamaProfessionalEvaluationNamespaceConstrained )
            .CreateConsumerAsync(
                new LicenseConsumptionOptions { ProjectName = "AProjectOfAnotherName", AdditionalProjectNamesProvider = () => [additionalProjectName] } );

        Assert.Equal( expectedResult, consumer.TryConsume( new MetalamaExtensionLicenseRequirement( "<ComponentName>" ) ) );
    }

    /// <summary>
    /// The further names are an addition and not a replacement: the name of the project still satisfies the
    /// constraint on its own.
    /// </summary>
    [Fact]
    public async Task TheNameOfTheProjectStillCounts()
    {
        var consumer = await this.CreateConsumptionService( LicenseKeyProvider.MetalamaProfessionalEvaluationNamespaceConstrained )
            .CreateConsumerAsync(
                new LicenseConsumptionOptions
                {
                    ProjectName = TestLicenseKeyProvider.NamespaceConstraint, AdditionalProjectNamesProvider = () => ["AnotherNamespace"]
                } );

        Assert.True( consumer.TryConsume( new MetalamaExtensionLicenseRequirement( "<ComponentName>" ) ) );
    }

    /// <summary>
    /// The delegate returns an <see cref="ImmutableArray{T}"/>. The default value of this type is not an empty array
    /// but an array with no storage, which throws when it is enumerated. A delegate that returns <c>default</c> is
    /// treated as offering no further name.
    /// </summary>
    [Fact]
    public async Task ADefaultListIsTreatedAsEmpty()
    {
        var consumer = await this.CreateConsumptionService( LicenseKeyProvider.MetalamaProfessionalEvaluationNamespaceConstrained )
            .CreateConsumerAsync(
                new LicenseConsumptionOptions
                {
                    ProjectName = TestLicenseKeyProvider.NamespaceConstraint, AdditionalProjectNamesProvider = () => default
                } );

        Assert.True( consumer.TryConsume( new MetalamaExtensionLicenseRequirement( "<ComponentName>" ) ) );
    }

    /// <summary>
    /// The consumer invokes the delegate at most once, however many requirements it evaluates against a
    /// namespace-constrained key.
    /// </summary>
    [Fact]
    public async Task TheFurtherNamesAreComputedOnce()
    {
        var invocations = 0;

        var consumer = await this.CreateConsumptionService( LicenseKeyProvider.MetalamaProfessionalEvaluationNamespaceConstrained )
            .CreateConsumerAsync(
                new LicenseConsumptionOptions
                {
                    ProjectName = "AProjectOfAnotherName",
                    AdditionalProjectNamesProvider = () =>
                    {
                        invocations++;

                        return [TestLicenseKeyProvider.NamespaceConstraint];
                    }
                } );

        Assert.True( consumer.TryConsume( new MetalamaExtensionLicenseRequirement( "<ComponentName>" ) ) );
        Assert.True( consumer.TryConsume( new MetalamaExtensionLicenseRequirement( "<ComponentName>" ) ) );
        Assert.Equal( 1, invocations );
    }

    /// <summary>
    /// A key that is not constrained to a namespace does not need the further names, so the consumer does not invoke
    /// the delegate.
    /// </summary>
    [Fact]
    public async Task AnUnconstrainedKeyDoesNotComputeTheFurtherNames()
    {
        var invocations = 0;

        var consumer = await this.CreateConsumptionService( LicenseKeyProvider.MetalamaProfessionalBusiness )
            .CreateConsumerAsync(
                new LicenseConsumptionOptions
                {
                    ProjectName = "AProjectOfAnotherName",
                    AdditionalProjectNamesProvider = () =>
                    {
                        invocations++;

                        return ImmutableArray<string>.Empty;
                    }
                } );

        Assert.True( consumer.TryConsume( new MetalamaExtensionLicenseRequirement( "<ComponentName>" ) ) );
        Assert.Equal( 0, invocations );
    }
}
