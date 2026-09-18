// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpCrafters.Backstage.Commands.Configuration;

internal sealed class DumpConfigurationCommandSettings : BaseCommandSettings
{
    [Description( "The alias of a single configuration to dump. Dumps every configuration when omitted." )]
    [CommandArgument( 0, "[alias]" )]
    public string? Alias { get; init; }

    [Description( "Dumps every configuration. This is also what happens when no alias is given." )]
    [CommandOption( "--all" )]
    public bool All { get; init; }
}
