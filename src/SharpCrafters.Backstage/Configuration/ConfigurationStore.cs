// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Configuration;

/// <summary>
/// Where a configuration object is stored. A caller that wants to show the location to the user, or to open it in an
/// editor, needs to know which kind of store it is: a file path goes to the default editor of the user, whereas a
/// registry key goes to <c>regedit</c>.
/// </summary>
/// <param name="Kind">The kind of store.</param>
/// <param name="Path">
/// The full path of the file, or the full path of the registry key including the name of the hive, for instance
/// <c>HKEY_CURRENT_USER\Software\SharpCrafters\PostSharp 3</c>.
/// </param>
[PublicAPI]
public readonly record struct ConfigurationStore( ConfigurationStoreKind Kind, string Path )
{
    /// <summary>
    /// Returns <see cref="Path"/>, which is what a message shows in either case.
    /// </summary>
    public override string ToString() => this.Path;
}
