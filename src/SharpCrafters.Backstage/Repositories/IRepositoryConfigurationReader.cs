// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Application;
using SharpCrafters.Backstage.Extensibility;

namespace SharpCrafters.Backstage.Repositories;

/// <summary>
/// Reads the repository configuration file of a product, the file named by
/// <see cref="ProductProfile.RepositoryConfigurationFileName"/>. A product registers an implementation in
/// <see cref="BackstageProduct.RegisterServices"/> when its file is not a JSON file in the format of
/// <c>metalama.json</c>. Without an implementation, the file is read as such a JSON file.
/// </summary>
[PublicAPI]
public interface IRepositoryConfigurationReader : IBackstageService
{
    /// <summary>
    /// Gets a value indicating whether the product also uses a file of this name below the repository root, for another
    /// purpose. When <c>true</c>, a file below the root is reported only when it declares a repository setting, which
    /// is read only at the root. When <c>false</c>, any file of this name below the root is reported as misplaced.
    /// </summary>
    bool IsFileUsedBelowRoot { get; }

    /// <summary>
    /// Reads the repository settings from the text of a file.
    /// </summary>
    /// <param name="filePath">The path of the file, for the messages.</param>
    /// <param name="text">The text of the file.</param>
    /// <returns>The settings of the file, or the reasons why the file is ignored.</returns>
    RepositoryConfigurationReadResult Read( string filePath, string text );
}
