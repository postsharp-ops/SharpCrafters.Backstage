// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System;

namespace SharpCrafters.Backstage.Configuration.Registry;

/// <summary>
/// Reads and writes the Windows registry. It exists so that the components that store their configuration there can
/// be tested against a fake hive, which is the only way to test them at all: a test must not touch the registry of
/// the machine that runs it.
/// </summary>
/// <remarks>
/// The shape follows the one PostSharp 2026.0 uses, because the implementation has to reach exactly the same keys
/// and values.
/// </remarks>
[PublicAPI]
public interface IRegistryService : IBackstageService
{
    /// <summary>
    /// Gets a value indicating whether the registry exists on the current platform.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Opens a key.
    /// </summary>
    /// <param name="hive">The hive of the key.</param>
    /// <param name="keyPath">The path of the key inside the hive, for instance <c>Software\SharpCrafters\PostSharp 3</c>.</param>
    /// <param name="writable">Whether the key is opened for writing.</param>
    /// <returns>The key, or <see langword="null"/> when it does not exist.</returns>
    IRegistryKey? OpenKey( RegistryHiveKind hive, string keyPath, bool writable = false );

    /// <summary>
    /// Opens a key, creating it when it does not exist. The key is always writable.
    /// </summary>
    /// <returns>The key, or <see langword="null"/> when it could not be created, typically for want of permission.</returns>
    IRegistryKey? CreateKey( RegistryHiveKind hive, string keyPath );

    /// <summary>
    /// Gets the full path of a key as it is displayed, for instance
    /// <c>HKEY_CURRENT_USER\Software\SharpCrafters\PostSharp 3</c>.
    /// </summary>
    string GetDisplayPath( RegistryHiveKind hive, string keyPath );

    /// <summary>
    /// Watches a key and everything below it, and calls back when anything changes.
    /// </summary>
    /// <param name="hive">The hive of the key.</param>
    /// <param name="keyPath">The path of the key inside the hive.</param>
    /// <param name="onChanged">
    /// Called after a change. It says that something changed and not what, so the caller re-reads what it cares
    /// about. It may be called when nothing the caller cares about has changed, and it may coalesce several changes
    /// into one call.
    /// </param>
    /// <returns>An object that stops the watch when it is disposed, or <see langword="null"/> when the key cannot be
    /// watched.</returns>
    /// <remarks>
    /// The sub-keys are watched as well as the key, because a configuration object of this product spreads over
    /// both: the registered license keys live in a sub-key of the one that holds the rest of the licensing settings.
    /// </remarks>
    IDisposable? WatchChanges( RegistryHiveKind hive, string keyPath, Action onChanged );
}
