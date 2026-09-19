// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;

namespace SharpCrafters.Backstage.Licensing.Registration;

/// <summary>
/// An edition that the user obtains by asking for it rather than by buying it: a free edition, a trial, or one that an
/// earlier version of the product issued.
/// </summary>
/// <remarks>
/// <para>
/// A product family declares the editions it offers, and the command line and the setup pages present what is
/// declared. Neither of them knows the editions of any family, which is what keeps a page from inviting a user to stay
/// with an edition that does not exist, and the command line from advertising a verb whose only outcome is an error.
/// </para>
/// <para>
/// An edition carries its own procedure rather than naming one on a service. The alternative was a service with a
/// method per edition and an edition that pointed at one, which listed every edition of every family on an interface
/// that is meant to know none of them.
/// </para>
/// <para>
/// An edition is created once per family, before any service exists: the command line reads the editions while it
/// builds its verbs. So everything that describes an edition must be computable without a service, and everything that
/// needs one takes a <see cref="SelfRegisteredEditionContext"/>, which the edition must not keep.
/// </para>
/// </remarks>
[PublicAPI]
public abstract class SelfRegisteredEdition
{
    /// <summary>
    /// Gets the name under which the edition is asked for: the verb of the command line, and the token that the setup
    /// pages post.
    /// </summary>
    public abstract string Alias { get; }

    /// <summary>
    /// Gets the name of the edition as a user reads it, for instance <c>PostSharp Essentials</c>.
    /// </summary>
    /// <remarks>
    /// The edition names itself rather than being named after the product of its license key. The two differ for a
    /// family that expresses an edition through the license type: the free edition of PostSharp is a PostSharp
    /// Ultimate key, and naming it after its product would invite the user to activate the edition they have to buy.
    /// </remarks>
    public abstract string DisplayName { get; }

    /// <summary>
    /// Gets one sentence saying what the edition is. The command line describes its verb with it, and the setup pages
    /// use it as the body of the choice.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Gets the sentence with which the user is told that the edition is registered.
    /// </summary>
    public virtual string SuccessMessage => $"You are now using {this.DisplayName}.";

    /// <summary>
    /// Gets what kind of offer this is, for a caller that looks for one of them without matching an alias.
    /// </summary>
    public virtual SelfRegisteredEditionKind Kind => SelfRegisteredEditionKind.Free;

    /// <summary>
    /// Gets a value indicating whether the user must say why they are entitled to the edition.
    /// </summary>
    public virtual bool RequiresReason => false;

    /// <summary>
    /// Gets a value indicating whether the command line offers a verb for the edition.
    /// </summary>
    /// <remarks>
    /// An edition that another program registers on the user's behalf declares <see langword="false"/>, so that the
    /// help does not advertise a verb for something nobody registers by hand.
    /// </remarks>
    public virtual bool IsAvailableFromCommandLine => true;

    /// <summary>
    /// Gets the heading under which the setup pages offer the edition, or <see langword="null"/> when they do not
    /// offer it at all.
    /// </summary>
    /// <remarks>
    /// An edition that only an earlier version of the product can consume is not offered to someone setting the
    /// product up for the first time. Neither is one that <see cref="RequiresReason"/>: the setup pages have no way to
    /// ask the question.
    /// </remarks>
    public virtual string? SetupTitle => null;

    /// <summary>
    /// Determines whether the edition can be registered on this machine as things stand.
    /// </summary>
    /// <remarks>
    /// This is the question the setup pages ask before offering the edition, so it must read the state of the machine
    /// and never the choices of the caller: an edition that refused itself because nothing was chosen yet would never
    /// be offered at all.
    /// </remarks>
    public virtual SelfRegisteredEditionAvailability GetAvailability( SelfRegisteredEditionContext context )
        => SelfRegisteredEditionAvailability.Available;

    /// <summary>
    /// Describes the license that the edition grants.
    /// </summary>
    /// <remarks>
    /// Public so that a test can assert what an edition grants without registering it.
    /// </remarks>
    public abstract UnsignedLicense CreateLicense( SelfRegisteredEditionContext context );

    /// <summary>
    /// Records whatever the edition keeps beside its license key. The default keeps nothing.
    /// </summary>
    /// <remarks>
    /// It returns the configuration rather than writing it, because it is called inside the transaction that writes
    /// the key, so that the key and this state are written together or not at all.
    /// </remarks>
    protected virtual LicensingConfiguration OnRegistering( LicensingConfiguration configuration, SelfRegisteredEditionContext context )
        => configuration;

    /// <summary>
    /// Registers the edition.
    /// </summary>
    /// <remarks>
    /// Overridable, so that an edition with a step of its own owns that step. What is left to the registration service
    /// is only what an edition cannot do: deciding that the session is attended at all, and building the key.
    /// </remarks>
    protected internal virtual LicenseRegistrationResult Register( SelfRegisteredEditionContext context )
    {
        var availability = this.GetAvailability( context );

        if ( !availability.IsAvailable )
        {
            return LicenseRegistrationResult.Failure( availability.Message! );
        }

        return context.RegisterLicense( this.CreateLicense( context ), configuration => this.OnRegistering( configuration, context ) );
    }

    /// <inheritdoc />
    public override string ToString() => this.Alias;
}
