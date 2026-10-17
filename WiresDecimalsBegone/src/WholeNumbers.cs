using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WiresDecimalsBegone
{
    // Round floating labels only. Never alter HitData or health.
    internal static class WholeNumbers
    {
        private static readonly Regex NumberLabel = new Regex(
            @"^(?<leading>\s*)(?<sign>[+\-]?)(?<value>\d+(?:[\.,]\d+)?)(?<trailing>\s*)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        internal static float Round(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return value;
            // Below 1 the decimal is the only information there is.
            if (Math.Abs(value) < 1f)
                return value;
            return (float)Math.Round(
                (double)value, 0, MidpointRounding.AwayFromZero);
        }

        internal static string RoundLabel(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            Match match = NumberLabel.Match(text);
            if (!match.Success)
                return text;

            string input = match.Groups["value"].Value;
            if (input.IndexOf('.') < 0 && input.IndexOf(',') < 0)
                return text;

            if (!double.TryParse(
                input.Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out double value) || double.IsInfinity(value) ||
                double.IsNaN(value))
                return text;
            if (Math.Abs(value) < 1.0)
                return text;

            double rounded = Math.Round(
                value, 0, MidpointRounding.AwayFromZero);
            return match.Groups["leading"].Value +
                match.Groups["sign"].Value +
                rounded.ToString("0", CultureInfo.InvariantCulture) +
                match.Groups["trailing"].Value;
        }
    }
}
