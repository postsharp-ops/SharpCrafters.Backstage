// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpCrafters.Backstage.Commands.Licensing;

#pragma warning disable CS8618

internal class TestLicenseServerCommandSettings : BaseCommandSettings
{
    [Description( "The URL of the license server to contact." )]
    [CommandArgument( 1, "<url>" )]
    public string Url { get; init; }
}
