// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// An error of an <c>introspect</c> command, with its exit code.
/// </summary>
public sealed class IntrospectionCommandException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IntrospectionCommandException"/> class.
    /// </summary>
    public IntrospectionCommandException( string message, int exitCode ) : base( message )
    {
        this.ExitCode = exitCode;
    }

    /// <summary>
    /// Gets the exit code of the command (<see cref="IntrospectionExitCodes"/>).
    /// </summary>
    public int ExitCode { get; }
}
