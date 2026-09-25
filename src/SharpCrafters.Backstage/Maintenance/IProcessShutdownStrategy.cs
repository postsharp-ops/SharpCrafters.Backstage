// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// Stops one kind of process that keeps files of the product locked after a build, for the <c>shutdown</c>,
/// <c>kill</c> and <c>cleanup</c> commands.
/// </summary>
/// <remarks>
/// <para>
/// Several strategies contribute to one command, and each knows how to find and stop its own processes: Backstage
/// contributes the .NET build servers and its own tool applications, and a product contributes the processes that
/// only it knows, for instance the pipe servers of PostSharp. A strategy is registered with
/// <see cref="ProcessShutdownServiceExtensions.AddProcessShutdownStrategy"/>, and the host of the commands registers
/// its own through the <c>registerServices</c> delegate of <c>BackstageCommandOptions</c>.
/// </para>
/// <para>
/// The commands resolve every registered strategy, so the service provider of the host must keep several
/// registrations of this type. The provider of the commands, built on <c>Microsoft.Extensions.DependencyInjection</c>,
/// does; <see cref="SimpleServiceProviderBuilder"/> keeps only the last one.
/// </para>
/// </remarks>
[PublicAPI]
public interface IProcessShutdownStrategy : IBackstageService
{
    /// <summary>
    /// Finds the processes of this strategy, asks them to exit, waits for them, and ends the ones that remain when
    /// <see cref="ProcessShutdownOptions.Force"/> is set.
    /// </summary>
    /// <returns>One result per process found, in no particular order. The list is empty when none was running.</returns>
    IReadOnlyList<ProcessShutdownResult> ShutDownProcesses( ProcessShutdownOptions options );
}
