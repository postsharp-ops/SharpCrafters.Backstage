// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Metalama.Backstage.Licensing.Consumption;
using Metalama.Backstage.Licensing.Consumption.Requirements;
using Metalama.Backstage.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Metalama.Backstage.Tests.Licensing.Consumption;

public sealed class NamespaceBoundLicenseTests : LicenseConsumptionServiceTestsBase
{
    public NamespaceBoundLicenseTests( ITestOutputHelper logger ) : base( logger ) { }

    [Theory]
    [InlineData(TestLicenseKeyProvider.NamespaceConstraint, true )]
    [InlineData(TestLicenseKeyProvider.NamespaceConstraint + ".Yes", true )]
    [InlineData(null, false )]
    [InlineData("", false )]
    [InlineData("AnotherNamespace", false )]
    public void TestWithProjectName( string? projectName, bool expectedResult )
    {
        var consumer = this.CreateConsumptionService( LicenseKeyProvider.MetalamaProfessionalEvaluationNamespaceConstrained ).CreateConsumer( new LicenseConsumptionOptions() { ProjectName = projectName });
        Assert.Equal( expectedResult, consumer.TryConsume( new MetalamaExtensionLicenseRequirement( "<ComponentName>" ) ) );   
    }
}