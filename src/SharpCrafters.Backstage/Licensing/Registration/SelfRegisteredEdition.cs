// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;

namespace SharpCrafters.Backstage.Licensing.Registration;

/// <summary>
/// An edition that the user can obtain by asking for it rather than by buying it: a free edition, or one that an
/// earlier version of the product issued.
/// </summary>
/// <remarks>
/// <para>
/// A product family declares the editions it offers, and the command line and the setup pages present what is
/// declared. Neither of them knows the editions of any family, which is what keeps a page from inviting a user to
/// stay with an edition that does not exist, and the command line from advertising a command whose only outcome is
/// an error.
/// </para>
/// <para>
/// The registration is an action, so that a family says which procedure applies to which of its editions. The
/// procedures live in <see cref="ILicenseRegistrationService"/>, because the rules they enforce — that the session
/// is attended, that the edition is not registered already — belong to licensing and not to a product.
/// </para>
/// </remarks>
/// <param name="Alias">
/// The name under which the edition is asked for: the verb of the command line, for instance <c>community</c>.
/// </param>
/// <param name="Description">
/// One sentence saying what the edition is. The command line uses it to describe the command, and the setup pages
/// use it as the body of the choice.
/// </param>
/// <param name="RegisterAction">
/// Registers the edition. It takes the service rather than closing over it, because a catalog is built once for the
/// product and a service belongs to a service provider, of which there may be several.
/// </param>
[PublicAPI]
public sealed record SelfRegisteredEdition(
    string Alias,
    string Description,
    Func<ILicenseRegistrationService, CommunityLicenseReason, LicenseRegistrationResult> RegisterAction )
{
    /// <summary>
    /// Gets the sentence with which the user is told that the edition is registered. The default says that they are
    /// now using it, named by its alias.
    /// </summary>
    public string SuccessMessage { get; init; } = $"You are now using the {Alias} edition.";

    /// <summary>
    /// Gets a value indicating whether the command that registers this edition requires the user to say why they are
    /// entitled to it.
    /// </summary>
    public bool RequiresReason { get; init; }

    /// <summary>
    /// Gets the heading under which the setup pages offer the edition, or <see langword="null"/> when they do not
    /// offer it at all.
    /// </summary>
    /// <remarks>
    /// An edition that only an earlier version of the product can consume is registered from the command line and
    /// not offered to someone setting the product up for the first time. Neither is one that
    /// <see cref="RequiresReason"/>: the setup pages have no way to ask the question.
    /// </remarks>
    public string? SetupTitle { get; init; }
}
