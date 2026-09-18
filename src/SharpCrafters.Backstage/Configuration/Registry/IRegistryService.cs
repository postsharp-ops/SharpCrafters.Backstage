// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using SharpCrafters.Backstage.Extensibility;
using System;
using System.Collections.Generic;

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
}

/// <summary>
/// An open key of the registry.
/// </summary>
[PublicAPI]
public interface IRegistryKey : IDisposable
{
    /// <summary>
    /// Gets the full path of the key as it is displayed.
    /// </summary>
    string DisplayPath { get; }

    /// <summary>
    /// Gets the value of a given name, or <see langword="null"/> when the value does not exist. A string comes back
    /// as <see cref="string"/>, a <c>DWORD</c> as <see cref="int"/> and a <c>QWORD</c> as <see cref="long"/>.
    /// </summary>
    object? GetValue( string name );

    /// <summary>
    /// Writes a <c>REG_SZ</c> value.
    /// </summary>
    void SetStringValue( string name, string value );

    /// <summary>
    /// Writes a <c>REG_DWORD</c> value.
    /// </summary>
    void SetDWordValue( string name, int value );

    /// <summary>
    /// Writes a <c>REG_QWORD</c> value.
    /// </summary>
    void SetQWordValue( string name, long value );

    /// <summary>
    /// Deletes a value. Deleting a value that does not exist does nothing.
    /// </summary>
    void DeleteValue( string name );

    /// <summary>
    /// Gets the names of the values of this key, in no particular order.
    /// </summary>
    IReadOnlyList<string> GetValueNames();

    /// <summary>
    /// Gets the names of the immediate sub-keys of this key, in no particular order.
    /// </summary>
    IReadOnlyList<string> GetSubKeyNames();

    /// <summary>
    /// Opens a sub-key.
    /// </summary>
    /// <returns>The sub-key, or <see langword="null"/> when it does not exist.</returns>
    IRegistryKey? OpenSubKey( string name, bool writable = false );

    /// <summary>
    /// Opens a sub-key, creating it when it does not exist.
    /// </summary>
    /// <returns>The sub-key, or <see langword="null"/> when it could not be created.</returns>
    IRegistryKey? CreateSubKey( string name );

    /// <summary>
    /// Deletes a sub-key and everything below it. Deleting a sub-key that does not exist does nothing.
    /// </summary>
    void DeleteSubKeyTree( string name );
}
