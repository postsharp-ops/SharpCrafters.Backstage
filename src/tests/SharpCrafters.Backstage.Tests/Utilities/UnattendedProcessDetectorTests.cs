// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Metalama.Backstage;
using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Testing;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Utilities;

/// <summary>
/// Tests the real <see cref="UnattendedProcessDetector"/>, which the other tests replace with
/// <see cref="TestUnattendedProcessDetector"/>.
/// </summary>
/// <remarks>
/// The detector also reads <see cref="System.Environment.UserInteractive"/> and the session of the process, which a test
/// cannot control. A build agent that runs as a service is unattended by those two facts alone. The tests below
/// therefore assert an attended answer only when it comes from a check that runs before those two, and otherwise assert
/// an unattended answer, which is the same on every machine.
/// </remarks>
public sealed class UnattendedProcessDetectorTests : TestsBase
{
    private readonly TestContainerDetector _containerDetector = new();
    private readonly TestParentProcessSearch _parentProcessSearch = new();

    public UnattendedProcessDetectorTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
        => services
            .AddSingleton<IContainerDetector>( this._containerDetector )
            .AddSingleton<IParentProcessSearch>( this._parentProcessSearch );

    private UnattendedProcessDetector CreateInitializedDetector()
    {
        var detector = new UnattendedProcessDetector( this.ServiceProvider );
        detector.Initialize();

        return detector;
    }

    private bool IsCurrentProcessUnattended() => this.CreateInitializedDetector().IsCurrentProcessUnattended;

    private void SetForceAttended( string value )
    {
        // The property of TestsBase named UnattendedProcessDetector hides the type in an expression, so the type is qualified.
        var variableName = MetalamaProduct.Profile.GetEnvironmentVariableName( ProcessClassification.UnattendedProcessDetector.ForceAttendedVariableName );
        this.EnvironmentVariableProvider.Environment[variableName] = value;
    }

    // The property of TestsBase named RuntimeInformation hides the type, so the type is qualified.
    private static ProcessInfo CreateProcess( string name )
        => new(
            1234,
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform( OSPlatform.Windows )
                ? $"C:\\Programs\\{name}.exe"
                : $"/opt/programs/{name}" );

    [Fact]
    public void AWorkerProcessIsUnattendedWithoutExaminingTheProcess()
    {
        this.ApplicationInfo = new TestApplicationInfo { IsWorkerProcess = true };

        // The variable forces the attended answer for every other application.
        this.SetForceAttended( "true" );

        Assert.True( this.IsCurrentProcessUnattended() );
        Assert.Equal( 0, this._parentProcessSearch.CallCount );
    }

    [Fact]
    public void TheForceAttendedVariableIsReadFromTheEnvironmentVariableProvider()
    {
        // The variable is set only in the test provider, so the answer shows that the detector reads that provider.
        this.SetForceAttended( "true" );
        this._containerDetector.IsRunningInContainer = true;

        Assert.False( this.IsCurrentProcessUnattended() );
    }

    [Theory]
    [InlineData( "false" )]
    [InlineData( "" )]
    [InlineData( "yes" )]
    public void TheForceAttendedVariableForcesNothingUnlessItIsTrue( string value )
    {
        this.SetForceAttended( value );
        this._containerDetector.IsRunningInContainer = true;

        Assert.True( this.IsCurrentProcessUnattended() );
    }

    [Fact]
    public void AProcessStartedByTheServiceControlManagerIsUnattended()
    {
        this._parentProcessSearch.Processes.Add( CreateProcess( "services" ) );

        Assert.True( this.IsCurrentProcessUnattended() );
    }

    [Fact]
    public void AContinuousIntegrationServerIsRecognizedFromTheEnvironmentVariableProvider()
    {
        // GitHub Actions is recognized from a variable and a process. The variable is set only in the test provider.
        this.EnvironmentVariableProvider.Environment["GITHUB_ACTIONS"] = "true";
        this._parentProcessSearch.Processes.Add( CreateProcess( "Runner.Worker" ) );

        Assert.True( this.IsCurrentProcessUnattended() );
    }

    [Fact]
    public void TheAnswerCannotBeReadBeforeInitialization()
    {
        var detector = new UnattendedProcessDetector( this.ServiceProvider );

        Assert.Throws<InvalidOperationException>( () => detector.IsCurrentProcessUnattended );
    }

    [Fact]
    public void ASecondInitializationDoesNotDetectAgain()
    {
        // The container makes the answer unattended. A second detection would find no container and answer otherwise
        // on a developer machine, and would walk the parent processes again.
        this._containerDetector.IsRunningInContainer = true;
        var detector = this.CreateInitializedDetector();
        var callCount = this._parentProcessSearch.CallCount;

        this._containerDetector.IsRunningInContainer = false;
        detector.Initialize();

        Assert.True( detector.IsCurrentProcessUnattended );
        Assert.Equal( callCount, this._parentProcessSearch.CallCount );
    }

    private sealed class TestContainerDetector : IContainerDetector
    {
        public bool IsRunningInContainer { get; set; }
    }

    private sealed class TestParentProcessSearch : IParentProcessSearch
    {
        public List<ProcessInfo> Processes { get; } = [];

        public int CallCount { get; private set; }

        public IReadOnlyList<ProcessInfo> GetParentProcesses( ISet<string>? pivots = null )
        {
            this.CallCount++;

            return this.Processes;
        }
    }
}

/// <summary>
/// Tests that the services detect an unattended process when they are initialized, and not when the answer is first
/// needed. The detection walks the parent processes, and a parent process can exit before the current process ends.
/// </summary>
public sealed class UnattendedProcessDetectionTimingTests : TestsBase
{
    private readonly CountingUnattendedProcessDetector _detector = new();

    public UnattendedProcessDetectionTimingTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
        => services.AddSingleton<IUnattendedProcessDetector>( this._detector );

    [Fact]
    public void TheDetectorIsInitializedWithTheServices()
    {
        Assert.Equal( 0, this._detector.InitializationCount );

        this.EnsureServicesInitialized();

        Assert.Equal( 1, this._detector.InitializationCount );
    }

    private sealed class CountingUnattendedProcessDetector : IUnattendedProcessDetector
    {
        public int InitializationCount { get; private set; }

        public void Initialize() => this.InitializationCount++;

        public bool IsCurrentProcessUnattended => false;
    }
}
