// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Configuration.Registry;

/// <summary>
/// The hive that holds a key.
/// </summary>
[PublicAPI]
public enum RegistryHiveKind
{
    /// <summary>
    /// The settings of the current user, <c>HKEY_CURRENT_USER</c>.
    /// </summary>
    CurrentUser,

    /// <summary>
    /// The settings of every user of the machine, <c>HKEY_LOCAL_MACHINE</c>. A product reads them and an
    /// administrator writes them.
    /// </summary>
    LocalMachine
}
