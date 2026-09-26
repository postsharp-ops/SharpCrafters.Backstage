// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Maintenance;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Commands.Tests.Commands.Maintenance;

public sealed class ShutdownCommandTests : CommandsTestsBase
{
    public ShutdownCommandTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
    {
        base.ConfigureServices( services );

        services.AddProcessShutdownStrategy( _ => new StubStrategy( "Stub server", 1234 ) );
        services.AddProcessShutdownStrategy( _ => new StubStrategy( "Other stub server", 5678 ) );
    }

    /// <summary>
    /// Tests that the command runs every registered strategy and reports each process, with the exit code zero. A product
    /// and Backstage both contribute strategies, and a process that no strategy reports is left locking the files of the
    /// product without the user being told.
    /// </summary>
    [Fact]
    public async Task EveryStrategyIsRunAndReported()
    {
        await this.TestCommandAsync( "shutdown", "Stub server (process 1234): exited" );
        await this.TestCommandAsync( "shutdown", "Other stub server (process 5678): exited" );
    }

    /// <summary>
    /// Tests that <c>kill</c>, the earlier name of the command, still runs it. Scripts and documentation of earlier versions
    /// call it by that name.
    /// </summary>
    [Fact]
    public async Task KillIsTheSameCommand()
        => await this.TestCommandAsync( "kill", "Stub server (process 1234): exited" );

    /// <summary>
    /// Tests that <c>--all</c> reaches the strategies. Without it, the processes of the integrated development
    /// environments are never ended, and the user who asked for them would be left with their files locked.
    /// </summary>
    [Fact]
    public async Task AllReachesTheStrategies()
    {
        await this.TestCommandAsync( "shutdown", "Stub server (process 1234): exited", "ended" );
        await this.TestCommandAsync( "shutdown --all", "Stub server (process 1234): ended" );
    }

    private sealed class StubStrategy : IProcessShutdownStrategy
    {
        private readonly string _description;
        private readonly int _processId;

        public StubStrategy( string description, int processId )
        {
            this._description = description;
            this._processId = processId;
        }

        public IReadOnlyList<ProcessShutdownResult> ShutDownProcesses( ProcessShutdownOptions options )
            => [new ProcessShutdownResult( this._description, this._processId, options.All ? ProcessShutdownOutcome.Ended : ProcessShutdownOutcome.Exited )];
    }
}
