// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpCrafters.Backstage.Commands.Maintenance;

internal class ShutdownCommandSettings : BaseCommandSettings
{
    [Description( "Cancels the work of the processes, and ends the ones that have not exited when the timeout has elapsed." )]
    [CommandOption( "--force" )]
    public bool Force { get; init; }

    [Description( "How long to wait for the processes to exit, in seconds." )]
    [CommandOption( "--timeout <SECONDS>" )]
    [DefaultValue( 60 )]
    public int Timeout { get; init; }

    public override ValidationResult Validate()
        => this.Timeout < 0 ? ValidationResult.Error( "The timeout cannot be negative." ) : ValidationResult.Success();
}
