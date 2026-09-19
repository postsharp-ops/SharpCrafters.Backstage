// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing.Registration;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Licensing.Registration;

/// <summary>
/// Tests the editions that can be registered as things stand, which is what the setup pages offer.
/// </summary>
/// <remarks>
/// The trial is the only edition whose availability moves, so it is what these tests watch. The pages read this list
/// and nothing else, so an edition missing from it is an edition the user cannot reach.
/// </remarks>
public sealed class AvailableEditionsTests : LicensingTestsBase
{
    public AvailableEditionsTests( ITestOutputHelper logger ) : base( logger ) { }

    private bool IsTrialAvailable
        => this.LicenseRegistrationService.AvailableEditions.Any( e => e.Kind == SelfRegisteredEditionKind.Trial );

    /// <summary>
    /// On a machine where nothing has been registered, every edition the family declares is available.
    /// </summary>
    [Fact]
    public void EveryEditionIsAvailableInAnEmptyEnvironment()
        => Assert.Equal(
            this.Catalog.SelfRegisteredEditions.Select( e => e.Alias ).ToArray(),
            this.LicenseRegistrationService.AvailableEditions.Select( e => e.Alias ).ToArray() );

    /// <summary>
    /// The trial disappears while one is running, so the pages do not offer to start a second.
    /// </summary>
    [Fact]
    public void TheTrialIsWithdrawnWhileOneIsRunning()
    {
        Assert.True( this.IsTrialAvailable );
        Assert.True( this.RegisterTrial().IsSuccess );

        Assert.False( this.IsTrialAvailable );
    }

    /// <summary>
    /// It stays withdrawn once the trial has expired, until the cool-off period has elapsed as well, and comes back
    /// afterwards.
    /// </summary>
    [Fact]
    public void TheTrialComesBackOnlyAfterTheCoolOffPeriod()
    {
        Assert.True( this.RegisterTrial().IsSuccess );

        // The trial itself has run out, but the period during which a new one cannot be started has not.
        this.Time.AddTime( LicensingConstants.EvaluationPeriod );
        Assert.False( this.IsTrialAvailable );

        this.Time.AddTime( LicensingConstants.NoEvaluationPeriod );
        Assert.True( this.IsTrialAvailable );
    }

    /// <summary>
    /// The reason an edition is withdrawn is reported to whoever asks for it by name, so that the command line says
    /// why rather than claiming the edition does not exist.
    /// </summary>
    [Fact]
    public void RegisteringAWithdrawnEditionSaysWhy()
    {
        Assert.True( this.RegisterTrial().IsSuccess );

        var result = this.RegisterTrial();

        Assert.False( result.IsSuccess );
        Assert.Equal( "The evaluation license is already active.", result.ErrorMessage );
    }
}
