using System.Globalization;

namespace LavaPlayground.Api.Lava;

public partial class LavaFilterRegistry
{
    private void RegisterDateFilters()
    {
        Register("Date", "Date", "Formats a date using a .NET format string. 'Now' and 'Today' are keywords.",
            "{{ 'Now' | Date:'dddd, MMMM d, yyyy' }}",
            (input, args) =>
            {
                if (!TryToDate(input, out var date))
                {
                    return Str(input); // Not a date; pass it through untouched.
                }
                var format = Str(Arg(args, 0) ?? "M/d/yyyy");
                // Rock's culture-aware shorthands.
                format = format switch
                {
                    "sd" => "M/d/yyyy",
                    "st" => "h:mm tt",
                    _ => format,
                };
                return date.ToString(format, CultureInfo.InvariantCulture);
            });

        Register("DateAdd", "Date", "Adds an interval to a date. Units: y, M, w, d, h, m, s.",
            "{{ 'Now' | DateAdd:-12,'h' }}",
            (input, args) =>
            {
                var date = ToDate(input, "DateAdd");
                var amount = (double)Num(Arg(args, 0) ?? 0m, "DateAdd");
                var unit = Str(Arg(args, 1) ?? "d");
                return unit switch
                {
                    "y" => date.AddYears((int)amount),
                    "M" => date.AddMonths((int)amount),
                    "w" => date.AddDays(amount * 7),
                    "d" => date.AddDays(amount),
                    "h" => date.AddHours(amount),
                    "m" => date.AddMinutes(amount),
                    "s" => date.AddSeconds(amount),
                    _ => throw new LavaException($"Unknown DateAdd unit \"{unit}\". Use y, M, w, d, h, m, or s."),
                };
            });

        Register("DateDiff", "Date", "Returns the difference between two dates in the given unit.",
            "{{ '2026-01-01' | DateDiff:'2026-12-25','d' }} → 358",
            (input, args) =>
            {
                var start = ToDate(input, "DateDiff");
                var end = ToDate(Arg(args, 0), "DateDiff");
                var unit = Str(Arg(args, 1) ?? "d");
                var span = end - start;
                return unit switch
                {
                    "y" => (decimal)(end.Year - start.Year),
                    "M" => (decimal)((end.Year - start.Year) * 12 + end.Month - start.Month),
                    "w" => decimal.Truncate((decimal)span.TotalDays / 7),
                    "d" => decimal.Truncate((decimal)span.TotalDays),
                    "h" => decimal.Truncate((decimal)span.TotalHours),
                    "m" => decimal.Truncate((decimal)span.TotalMinutes),
                    "s" => decimal.Truncate((decimal)span.TotalSeconds),
                    _ => throw new LavaException($"Unknown DateDiff unit \"{unit}\". Use y, M, w, d, h, m, or s."),
                };
            });

        Register("DaysFromNow", "Date", "Describes the day relative to today in words.",
            "{{ '2026-07-12' | DaysFromNow }} → tomorrow",
            (input, _) =>
            {
                var days = (int)(ToDate(input, "DaysFromNow").Date - DateTime.Today).TotalDays;
                return days switch
                {
                    0 => "today",
                    1 => "tomorrow",
                    -1 => "yesterday",
                    > 1 => $"in {days} days",
                    _ => $"{-days} days ago",
                };
            });

        Register("DaysSince", "Date", "Returns the number of whole days since the given date.",
            "{{ '2026-01-01' | DaysSince }}",
            (input, _) => (decimal)(DateTime.Today - ToDate(input, "DaysSince").Date).TotalDays);

        Register("DaysUntil", "Date", "Returns the number of whole days until the given date.",
            "{{ '2026-12-25' | DaysUntil }}",
            (input, _) => (decimal)(ToDate(input, "DaysUntil").Date - DateTime.Today).TotalDays);

        Register("DaysInMonth", "Date", "Returns the number of days in the date's month.",
            "{{ '2026-02-14' | DaysInMonth }} → 28",
            (input, _) =>
            {
                var date = ToDate(input, "DaysInMonth");
                return (decimal)DateTime.DaysInMonth(date.Year, date.Month);
            });

        Register("HumanizeDateTime", "Date", "Describes how long ago (or from now) a date is, in friendly words.",
            "{{ Person.BirthDate | HumanizeDateTime }} → 33 years ago",
            (input, _) =>
            {
                var date = ToDate(input, "HumanizeDateTime");
                var span = DateTime.Now - date;
                var future = span.TotalSeconds < 0;
                span = span.Duration();
                string text = span.TotalDays >= 365 ? Plural((int)(span.TotalDays / 365), "year")
                    : span.TotalDays >= 30 ? Plural((int)(span.TotalDays / 30), "month")
                    : span.TotalDays >= 2 ? Plural((int)span.TotalDays, "day")
                    : span.TotalDays >= 1 ? (future ? "tomorrow" : "yesterday")
                    : span.TotalHours >= 1 ? Plural((int)span.TotalHours, "hour")
                    : span.TotalMinutes >= 1 ? Plural((int)span.TotalMinutes, "minute")
                    : Plural(Math.Max(1, (int)span.TotalSeconds), "second");
                if (text is "tomorrow" or "yesterday")
                {
                    return text;
                }
                return future ? "in " + text : text + " ago";
            });

        Register("HumanizeTimeSpan", "Date", "Describes the span between two dates; a precision sets how many units to show.",
            "{{ '2026-07-01' | HumanizeTimeSpan:'2026-08-02',2 }} → 4 weeks, 4 days",
            (input, args) =>
            {
                var start = ToDate(input, "HumanizeTimeSpan");
                var end = ToDate(Arg(args, 0) ?? "Now", "HumanizeTimeSpan");
                var precision = (int)Num(Arg(args, 1) ?? 1m, "HumanizeTimeSpan");
                var span = (end - start).Duration();

                var parts = new List<string>();
                var days = (long)span.TotalDays;
                var years = days / 365; days %= 365;
                var weeks = days / 7; days %= 7;
                void Add(long n, string unit)
                {
                    if (n > 0) parts.Add(Plural((int)n, unit));
                }
                Add(years, "year");
                Add(weeks, "week");
                Add(days, "day");
                Add(span.Hours, "hour");
                Add(span.Minutes, "minute");
                if (parts.Count == 0)
                {
                    parts.Add(Plural(Math.Max(0, span.Seconds), "second"));
                }
                return string.Join(", ", parts.Take(Math.Max(1, precision)));
            });

        Register("SundayDate", "Date", "Returns the Sunday date that ends the date's week.",
            "{{ 'Now' | SundayDate | Date:'M/d/yyyy' }}",
            (input, _) =>
            {
                var date = ToDate(input, "SundayDate").Date;
                var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)date.DayOfWeek + 7) % 7;
                return date.AddDays(daysUntilSunday);
            });

        Register("ToMidnight", "Date", "Returns the date with the time set to 12:00 AM.",
            "{{ 'Now' | ToMidnight | Date:'h:mm tt' }} → 12:00 AM",
            (input, _) => ToDate(input, "ToMidnight").Date);
    }

    private static string Plural(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
}
