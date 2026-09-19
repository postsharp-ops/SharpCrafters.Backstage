// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;
using System.Collections.Immutable;

namespace SharpCrafters.Backstage.Configuration.Registry;

/// <summary>
/// Maps one configuration object onto a registry key.
/// </summary>
/// <typeparam name="T">The configuration object that the schema maps.</typeparam>
/// <remarks>
/// <para>
/// A schema derives from this class rather than implementing <see cref="IRegistryConfigurationSchema"/> so that it
/// says only what is peculiar to it. The type of the object, the cast that every write would otherwise repeat, and
/// the reading and writing of the shapes that a registry has no notion of — a dictionary, a value holding a
/// identifier in its textual form — are the same wherever they are needed and are given here.
/// </para>
/// <para>
/// The helpers are deliberate about one thing: a key that is absent is not an error. A configuration object is read
/// before anything has been written, and every version of the product that shares these keys creates only the ones it
/// has something to put in, so a reader that demanded a key would fail on a machine where the other version has
/// simply never run.
/// </para>
/// </remarks>
[PublicAPI]
public abstract class RegistryConfigurationSchema<T> : IRegistryConfigurationSchema
    where T : ConfigurationFile
{
    /// <inheritdoc />
    public Type ConfigurationType => typeof(T);

    /// <inheritdoc />
    /// <remarks>
    /// The settings of a user live under the hive of that user. A schema whose object is machine-wide overrides this.
    /// </remarks>
    public virtual RegistryHiveKind Hive => RegistryHiveKind.CurrentUser;

    /// <inheritdoc />
    public abstract string KeyPath { get; }

    ConfigurationFile IRegistryConfigurationSchema.Read( IRegistryKey? key ) => this.Read( key );

    void IRegistryConfigurationSchema.Write( IRegistryKey key, ConfigurationFile value ) => this.Write( key, (T) value );

    /// <summary>
    /// Reads the configuration object from a key, which is <see langword="null"/> when the key does not exist.
    /// </summary>
    protected abstract T Read( IRegistryKey? key );

    /// <summary>
    /// Writes the configuration object into a key.
    /// </summary>
    protected abstract void Write( IRegistryKey key, T value );

    /// <summary>
    /// Reads a value that holds an identifier in its textual form.
    /// </summary>
    protected static Guid? ReadGuid( IRegistryKey? key, string name ) => Guid.TryParse( key.GetString( name ), out var value ) ? value : null;

    /// <summary>
    /// Reads a sub-key as a dictionary, one entry per value.
    /// </summary>
    /// <param name="key">The key that holds the sub-key, or <see langword="null"/>.</param>
    /// <param name="subKeyName">The name of the sub-key.</param>
    /// <param name="convert">Reads one value.</param>
    /// <remarks>
    /// The comparison is case-insensitive, as it is in the file-based store: the names are hashes and identifiers,
    /// which are written in one case and read in another often enough to matter, and the registry compares the names
    /// of values that way in any case.
    /// </remarks>
    protected static ImmutableDictionary<string, TValue> ReadDictionary<TValue>(
        IRegistryKey? key,
        string subKeyName,
        Func<object?, TValue> convert )
    {
        using var subKey = key?.OpenSubKey( subKeyName );

        if ( subKey == null )
        {
            return ImmutableDictionary<string, TValue>.Empty.WithComparers( StringComparer.OrdinalIgnoreCase );
        }

        var builder = ImmutableDictionary.CreateBuilder<string, TValue>( StringComparer.OrdinalIgnoreCase );

        foreach ( var name in subKey.GetValueNames() )
        {
            builder[name] = convert( subKey.GetValue( name ) );
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Reads a sub-key as a dictionary, leaving out the entries whose value could not be read.
    /// </summary>
    /// <remarks>
    /// A value the registry holds in a form this version does not understand is absent rather than being a default
    /// that would look like a real one: an entry recording when something last happened must not read as though it
    /// happened at the beginning of time.
    /// </remarks>
    protected static ImmutableDictionary<string, TValue> ReadDictionaryOfValues<TValue>(
        IRegistryKey? key,
        string subKeyName,
        Func<object?, TValue?> convert )
        where TValue : struct
    {
        var builder = ImmutableDictionary.CreateBuilder<string, TValue>( StringComparer.OrdinalIgnoreCase );

        foreach ( var entry in ReadDictionary( key, subKeyName, convert ) )
        {
            if ( entry.Value != null )
            {
                builder[entry.Key] = entry.Value.Value;
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Makes the values of a sub-key be exactly the entries of a dictionary.
    /// </summary>
    /// <remarks>
    /// Unlike the values of a key that this version shares with another one, a whole sub-key of this shape belongs to
    /// whoever writes it, so an entry it no longer holds is removed. These dictionaries are pruned as they are
    /// written, and a value left behind would grow the key without bound.
    /// </remarks>
    protected static void WriteDictionary<TValue>(
        IRegistryKey key,
        string subKeyName,
        ImmutableDictionary<string, TValue> entries,
        Action<IRegistryKey, string, TValue> writeEntry )
    {
        using var subKey = key.CreateSubKey( subKeyName );

        if ( subKey == null )
        {
            return;
        }

        foreach ( var entry in entries )
        {
            writeEntry( subKey, entry.Key, entry.Value );
        }

        foreach ( var name in subKey.GetValueNames() )
        {
            if ( !entries.ContainsKey( name ) )
            {
                subKey.DeleteValue( name );
            }
        }
    }
}
