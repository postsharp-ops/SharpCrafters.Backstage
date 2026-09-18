// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Telemetry;
using SharpCrafters.Backstage.Telemetry.Metrics;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Telemetry;

/// <summary>
/// Tests the values that a telemetry report carries and the text they are written as.
/// </summary>
/// <remarks>
/// <para>
/// The text is a wire format: it is what reaches the server, which parses it without knowing anything about the
/// machine that produced it. Every metric therefore has to be written the same way on every machine, whatever the
/// culture of the user, which is why they go through <see cref="System.Xml.XmlConvert"/> and not through
/// <see cref="object.ToString"/>. That is a quiet convention that an ordinary-looking edit would undo, and a build
/// machine in an invariant culture would not notice.
/// </para>
/// <para>
/// The tests run under a culture that formats numbers and dates differently from the invariant one, so a metric
/// that stopped being culture-independent fails here rather than in the field.
/// </para>
/// </remarks>
public sealed class MetricTests
{
    /// <summary>
    /// Runs an assertion under a culture whose decimal separator is a comma and whose date format is not the
    /// invariant one.
    /// </summary>
    private static void InAnotherCulture( Action action )
    {
        var previousCulture = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo( "fr-FR" );
            action();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
        }
    }

    private static string Write( Metric metric )
    {
        using var stringWriter = new StringWriter();
        metric.WriteValue( stringWriter );

        return stringWriter.ToString();
    }

    [Fact]
    public void ABooleanIsWrittenAsTrueOrFalse()
        => InAnotherCulture(
            () =>
            {
                Assert.Equal( "true", Write( new BoolMetric( "b", true ) ) );
                Assert.Equal( "false", Write( new BoolMetric( "b", false ) ) );
            } );

    [Theory]
    [InlineData( 0, "0" )]
    [InlineData( 1, "1" )]
    [InlineData( -1, "-1" )]
    [InlineData( int.MaxValue, "2147483647" )]
    [InlineData( int.MinValue, "-2147483648" )]
    public void AnIntegerIsWrittenWithoutGrouping( int value, string expected )
        => InAnotherCulture( () => Assert.Equal( expected, Write( new Int32Metric( "i", value ) ) ) );

    /// <summary>
    /// A number is written with a full stop for its decimal separator, and not with the comma that the culture of
    /// the machine may call for.
    /// </summary>
    [Theory]
    [InlineData( 0d, "0" )]
    [InlineData( 1.5d, "1.5" )]
    [InlineData( -0.25d, "-0.25" )]
    public void ANumberIsWrittenWithAFullStop( double value, string expected )
        => InAnotherCulture( () => Assert.Equal( expected, Write( new DoubleMetric( "d", value ) ) ) );

    /// <summary>
    /// A moment is written in a form that says which moment it is, keeping the offset that tells universal time
    /// from local. A report is read in a place other than the one that wrote it, so a moment without that is a
    /// moment that cannot be placed.
    /// </summary>
    [Fact]
    public void AMomentIsWrittenWithItsOffset()
        => InAnotherCulture(
            () =>
            {
                Assert.Equal( "2026-09-18T14:37:11Z", Write( new DateTimeMetric( "t", new DateTime( 2026, 9, 18, 14, 37, 11, DateTimeKind.Utc ) ) ) );

                Assert.Equal(
                    "2026-09-18T14:37:11",
                    Write( new DateTimeMetric( "t", new DateTime( 2026, 9, 18, 14, 37, 11, DateTimeKind.Unspecified ) ) ) );
            } );

    [Fact]
    public void AStringIsWrittenAsItself()
    {
        Assert.Equal( "a value", Write( new StringMetric( "s", "a value" ) ) );
        Assert.Equal( "", Write( new StringMetric( "s", "" ) ) );
    }

    /// <summary>
    /// A string metric that was never given a value is written as the word null rather than as nothing, so that a
    /// metric nobody set is told apart from one set to the empty string.
    /// </summary>
    [Fact]
    public void AStringThatWasNeverSetIsWrittenAsNull() => Assert.Equal( "null", Write( new StringMetric( "s", null ) ) );

    /// <summary>
    /// A set is written as its values separated by commas.
    /// </summary>
    [Fact]
    public void ASetIsWrittenAsCommaSeparatedValues()
    {
        var metric = new SetMetric( "s" );

        Assert.Equal( "", Write( metric ) );

        metric.Set.Add( "one" );
        Assert.Equal( "one", Write( metric ) );

        metric.Set.Add( "two" );
        var written = Write( metric );

        // The order is that of a hash set, which is not specified, so the two values are asserted without it.
        Assert.Contains( "one", written, StringComparison.Ordinal );
        Assert.Contains( "two", written, StringComparison.Ordinal );
        Assert.Contains( ",", written, StringComparison.Ordinal );
    }

    /// <summary>
    /// A set holds each value once, so a feature reported twice in one session counts once.
    /// </summary>
    [Fact]
    public void ASetHoldsEachValueOnce()
    {
        var metric = new SetMetric( "s" );

        Assert.True( metric.SetValue( "one" ) );
        Assert.True( metric.SetValue( "one" ) );

        Assert.Equal( "one", Write( metric ) );
    }

    /// <summary>
    /// A value of a set that contains the separator cannot be told from two values once it is written.
    /// </summary>
    /// <remarks>
    /// Asserted rather than corrected, because the values are feature names that we choose and none of them carries
    /// a comma. What the test pins is that nothing escapes them, so that whoever first adds a value that might
    /// carry one finds this instead of finding a miscounted feature months later.
    /// </remarks>
    [Fact]
    public void ASetDoesNotEscapeTheSeparator()
    {
        var metric = new SetMetric( "s" );
        metric.Set.Add( "one,two" );

        Assert.Equal( "one,two", Write( metric ) );
    }

    /// <summary>
    /// Setting a number adds to it rather than replacing it, which is what makes a metric a measure of a session
    /// and not of its last event.
    /// </summary>
    [Fact]
    public void SettingANumberAddsToIt()
    {
        var integer = new Int32Metric( "i", 1 );
        Assert.True( integer.SetValue( 2 ) );
        Assert.Equal( 3, integer.Value );

        var number = new DoubleMetric( "d", 1.5 );
        Assert.True( number.SetValue( 2.5 ) );
        Assert.Equal( 4d, number.Value );
    }

    /// <summary>
    /// Setting a truth adds to it in the same sense: once something has happened in a session, a later event in
    /// which it did not happen does not unsay it.
    /// </summary>
    [Fact]
    public void SettingATruthNeverUnsetsIt()
    {
        var metric = new BoolMetric( "b", false );

        Assert.True( metric.SetValue( true ) );
        Assert.True( metric.Value );

        Assert.True( metric.SetValue( false ) );
        Assert.True( metric.Value );
    }

    /// <summary>
    /// A value of the wrong kind is refused rather than throwing or being coerced into nonsense. The values arrive
    /// from the product being measured, so a metric named in one place and given a value in another must not be
    /// able to bring a build down.
    /// </summary>
    [Fact]
    public void AValueOfTheWrongKindIsRefused()
    {
        Assert.False( new Int32Metric( "i" ).SetValue( "not a number" ) );
        Assert.False( new DoubleMetric( "d" ).SetValue( "not a number" ) );
        Assert.False( new BoolMetric( "b" ).SetValue( "not a truth" ) );
        Assert.False( new SetMetric( "s" ).SetValue( 1 ) );

        Assert.False( new Int32Metric( "i" ).SetValue( null ) );
        Assert.False( new DoubleMetric( "d" ).SetValue( null ) );
        Assert.False( new BoolMetric( "b" ).SetValue( null ) );
        Assert.False( new SetMetric( "s" ).SetValue( null ) );
    }

    /// <summary>
    /// A number given as a string is read, because that is how a value set from a project file arrives.
    /// </summary>
    [Fact]
    public void ANumberGivenAsTextIsRead()
    {
        var metric = new Int32Metric( "i" );

        Assert.True( metric.SetValue( "42" ) );
        Assert.Equal( 42, metric.Value );
    }

    /// <summary>
    /// The metrics that no caller sets say so rather than pretending to accept a value.
    /// </summary>
    [Fact]
    public void TheMetricsThatCannotBeSetSaySo()
    {
        Assert.Throws<NotSupportedException>( () => new StringMetric( "s" ).SetValue( "a value" ) );
        Assert.Throws<NotSupportedException>( () => new DateTimeMetric( "t" ).SetValue( DateTime.UtcNow ) );
    }

    /// <summary>
    /// A collection finds a metric by its name.
    /// </summary>
    [Fact]
    public void ACollectionIsKeyedByTheNameOfItsMetrics()
    {
        var metrics = new MetricCollection { new Int32Metric( "count", 1 ), new StringMetric( "name", "a value" ) };

        Assert.Equal( 1, ((Int32Metric) metrics["count"]).Value );
        Assert.Equal( "a value", ((StringMetric) metrics["name"]).Value );
        Assert.True( metrics.Contains( "count" ) );
        Assert.False( metrics.Contains( "absent" ) );
    }

    /// <summary>
    /// A collection that has been frozen refuses every change, so that a report which has been written cannot be
    /// added to afterwards.
    /// </summary>
    [Fact]
    public void AFrozenCollectionRefusesEveryChange()
    {
        var metrics = new MetricCollection { new Int32Metric( "count", 1 ) };

        Assert.False( metrics.IsReadOnly );

        metrics.Freeze();

        Assert.True( metrics.IsReadOnly );
        Assert.Throws<InvalidOperationException>( () => metrics.Add( new Int32Metric( "another", 1 ) ) );
        Assert.Throws<InvalidOperationException>( () => metrics.Remove( "count" ) );
        Assert.Throws<InvalidOperationException>( () => metrics.Clear() );
        Assert.Throws<InvalidOperationException>( () => metrics[0] = new Int32Metric( "count", 2 ) );

        // What it already holds is still readable.
        Assert.Equal( 1, ((Int32Metric) metrics["count"]).Value );
    }

    /// <summary>
    /// The empty collection is shared between every caller that needs one, so it must refuse to be added to: a
    /// caller that filled it would be filling everyone else's.
    /// </summary>
    [Fact]
    public void TheSharedEmptyCollectionCannotBeFilled()
    {
        Assert.True( MetricCollection.EmptyReadOnly.IsReadOnly );
        Assert.Empty( MetricCollection.EmptyReadOnly );
        Assert.Throws<InvalidOperationException>( () => MetricCollection.EmptyReadOnly.Add( new Int32Metric( "count", 1 ) ) );
        Assert.Empty( MetricCollection.EmptyReadOnly );
    }
}
