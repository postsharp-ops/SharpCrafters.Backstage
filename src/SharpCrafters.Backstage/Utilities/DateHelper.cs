// Copyright (c) 2020-2025 SharpCrafters s.r.o. and contributors.
// SharpCrafters s.r.o. licenses this file to you under either the MIT license or a proprietary license, depending on the repository from which it was obtained.
// Refer to LICENSE.md in the repository root for complete details.

using System;

namespace SharpCrafters.Backstage.Utilities;

internal static class DateHelper
{
    /// <summary>
    /// Gets the first Monday of the month that <paramref name="date"/> falls in, at midnight. The result keeps the
    /// <see cref="DateTime.Kind"/> of <paramref name="date"/>, so that a moment in universal time yields a boundary
    /// in universal time.
    /// </summary>
    public static DateTime GetFirstMondayOfMonth( this DateTime date )
    {
        var firstDayOfMonth = new DateTime( date.Year, date.Month, 1, 0, 0, 0, date.Kind );

        var offset = (DayOfWeek.Monday - firstDayOfMonth.DayOfWeek + 7) % 7;

        return firstDayOfMonth.AddDays( offset );
    }

}
