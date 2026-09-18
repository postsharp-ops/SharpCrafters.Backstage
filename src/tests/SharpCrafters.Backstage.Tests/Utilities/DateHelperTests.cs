// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using SharpCrafters.Backstage.Utilities;
using System;
using System.Globalization;
using Xunit;

namespace SharpCrafters.Backstage.Tests.Utilities;

/// <summary>
/// Tests the monthly boundary that the telemetry identifier and the salts are rotated on.
/// </summary>
/// <remarks>
/// The boundary is a Monday so that the lifetime of an identifier always spans whole weeks, and weekly aggregates
/// are never split between two identifiers. The arithmetic is the same as the one PostSharp uses, so that the two
/// products, which share the value recording the last rotation, agree on when the next one is due.
/// </remarks>
public sealed class DateHelperTests
{
    private static DateTime Parse( string dateString )
    {
        if ( DateTime.TryParseExact(
                dateString,
                ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate ) )
        {
            return parsedDate;
        }
        else
        {
            throw new FormatException( "Invalid date format. Expected format is YYYY-MM-DD, optionally followed by HH:mm:ss." );
        }
    }

    /// <summary>
    /// Every day of a month gives the same first Monday, whether it falls before that Monday or after it.
    /// </summary>
    [Theory]

    // May 2025, whose first day is a Thursday, so the first Monday is the 5th. The first four days precede it.
    [InlineData( "2025-05-01", "2025-05-05" )]
    [InlineData( "2025-05-04", "2025-05-05" )]
    [InlineData( "2025-05-05", "2025-05-05" )]
    [InlineData( "2025-05-11", "2025-05-05" )]
    [InlineData( "2025-05-31", "2025-05-05" )]

    // September 2025, whose first day is itself a Monday: the offset is zero, which is the case an implementation
    // that adds seven days when the remainder is zero gets wrong.
    [InlineData( "2025-09-01", "2025-09-01" )]
    [InlineData( "2025-09-30", "2025-09-01" )]

    // February 2027, whose first day is a Monday in a month of exactly four weeks.
    [InlineData( "2027-02-28", "2027-02-01" )]

    // August 2027, whose first day is a Sunday, so the first Monday is the 2nd: the greatest offset there is.
    [InlineData( "2027-08-01", "2027-08-02" )]
    [InlineData( "2027-08-02", "2027-08-02" )]

    // July 2025, whose first day is a Tuesday, so the first Monday is the 7th: the greatest wait there is.
    [InlineData( "2025-07-01", "2025-07-07" )]
    [InlineData( "2025-07-06", "2025-07-07" )]

    // A leap February, and the last day of a year.
    [InlineData( "2028-02-29", "2028-02-07" )]
    [InlineData( "2025-12-31", "2025-12-01" )]
    public void TheFirstMondayIsTheFirstMonday( string input, string expectedOutput )
        => Assert.Equal( Parse( expectedOutput ), Parse( input ).GetFirstMondayOfMonth() );

    /// <summary>
    /// The first Monday is always a Monday, always in the month asked for, and always within its first seven days.
    /// Asserted over ten years rather than over the months written out above, so that a month nobody thought of is
    /// covered too.
    /// </summary>
    [Fact]
    public void TheFirstMondayIsAMondayOfTheFirstWeekOfEveryMonth()
    {
        for ( var date = new DateTime( 2020, 1, 1 ); date < new DateTime( 2030, 1, 1 ); date = date.AddDays( 1 ) )
        {
            var firstMonday = date.GetFirstMondayOfMonth();

            Assert.Equal( DayOfWeek.Monday, firstMonday.DayOfWeek );
            Assert.Equal( date.Year, firstMonday.Year );
            Assert.Equal( date.Month, firstMonday.Month );
            Assert.InRange( firstMonday.Day, 1, 7 );
            Assert.Equal( TimeSpan.Zero, firstMonday.TimeOfDay );
        }
    }

    /// <summary>
    /// The time of day is discarded: a boundary is a date, and two moments of the same day are on the same side of
    /// it.
    /// </summary>
    [Fact]
    public void TheTimeOfDayDoesNotMatter()
        => Assert.Equal( Parse( "2025-05-05" ), Parse( "2025-05-20 23:59:59" ).GetFirstMondayOfMonth() );

    /// <summary>
    /// The kind of the moment is carried through, so that a boundary derived from a moment in universal time is
    /// itself in universal time and can be compared with one without a silent shift.
    /// </summary>
    [Theory]
    [InlineData( DateTimeKind.Utc )]
    [InlineData( DateTimeKind.Local )]
    [InlineData( DateTimeKind.Unspecified )]
    public void TheKindIsCarriedThrough( DateTimeKind kind )
        => Assert.Equal( kind, new DateTime( 2025, 5, 20, 0, 0, 0, kind ).GetFirstMondayOfMonth().Kind );
}
