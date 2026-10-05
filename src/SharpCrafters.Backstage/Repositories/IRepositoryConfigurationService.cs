// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Extensibility;

namespace SharpCrafters.Backstage.Repositories;

/// <summary>
/// Resolves the repository-scoped <c>metalama.json</c> configuration for a directory by walking up the directory tree
/// to the repository root (the directory containing <c>.git</c>). Only the <c>metalama.json</c> at the repository root
/// is read; a misplaced or malformed file is ignored and reported as a warning on the returned result. A product whose
/// file has another format, such as the <c>postsharp.config</c> of PostSharp, registers an
/// <see cref="IRepositoryConfigurationReader"/>, which reads the file instead.
/// </summary>
internal interface IRepositoryConfigurationService : IBackstageService
{
    /// <summary>
    /// Resolves the <c>metalama.json</c> applicable to <paramref name="startingDirectory"/> (typically a project or
    /// solution directory). The result is never <c>null</c>; when no applicable file is found, it carries an empty
    /// configuration and no warnings.
    /// </summary>
    RepositoryConfigurationResult GetRepositoryConfiguration( string startingDirectory );

    /// <summary>
    /// Gets the repository root for <paramref name="startingDirectory"/> — the nearest ancestor (including the directory
    /// itself) that contains a <c>.git</c> folder or file — or <c>null</c> when <paramref name="startingDirectory"/> is
    /// not inside a git repository (or is null/empty).
    /// </summary>
    string? GetRepositoryRoot( string? startingDirectory );
}
