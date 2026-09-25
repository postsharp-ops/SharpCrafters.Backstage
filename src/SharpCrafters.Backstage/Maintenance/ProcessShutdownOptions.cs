// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;

namespace SharpCrafters.Backstage.Maintenance;

/// <summary>
/// How an <see cref="IProcessShutdownStrategy"/> stops its processes.
/// </summary>
/// <param name="Force">
/// <c>true</c> to cancel the work of the processes instead of letting it end, and to end the processes that have not exited
/// when <paramref name="Timeout"/> has elapsed. <c>false</c> to ask the processes to exit and to report the ones that
/// remain.
/// </param>
/// <param name="Timeout">How long the strategy waits for its processes to exit.</param>
[PublicAPI]
public sealed record ProcessShutdownOptions( bool Force, TimeSpan Timeout );
