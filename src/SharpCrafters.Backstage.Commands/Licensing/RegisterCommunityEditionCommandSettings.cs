// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Licensing;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpCrafters.Backstage.Commands.Licensing;

internal class RegisterCommunityEditionCommandSettings : BaseCommandSettings
{
    [Description(
        "The reason for which you are entitled to the edition: Individual, NonCommercial, OpenSource or SmallBusiness. "
        + "Use it for a scripted install. When it is omitted, the command asks for the reason." )]
    [CommandOption( "--reason <REASON>" )]
    public CommunityLicenseReason? Reason { get; init; }
}
