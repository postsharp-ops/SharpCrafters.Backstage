// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

namespace SharpCrafters.Aspects.Introspection.Query;

/// <summary>
/// The exit codes of the <c>introspect</c> commands (Documentation/future/Introspection-Cli.md of PostSharp, section 5.9).
/// </summary>
public static class IntrospectionExitCodes
{
    /// <summary>
    /// The query ran, including when its result is empty.
    /// </summary>
    public const int Success = 0;

    /// <summary>
    /// Another error, such as a scope that is not one project or a project that cannot be evaluated.
    /// </summary>
    public const int Error = 1;

    /// <summary>
    /// No file exists: the project was not built.
    /// </summary>
    public const int NoFile = 2;

    /// <summary>
    /// A selector matches several objects, which the message lists.
    /// </summary>
    public const int Ambiguous = 3;

    /// <summary>
    /// A condition, a selection or an ordering is invalid, or asks for data that the product does not provide.
    /// </summary>
    public const int InvalidQuery = 4;
}
