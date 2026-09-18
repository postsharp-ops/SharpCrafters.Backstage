// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;

namespace SharpCrafters.Backstage.Configuration.Registry;

/// <summary>
/// Reads and writes the individual values of a configuration object, in the encodings of
/// <see cref="RegistryValueConverters"/>, writing only what differs from what is stored.
/// </summary>
/// <remarks>
/// <para>
/// A write is skipped when the stored value already holds the wanted content. A configuration object maps onto a
/// dozen values and an update usually changes one of them, so writing them all would turn one write into a dozen,
/// each of which reaches the disk. It also keeps the last-write time of the key meaningful, which is what tells
/// another process that something has actually changed.
/// </para>
/// <para>
/// A value whose model counterpart is absent is deleted rather than written empty, because absent is what the other
/// version of the product reads as "not set". Only the values a schema names are ever touched.
/// </para>
/// </remarks>
[PublicAPI]
public static class RegistryConfigurationValues
{
    /// <summary>
    /// Reads a string, which is <see langword="null"/> when the value is absent.
    /// </summary>
    public static string? GetString( this IRegistryKey? key, string name ) => RegistryValueConverters.ToStringValue( key?.GetValue( name ) );

    /// <summary>
    /// Reads a date, which is <see langword="null"/> when the value is absent or outside the range of a date.
    /// </summary>
    public static DateTime? GetDateTime( this IRegistryKey? key, string name ) => RegistryValueConverters.QWordToDateTime( key?.GetValue( name ) );

    /// <summary>
    /// Reads a Boolean.
    /// </summary>
    /// <param name="key">The key, or <see langword="null"/> when it does not exist.</param>
    /// <param name="name">The name of the value.</param>
    /// <param name="defaultValue">What an absent value means, which is not always <see langword="false"/>.</param>
    public static bool GetBoolean( this IRegistryKey? key, string name, bool defaultValue = false )
        => RegistryValueConverters.DWordToBoolean( key?.GetValue( name ), defaultValue );

    /// <summary>
    /// Reads a Boolean that may be unset.
    /// </summary>
    public static bool? GetNullableBoolean( this IRegistryKey? key, string name )
        => RegistryValueConverters.DWordToNullableBoolean( key?.GetValue( name ) );

    /// <summary>
    /// Reads a 32-bit integer, which is <see langword="null"/> when the value is absent.
    /// </summary>
    public static int? GetInt32( this IRegistryKey? key, string name ) => key?.GetValue( name ) as int?;

    /// <summary>
    /// Reads a 64-bit integer, which is <see langword="null"/> when the value is absent.
    /// </summary>
    public static long? GetInt64( this IRegistryKey? key, string name ) => key?.GetValue( name ) as long?;

    /// <summary>
    /// Writes a string, or deletes the value when the string is <see langword="null"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the key was touched.</returns>
    public static bool SetString( this IRegistryKey key, string name, string? value )
    {
        var storedValue = RegistryValueConverters.ToStringValue( key.GetValue( name ) );

        if ( value == null )
        {
            if ( storedValue == null && key.GetValue( name ) == null )
            {
                return false;
            }

            key.DeleteValue( name );

            return true;
        }

        if ( string.Equals( storedValue, value, StringComparison.Ordinal ) )
        {
            return false;
        }

        key.SetStringValue( name, value );

        return true;
    }

    /// <summary>
    /// Writes a date, or deletes the value when the date is <see langword="null"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the key was touched.</returns>
    public static bool SetDateTime( this IRegistryKey key, string name, DateTime? value )
    {
        if ( value == null )
        {
            if ( key.GetValue( name ) == null )
            {
                return false;
            }

            key.DeleteValue( name );

            return true;
        }

        var encoded = RegistryValueConverters.DateTimeToQWord( value.Value );

        if ( key.GetValue( name ) is long storedValue && storedValue == encoded )
        {
            return false;
        }

        key.SetQWordValue( name, encoded );

        return true;
    }

    /// <summary>
    /// Writes a Boolean.
    /// </summary>
    /// <returns><see langword="true"/> if the key was touched.</returns>
    public static bool SetBoolean( this IRegistryKey key, string name, bool value )
        => key.SetInt32( name, RegistryValueConverters.BooleanToDWord( value ) );

    /// <summary>
    /// Writes a Boolean that may be unset. An unset value is written as zero rather than deleted, because zero is
    /// what "not answered" means in this encoding and is not the same as "false".
    /// </summary>
    /// <returns><see langword="true"/> if the key was touched.</returns>
    public static bool SetNullableBoolean( this IRegistryKey key, string name, bool? value )
        => key.SetInt32( name, RegistryValueConverters.NullableBooleanToDWord( value ) );

    /// <summary>
    /// Writes a 32-bit integer.
    /// </summary>
    /// <returns><see langword="true"/> if the key was touched.</returns>
    public static bool SetInt32( this IRegistryKey key, string name, int value )
    {
        if ( key.GetValue( name ) is int storedValue && storedValue == value )
        {
            return false;
        }

        key.SetDWordValue( name, value );

        return true;
    }

    /// <summary>
    /// Writes a 32-bit integer, or deletes the value when it is <see langword="null"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the key was touched.</returns>
    public static bool SetInt32( this IRegistryKey key, string name, int? value )
    {
        if ( value != null )
        {
            return key.SetInt32( name, value.Value );
        }

        if ( key.GetValue( name ) == null )
        {
            return false;
        }

        key.DeleteValue( name );

        return true;
    }

    /// <summary>
    /// Writes a 64-bit integer, or deletes the value when it is <see langword="null"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the key was touched.</returns>
    public static bool SetInt64( this IRegistryKey key, string name, long? value )
    {
        if ( value == null )
        {
            if ( key.GetValue( name ) == null )
            {
                return false;
            }

            key.DeleteValue( name );

            return true;
        }

        if ( key.GetValue( name ) is long storedValue && storedValue == value.Value )
        {
            return false;
        }

        key.SetQWordValue( name, value.Value );

        return true;
    }
}
