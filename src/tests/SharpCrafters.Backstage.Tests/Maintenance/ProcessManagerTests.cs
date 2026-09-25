// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;
using SharpCrafters.Backstage.Maintenance;
using SharpCrafters.Backstage.ProcessClassification;
using SharpCrafters.Backstage.Testing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SharpCrafters.Backstage.Tests.Maintenance;

/// <summary>
/// Tests that the process manager never stops the current process or one of its parents, which would end the operation
/// that asked for the clean-up.
/// </summary>
public sealed class ProcessManagerTests : TestsBase
{
    private const int _parentProcessId = 1001;
    private const int _grandparentProcessId = 1002;
    private const int _otherProcessId = 2001;

    private readonly TestParentProcessSearch _parentProcessSearch = new();

    public ProcessManagerTests( ITestOutputHelper logger ) : base( logger ) { }

    protected override void ConfigureServices( ServiceProviderBuilder services )
        => services.AddSingleton<IParentProcessSearch>( this._parentProcessSearch );

    private static int CurrentProcessId => Process.GetCurrentProcess().Id;

    // The implementation of Windows adds nothing to the base class, and it constructs on every operating system.
    private List<int> Exclude( params int[] processIds )
        => new WindowsProcessManager( this.ServiceProvider ).ExcludeCurrentProcessAndParents( processIds, id => id ).ToList();

    [Fact]
    public void TheCurrentProcessAndItsParentsAreNotStopped()
    {
        this._parentProcessSearch.Processes.Add( new ProcessInfo( _parentProcessId, "dotnet" ) );
        this._parentProcessSearch.Processes.Add( new ProcessInfo( _grandparentProcessId, "msbuild" ) );

        var remaining = this.Exclude( CurrentProcessId, _parentProcessId, _otherProcessId, _grandparentProcessId );

        Assert.Equal( new[] { _otherProcessId }, remaining );
    }

    [Fact]
    public void TheCurrentProcessIsNotStoppedWhenTheParentsCannotBeDetermined()
    {
        this._parentProcessSearch.Exception = new InvalidOperationException( "The parent processes cannot be read." );

        var remaining = this.Exclude( CurrentProcessId, _parentProcessId, _otherProcessId );

        Assert.Equal( new[] { _parentProcessId, _otherProcessId }, remaining );
    }

    private sealed class TestParentProcessSearch : IParentProcessSearch
    {
        public List<ProcessInfo> Processes { get; } = [];

        public Exception? Exception { get; set; }

        public IReadOnlyList<ProcessInfo> GetParentProcesses( ISet<string>? pivots = null )
            => this.Exception == null ? this.Processes : throw this.Exception;
    }
}
