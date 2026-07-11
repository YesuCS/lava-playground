using System.Globalization;
using System.Text.RegularExpressions;

namespace LavaPlayground.Api.Lava;

public partial class LavaFilterRegistry
{
    private void RegisterColorFilters()
    {
        Register("Lighten", "Color", "Increases a color's lightness by a percentage.",
            "{{ '#ff6b35' | Lighten:'20%' }}",
            (input, args) => HslAdjust(input, l: +Percent(Arg(args, 0))));

        Register("Darken", "Color", "Decreases a color's lightness by a percentage.",
            "{{ '#ff6b35' | Darken:'20%' }}",
            (input, args) => HslAdjust(input, l: -Percent(Arg(args, 0))));

        Register("Saturate", "Color", "Increases a color's saturation by a percentage.",
            "{{ '#888899' | Saturate:'30%' }}",
            (input, args) => HslAdjust(input, s: +Percent(Arg(args, 0))));

        Register("Desaturate", "Color", "Decreases a color's saturation by a percentage.",
            "{{ '#ff6b35' | Desaturate:'30%' }}",
            (input, args) => HslAdjust(input, s: -Percent(Arg(args, 0))));

        Register("AdjustHue", "Color", "Rotates a color's hue by degrees.",
            "{{ '#ff6b35' | AdjustHue:'40deg' }}",
            (input, args) => HslAdjust(input, hueDegrees: Degrees(Arg(args, 0))));

        Register("Grayscale", "Color", "Removes all saturation from a color.",
            "{{ '#ff6b35' | Grayscale }}",
            (input, _) =>
            {
                var c = LavaColor.Parse(Str(input));
                var (h, _, l) = c.ToHsl();
                return LavaColor.FromHsl(h, 0, l, c.A).ToDisplay();
            });

        Register("FadeIn", "Color", "Increases a color's opacity by a percentage.",
            "{{ 'rgba(255,107,53,0.4)' | FadeIn:'20%' }}",
            (input, args) =>
            {
                var c = LavaColor.Parse(Str(input));
                return (c with { A = Math.Clamp(c.A + Percent(Arg(args, 0)), 0, 1) }).ToDisplay();
            });

        Register("FadeOut", "Color", "Decreases a color's opacity by a percentage.",
            "{{ '#ff6b35' | FadeOut:'20%' }}",
            (input, args) =>
            {
                var c = LavaColor.Parse(Str(input));
                return (c with { A = Math.Clamp(c.A - Percent(Arg(args, 0)), 0, 1) }).ToDisplay();
            });

        Register("Mix", "Color", "Mixes another color in by a percentage.",
            "{{ '#ff6b35' | Mix:'#0000ff','25%' }}",
            (input, args) =>
            {
                var baseColor = LavaColor.Parse(Str(input));
                var mixColor = LavaColor.Parse(Str(Arg(args, 0)));
                return baseColor.MixWith(mixColor, Percent(Arg(args, 1) ?? "50%")).ToDisplay();
            });

        Register("Tint", "Color", "Mixes white into a color by a percentage.",
            "{{ '#ff6b35' | Tint:'25%' }}",
            (input, args) => LavaColor.Parse(Str(input))
                .MixWith(new LavaColor(255, 255, 255, 1), Percent(Arg(args, 0)))
                .ToDisplay());

        Register("Shade", "Color", "Mixes black into a color by a percentage.",
            "{{ '#ff6b35' | Shade:'25%' }}",
            (input, args) => LavaColor.Parse(Str(input))
                .MixWith(new LavaColor(0, 0, 0, 1), Percent(Arg(args, 0)))
                .ToDisplay());
    }

    private static double Percent(object? arg)
    {
        var s = Str(arg).Trim().TrimEnd('%');
        if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            throw new LavaException($"\"{Str(arg)}\" is not a percentage.");
        }
        return value / 100.0;
    }

    private static double Degrees(object? arg)
    {
        var s = Str(arg).Trim();
        if (s.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^3];
        }
        if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            throw new LavaException($"\"{Str(arg)}\" is not a degree value.");
        }
        return value;
    }

    private static string HslAdjust(object? input, double s = 0, double l = 0, double hueDegrees = 0)
    {
        var color = LavaColor.Parse(Str(input));
        var (h, sat, light) = color.ToHsl();
        h = (h + hueDegrees / 360.0 + 1) % 1;
        sat = Math.Clamp(sat + s, 0, 1);
        light = Math.Clamp(light + l, 0, 1);
        return LavaColor.FromHsl(h, sat, light, color.A).ToDisplay();
    }
}

/// <summary>Minimal color model: parses hex / rgb() / rgba() / common names; converts to and from HSL.</summary>
public readonly record struct LavaColor(int R, int G, int B, double A)
{
    private static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["white"] = "#ffffff", ["black"] = "#000000", ["red"] = "#ff0000", ["green"] = "#008000",
        ["blue"] = "#0000ff", ["yellow"] = "#ffff00", ["orange"] = "#ffa500", ["purple"] = "#800080",
        ["gray"] = "#808080", ["grey"] = "#808080", ["silver"] = "#c0c0c0", ["maroon"] = "#800000",
        ["navy"] = "#000080", ["teal"] = "#008080", ["olive"] = "#808000", ["lime"] = "#00ff00",
        ["aqua"] = "#00ffff", ["cyan"] = "#00ffff", ["fuchsia"] = "#ff00ff", ["magenta"] = "#ff00ff",
    };

    public static LavaColor Parse(string value)
    {
        value = value.Trim();
        if (Named.TryGetValue(value, out var hex))
        {
            value = hex;
        }

        if (value.StartsWith('#'))
        {
            var digits = value[1..];
            return digits.Length switch
            {
                3 => new LavaColor(HexPair(digits[0]), HexPair(digits[1]), HexPair(digits[2]), 1),
                6 => new LavaColor(Hex(digits[..2]), Hex(digits[2..4]), Hex(digits[4..6]), 1),
                8 => new LavaColor(Hex(digits[..2]), Hex(digits[2..4]), Hex(digits[4..6]), Hex(digits[6..8]) / 255.0),
                _ => throw new LavaException($"\"{value}\" is not a valid color."),
            };
        }

        var rgba = Regex.Match(value, @"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)$", RegexOptions.IgnoreCase);
        if (rgba.Success)
        {
            return new LavaColor(
                int.Parse(rgba.Groups[1].Value),
                int.Parse(rgba.Groups[2].Value),
                int.Parse(rgba.Groups[3].Value),
                rgba.Groups[4].Success ? double.Parse(rgba.Groups[4].Value, CultureInfo.InvariantCulture) : 1);
        }

        throw new LavaException($"\"{value}\" is not a recognized color (use a name, hex, or rgb/rgba).");

        static int Hex(string pair) => int.Parse(pair, NumberStyles.HexNumber);
        static int HexPair(char c) => int.Parse($"{c}{c}", NumberStyles.HexNumber);
    }

    public (double H, double S, double L) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (Math.Abs(max - min) < 1e-9)
        {
            return (0, 0, l);
        }
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h / 6, s, l);
    }

    public static LavaColor FromHsl(double h, double s, double l, double a)
    {
        double r, g, b;
        if (s < 1e-9)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }
        return new LavaColor((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255), a);

        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }

    public LavaColor MixWith(LavaColor other, double weight) => new(
        (int)Math.Round(R + (other.R - R) * weight),
        (int)Math.Round(G + (other.G - G) * weight),
        (int)Math.Round(B + (other.B - B) * weight),
        A + (other.A - A) * weight);

    public string ToDisplay() => A >= 0.999
        ? $"#{R:x2}{G:x2}{B:x2}"
        : $"rgba({R}, {G}, {B}, {Math.Round(A, 2).ToString(CultureInfo.InvariantCulture)})";
}
