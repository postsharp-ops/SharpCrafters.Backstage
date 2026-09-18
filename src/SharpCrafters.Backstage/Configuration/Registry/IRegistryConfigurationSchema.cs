// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;

namespace SharpCrafters.Backstage.Configuration.Registry;

/// <summary>
/// Maps one configuration object onto a key of the registry: which key holds it, and how its members correspond to
/// the values of that key.
/// </summary>
/// <remarks>
/// <para>
/// A schema exists because the correspondence is not mechanical. The names and the encodings are those that an
/// earlier version of the product chose, the two versions read and write the same values while they run beside each
/// other, and a member that this version added has no counterpart there at all. Serializing the object would have
/// produced names of this version's choosing, which the other version would not read.
/// </para>
/// <para>
/// A schema belongs to the product whose registry layout it describes, not to the product-neutral services.
/// </para>
/// </remarks>
[PublicAPI]
public interface IRegistryConfigurationSchema
{
    /// <summary>
    /// Gets the type of the configuration object that this schema maps.
    /// </summary>
    Type ConfigurationType { get; }

    /// <summary>
    /// Gets the hive that holds the key.
    /// </summary>
    RegistryHiveKind Hive { get; }

    /// <summary>
    /// Gets the path of the key inside the hive, for instance <c>Software\SharpCrafters\PostSharp 3</c>.
    /// </summary>
    string KeyPath { get; }

    /// <summary>
    /// Reads the configuration object from the key.
    /// </summary>
    /// <param name="key">The key, or <see langword="null"/> when it does not exist.</param>
    /// <returns>
    /// The configuration object. A schema returns the default object rather than <see langword="null"/> when the key
    /// is absent or holds none of the values it knows, because a product that has never run has a configuration all
    /// the same.
    /// </returns>
    ConfigurationFile Read( IRegistryKey? key );

    /// <summary>
    /// Writes the configuration object to the key.
    /// </summary>
    /// <param name="key">The key, which the caller has created.</param>
    /// <param name="value">The object to write.</param>
    /// <remarks>
    /// An implementation writes only the values it maps and never clears the key, because the other version of the
    /// product keeps its own values in the same place and a value this version does not know is not a value it may
    /// delete. It writes a value only when the stored one differs, for which
    /// <see cref="RegistryConfigurationValues"/> has the methods.
    /// </remarks>
    void Write( IRegistryKey key, ConfigurationFile value );
}
