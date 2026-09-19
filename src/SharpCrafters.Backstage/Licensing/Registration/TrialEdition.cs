// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using JetBrains.Annotations;
using System;
using System.Linq;

namespace SharpCrafters.Backstage.Licensing.Registration;

/// <summary>
/// The period during which the whole product is granted so that the user can judge it.
/// </summary>
/// <remarks>
/// Every family offers a trial, on the same terms and for the same length, so it is declared here rather than by each
/// of them. A family that wants different terms derives from this class and returns its own from
/// <c>LicenseProductCatalog.Trial</c>.
/// </remarks>
[PublicAPI]
public class TrialEdition : SelfRegisteredEdition
{
    private readonly ILicenseProductCatalog _catalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrialEdition"/> class.
    /// </summary>
    /// <param name="catalog">
    /// The catalog of the family, which names the product that the trial grants. It is taken here rather than read
    /// from a context because the command line reads <see cref="Description"/> before any service exists.
    /// </param>
    public TrialEdition( ILicenseProductCatalog catalog )
    {
        this._catalog = catalog;
    }

    /// <summary>
    /// Gets the name of the product that the trial grants, which is the premium edition of the family.
    /// </summary>
    protected string ProductName => this._catalog.GetDisplayName( this._catalog.EvaluationProduct );

    private static int PeriodInDays => (int) LicensingConstants.EvaluationPeriod.TotalDays;

    /// <inheritdoc />
    public override string Alias => "try";

    /// <inheritdoc />
    public override string DisplayName => $"the {this.ProductName} trial";

    /// <inheritdoc />
    public override string Description => $"Activates the {this.ProductName} trial period.";

    /// <inheritdoc />
    public override string SuccessMessage => $"You are now using the {this.ProductName} trial.";

    /// <inheritdoc />
    public override SelfRegisteredEditionKind Kind => SelfRegisteredEditionKind.Trial;

    /// <inheritdoc />
    public override string? SetupTitle => $"Start a {PeriodInDays}-day Trial of {this.ProductName}";

    /// <inheritdoc />
    /// <remarks>
    /// The trial is the one edition whose availability is not a given: it lasts a while, and it cannot be started
    /// again immediately afterwards, or it would not be a trial.
    /// </remarks>
    public override SelfRegisteredEditionAvailability GetAvailability( SelfRegisteredEditionContext context )
    {
        if ( context.RegisteredLicenses.Any( l => l.LicenseType == LicenseType.Evaluation && l.ValidTo >= context.UtcNow ) )
        {
            return SelfRegisteredEditionAvailability.Unavailable( "The evaluation license is already active." );
        }

        var lastEvaluationStartDate = context.Configuration.LastEvaluationStartDate ?? DateTime.MinValue;
        var nextEvaluationStartDate = lastEvaluationStartDate + LicensingConstants.NoEvaluationPeriod + LicensingConstants.EvaluationPeriod;

        if ( nextEvaluationStartDate > context.UtcNow )
        {
            return SelfRegisteredEditionAvailability.Unavailable( $"You cannot start a new trial period until {nextEvaluationStartDate}." );
        }

        return SelfRegisteredEditionAvailability.Available;
    }

    /// <inheritdoc />
    public override UnsignedLicense CreateLicense( SelfRegisteredEditionContext context )
    {
        // Counted from midnight, so that a trial started late in the evening is not a day shorter than one started in
        // the morning.
        var start = context.UtcNow.Date;
        var end = start + LicensingConstants.EvaluationPeriod;

        return new UnsignedLicense( context.Catalog.EvaluationProduct, LicenseType.Evaluation, start ) { ValidTo = end, SubscriptionEndDate = end };
    }

    /// <inheritdoc />
    /// <remarks>
    /// The moment the trial started is what the cool-off period is counted from, and it outlives the license key: a
    /// user who unregisters the trial does not thereby become entitled to another one.
    /// </remarks>
    protected override LicensingConfiguration OnRegistering( LicensingConfiguration configuration, SelfRegisteredEditionContext context )
        => configuration with { LastEvaluationStartDate = context.UtcNow };
}
