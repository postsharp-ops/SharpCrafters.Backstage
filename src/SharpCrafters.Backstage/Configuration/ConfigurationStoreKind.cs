// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Configuration;

/// <summary>
/// The kind of store that holds a configuration object.
/// </summary>
[PublicAPI]
public enum ConfigurationStoreKind
{
    /// <summary>
    /// A JSON file under the application data directory of the product.
    /// </summary>
    File,

    /// <summary>
    /// A key of the Windows registry. PostSharp keeps the settings it shares with its earlier versions there.
    /// </summary>
    RegistryKey
}
