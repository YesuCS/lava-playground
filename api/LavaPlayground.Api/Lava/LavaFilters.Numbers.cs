using System.Globalization;

namespace LavaPlayground.Api.Lava;

public partial class LavaFilterRegistry
{
    private void RegisterNumberFilters()
    {
        Register("Plus", "Numeric", "Adds a number.",
            "{{ 3 | Plus:4 }} → 7",
            (input, args) => Num(input, "Plus") + Num(Arg(args, 0) ?? 0m, "Plus"));

        Register("Minus", "Numeric", "Subtracts a number.",
            "{{ 10 | Minus:4 }} → 6",
            (input, args) => Num(input, "Minus") - Num(Arg(args, 0) ?? 0m, "Minus"));

        Register("Times", "Numeric", "Multiplies by a number.",
            "{{ 6 | Times:7 }} → 42",
            (input, args) => Num(input, "Times") * Num(Arg(args, 0) ?? 1m, "Times"));

        Register("DividedBy", "Numeric", "Divides by a number; a second argument sets decimal precision.",
            "{{ 12.434 | DividedBy:6,2 }} → 2.07",
            (input, args) =>
            {
                var divisor = Num(Arg(args, 0) ?? 1m, "DividedBy");
                if (divisor == 0)
                {
                    throw new LavaException("Cannot divide by zero.");
                }
                var result = Num(input, "DividedBy") / divisor;
                return args.Length > 1 ? Math.Round(result, (int)Num(args[1], "DividedBy")) : result;
            });

        Register("Modulo", "Numeric", "Returns the remainder of a division.",
            "{{ 7 | Modulo:3 }} → 1",
            (input, args) => Num(input, "Modulo") % Num(Arg(args, 0) ?? 1m, "Modulo"));

        Register("Abs", "Numeric", "Returns the absolute value.",
            "{{ -17 | Abs }} → 17",
            (input, _) => Math.Abs(Num(input, "Abs")));

        Register("Floor", "Numeric", "Rounds down to the nearest whole number.",
            "{{ 4.7 | Floor }} → 4",
            (input, _) => Math.Floor(Num(input, "Floor")));

        Register("Ceiling", "Numeric", "Rounds up to the nearest whole number.",
            "{{ 4.2 | Ceiling }} → 5",
            (input, _) => Math.Ceiling(Num(input, "Ceiling")));

        Register("Round", "Numeric", "Rounds to the nearest whole number, or to n decimal places.",
            "{{ 3.14159 | Round:2 }} → 3.14",
            (input, args) => Math.Round(Num(input, "Round"), (int)Num(Arg(args, 0) ?? 0m, "Round"), MidpointRounding.AwayFromZero));

        Register("AtLeast", "Numeric", "Returns at minimum the given value.",
            "{{ 3 | AtLeast:5 }} → 5",
            (input, args) => Math.Max(Num(input, "AtLeast"), Num(Arg(args, 0) ?? 0m, "AtLeast")));

        Register("AtMost", "Numeric", "Returns at maximum the given value.",
            "{{ 9 | AtMost:5 }} → 5",
            (input, args) => Math.Min(Num(input, "AtMost"), Num(Arg(args, 0) ?? 0m, "AtMost")));

        Register("Format", "Numeric", "Formats a number (or date) using a .NET format string.",
            "{{ 1234.5 | Format:'#,##0.00' }} → 1,234.50",
            (input, args) =>
            {
                var format = Str(Arg(args, 0));
                if (LavaValue.TryToNumber(input, out var n))
                {
                    return n.ToString(format, CultureInfo.InvariantCulture);
                }
                if (TryToDate(input, out var dt))
                {
                    return dt.ToString(format, CultureInfo.InvariantCulture);
                }
                return Str(input);
            });

        Register("FormatAsCurrency", "Numeric", "Formats a number as currency ($ by default; pass a symbol to override).",
            "{{ 1234.5 | FormatAsCurrency }} → $1,234.50",
            (input, args) =>
            {
                var symbol = args.Length > 0 ? Str(args[0]) : "$";
                var n = Num(input, "FormatAsCurrency");
                return symbol + n.ToString("#,##0.00", CultureInfo.InvariantCulture);
            });

        Register("NumberToOrdinal", "Numeric", "Converts a number to its ordinal form.",
            "{{ 3 | NumberToOrdinal }} → 3rd",
            (input, _) => NumberToOrdinal((long)Num(input, "NumberToOrdinal")));

        Register("NumberToOrdinalWords", "Numeric", "Converts a number to ordinal words.",
            "{{ 3 | NumberToOrdinalWords }} → third",
            (input, _) => NumberToOrdinalWords((int)Num(input, "NumberToOrdinalWords")));

        Register("NumberToWords", "Numeric", "Spells out a number in English words.",
            "{{ 3200 | NumberToWords }} → three thousand two hundred",
            (input, _) => NumberToWords((long)Num(input, "NumberToWords")));

        Register("NumberToRomanNumerals", "Numeric", "Converts a number (1-3999) to Roman numerals.",
            "{{ 2026 | NumberToRomanNumerals }} → MMXXVI",
            (input, _) => ToRoman((int)Num(input, "NumberToRomanNumerals")));

        Register("RandomNumber", "Numeric", "Returns a random whole number between 0 and the input.",
            "{{ 100 | RandomNumber }}",
            (input, _) => (decimal)Random.Shared.Next((int)Num(input, "RandomNumber") + 1));
    }
}
