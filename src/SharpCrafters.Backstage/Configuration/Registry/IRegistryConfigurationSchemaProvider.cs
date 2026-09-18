// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Configuration.Registry;

/// <summary>
/// Provides the schemas that map the configuration objects a product keeps in the Windows registry.
/// </summary>
/// <remarks>
/// A product registers an implementation to share a configuration object with its earlier versions, which is what
/// PostSharp does. A product that registers none keeps every configuration in a file, and so does every product away
/// from Windows, where there is nothing to share.
/// </remarks>
[PublicAPI]
public interface IRegistryConfigurationSchemaProvider : IBackstageService
{
    /// <summary>
    /// Gets the schemas, one per configuration object that lives in the registry.
    /// </summary>
    IEnumerable<IRegistryConfigurationSchema> GetSchemas();
}
