// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;
using System.IO;

namespace SharpCrafters.Backstage.Telemetry;

/// <summary>
/// The public key with which a telemetry package is encrypted.
/// </summary>
/// <remarks>
/// There is one key for every product, not one per family, because there is one endpoint that receives the packages
/// and one private key that opens them. The package also carries the hash of the public key that sealed it, so a
/// product that used a key of its own would upload packages that nothing on the other side can read.
/// </remarks>
[PublicAPI]
public static class TelemetryEncryptionKey
{
    private const string _resourceName = "SharpCrafters.Backstage.Telemetry.public.key";

    /// <summary>
    /// Reads the public key from the resources of this assembly.
    /// </summary>
    /// <returns>The public key, in the <c>RSAKeyValue</c> XML format.</returns>
    public static byte[] GetPublicKey()
    {
        using var keyStream = typeof(TelemetryEncryptionKey).Assembly.GetManifestResourceStream( _resourceName )
                              ?? throw new InvalidOperationException( "The public key that encrypts the telemetry packages was not found." );

        using var memoryStream = new MemoryStream();
        keyStream.CopyTo( memoryStream );

        return memoryStream.ToArray();
    }
}
