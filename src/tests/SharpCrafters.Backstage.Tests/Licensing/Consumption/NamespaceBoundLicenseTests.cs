// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using SharpCrafters.Backstage.Licensing.Consumption;
using SharpCrafters.Backstage.Testing;
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
                new LicenseConsumptionOptions { ProjectName = "AProjectOfAnotherName", AdditionalProjectNames = [additionalProjectName] } );

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
                    ProjectName = TestLicenseKeyProvider.NamespaceConstraint, AdditionalProjectNames = ["AnotherNamespace"]
                } );

        Assert.True( consumer.TryConsume( new MetalamaExtensionLicenseRequirement( "<ComponentName>" ) ) );
    }

    /// <summary>
    /// The property is an <see cref="System.Collections.Immutable.ImmutableArray{T}"/>, whose default value is not an
    /// empty array but an array with no storage, which throws when it is enumerated. An application that assigns
    /// <c>default</c> is treated as offering no further name.
    /// </summary>
    [Fact]
    public async Task ADefaultListIsTreatedAsEmpty()
    {
        var consumer = await this.CreateConsumptionService( LicenseKeyProvider.MetalamaProfessionalEvaluationNamespaceConstrained )
            .CreateConsumerAsync(
                new LicenseConsumptionOptions { ProjectName = TestLicenseKeyProvider.NamespaceConstraint, AdditionalProjectNames = default } );

        Assert.True( consumer.TryConsume( new MetalamaExtensionLicenseRequirement( "<ComponentName>" ) ) );
    }
}
