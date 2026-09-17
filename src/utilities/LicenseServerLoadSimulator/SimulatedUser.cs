// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System.Collections.Immutable;

namespace SharpCrafters.Backstage.LicenseServerLoadSimulator;

/// <summary>
/// One developer of the simulated organization: the account the license server sees, the machines they build on, and
/// how likely they are to work at the weekend.
/// </summary>
/// <param name="UserName">The account name, as a domain account, which is what a real server receives.</param>
/// <param name="Machines">The machines this user builds on. A real server allows a fixed number of them per seat.</param>
/// <param name="WeekendProbability">The probability that this user works on any given weekend day.</param>
/// <param name="IsBuildServer">Whether this user is a build server, whose leases a real server does not store and which consumes no seat.</param>
internal sealed record SimulatedUser(
    string UserName,
    ImmutableArray<string> Machines,
    double WeekendProbability,
    bool IsBuildServer )
{
    public override string ToString() => this.UserName;
}
