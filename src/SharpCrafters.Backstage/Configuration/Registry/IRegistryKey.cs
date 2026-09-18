// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;
using System.Collections.Generic;

namespace SharpCrafters.Backstage.Configuration.Registry;

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
    /// <remarks>
    /// <para>
    /// This and the other methods that change the registry throw when the change does not happen, where the methods
    /// that read it answer <see langword="null"/> or an empty list. The asymmetry is deliberate: an absent value is
    /// an answer a reader can act on, and a write that silently did nothing is not — the caller would report a
    /// success and announce a change while the key still holds what it held.
    /// </para>
    /// <para>
    /// The caller is <see cref="RegistryConfigurationManager"/>, which catches these and returns
    /// <see cref="ConfigurationUpdateOutcome.WriteFailed"/>. So nothing above it fails over a registry the user
    /// cannot write to; it is only told the truth about it.
    /// </para>
    /// </remarks>
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
