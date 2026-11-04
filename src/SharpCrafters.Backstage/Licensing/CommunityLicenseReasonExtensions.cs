// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;
using System.Collections.Immutable;

namespace SharpCrafters.Backstage.Licensing;

/// <summary>
/// Provides the text with which a user is asked why they are entitled to Metalama Community: the question, the
/// reasons that the user can choose, and the sentence that describes each reason. The command line and the setup
/// pages both ask this question.
/// </summary>
/// <remarks>
/// The user makes a representation by choosing a reason, so the text has a legal meaning. It is the text of the
/// <c>AskCommunityReasonPage</c> page of the Visual Studio extension (in the <c>Metalama.Vsx</c> repository), and the
/// two must be changed together.
/// </remarks>
[PublicAPI]
public static class CommunityLicenseReasonExtensions
{
    /// <summary>
    /// Gets the question that the reasons answer.
    /// </summary>
    public const string Question = "How will you use Metalama Community?";

    /// <summary>
    /// Gets the sentence that comes before the reasons.
    /// </summary>
    public const string Introduction = "Metalama Community is free for use in the following scenarios only.";

    /// <summary>
    /// Gets the reasons that a user can choose, in the order in which they are offered. <see cref="CommunityLicenseReason.None"/>
    /// is not one of them.
    /// </summary>
    public static ImmutableArray<CommunityLicenseReason> SelectableReasons { get; } =
    [
        CommunityLicenseReason.Individual,
        CommunityLicenseReason.SmallBusiness,
        CommunityLicenseReason.OpenSource,
        CommunityLicenseReason.NonCommercial
    ];

    /// <summary>
    /// Gets the sentence with which the user chooses the <paramref name="reason"/>.
    /// </summary>
    public static string GetDescription( this CommunityLicenseReason reason )
        => reason switch
        {
            CommunityLicenseReason.Individual => "I am a freelancer, a student, or a solo entrepreneur.",
            CommunityLicenseReason.SmallBusiness => "I am an employee and no more than 3 developers will use Metalama Community in my company.",
            CommunityLicenseReason.OpenSource => "I will use Metalama Community to build open-source projects.",
            CommunityLicenseReason.NonCommercial =>
                "I will use Metalama Community for non-commercial activities: classroom learning in an educational setting, "
                + "academic research, creating educational materials, or demonstrating the products.",
            _ => throw new ArgumentOutOfRangeException( nameof(reason), reason, "This value is not a reason that a user can choose." )
        };
}
