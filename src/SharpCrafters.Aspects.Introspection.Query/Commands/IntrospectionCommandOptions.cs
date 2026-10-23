// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;
using System.IO;

namespace SharpCrafters.Aspects.Introspection.Query.Commands;

/// <summary>
/// What the tool of a product gives the <c>introspect</c> commands: its provider, and where the commands write.
/// </summary>
public sealed class IntrospectionCommandOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IntrospectionCommandOptions"/> class.
    /// </summary>
    /// <param name="provider">The provider of the product.</param>
    /// <param name="standardOutput">The standard output, by default <see cref="Console.Out"/>.</param>
    /// <param name="errorOutput">The standard error, by default <see cref="Console.Error"/>.</param>
    /// <param name="workingDirectory">The directory against which the scope and the paths are resolved, by default the current
    /// directory.</param>
    /// <param name="isOutputTerminal">Whether the standard output is a terminal, which colors the JSON output; by default, whether
    /// <see cref="Console.Out"/> is not redirected, when <paramref name="standardOutput"/> is not given.</param>
    /// <param name="getEnvironmentVariable">Reads an environment variable, such as <c>NO_COLOR</c>; by default
    /// <see cref="Environment.GetEnvironmentVariable(string)"/>.</param>
    public IntrospectionCommandOptions(
        IntrospectionProvider provider,
        TextWriter? standardOutput = null,
        TextWriter? errorOutput = null,
        string? workingDirectory = null,
        bool? isOutputTerminal = null,
        Func<string, string?>? getEnvironmentVariable = null )
    {
        this.Provider = provider;
        this.StandardOutput = standardOutput ?? Console.Out;
        this.ErrorOutput = errorOutput ?? Console.Error;
        this.WorkingDirectory = Path.GetFullPath( workingDirectory ?? Environment.CurrentDirectory );
        this.IsOutputTerminal = isOutputTerminal ?? (standardOutput == null && !Console.IsOutputRedirected);
        this.GetEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    /// <summary>
    /// Gets the provider of the product.
    /// </summary>
    public IntrospectionProvider Provider { get; }

    /// <summary>
    /// Gets the standard output.
    /// </summary>
    public TextWriter StandardOutput { get; }

    /// <summary>
    /// Gets the standard error, where the warnings and the errors go.
    /// </summary>
    public TextWriter ErrorOutput { get; }

    /// <summary>
    /// Gets the directory against which the scope and the paths are resolved.
    /// </summary>
    public string WorkingDirectory { get; }

    /// <summary>
    /// Gets a value indicating whether the standard output is a terminal.
    /// </summary>
    public bool IsOutputTerminal { get; }

    /// <summary>
    /// Gets the function that reads an environment variable.
    /// </summary>
    public Func<string, string?> GetEnvironmentVariable { get; }
}
