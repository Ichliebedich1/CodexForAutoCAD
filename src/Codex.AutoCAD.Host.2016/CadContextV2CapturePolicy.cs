using System;
using System.Globalization;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    internal static class CadContextV2CapturePolicy
    {
        internal const string UnknownCommonValue = "UNKNOWN";

        internal static bool IsWithinCountLimit(int count, int maximum)
        {
            return maximum >= 0 && count >= 0 && count <= maximum;
        }

        internal static bool TryAccumulateCount(
            int current,
            int next,
            int maximum,
            out int total)
        {
            total = 0;
            if (!IsWithinCountLimit(current, maximum)
                || next < 0
                || next > maximum - current)
            {
                return false;
            }

            total = current + next;
            return true;
        }

        internal static bool IsNameDataLimit(string value)
        {
            return value != null
                && value.Length > CadContextJsonV2Constants.MaximumNameCharacters;
        }

        internal static string ClassifyContractFailure(string code)
        {
            var normalized = code ?? string.Empty;
            if (normalized.StartsWith("v2-", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(3);
            }

            if (normalized.EndsWith("_limit", StringComparison.Ordinal)
                || normalized.EndsWith("_characters", StringComparison.Ordinal)
                || normalized.EndsWith("_bytes", StringComparison.Ordinal))
            {
                return CadContextUnsupportedReasonsV2.EntityDataLimit;
            }

            switch (normalized)
            {
                case "context_v2_point2":
                case "context_v2_point3":
                case "context_v2_radius":
                case "context_v2_angle":
                case "context_v2_parameter":
                case "context_v2_rotation":
                case "context_v2_elevation":
                case "context_v2_bulge":
                case "context_v2_width":
                case "context_v2_text_height":
                case "context_v2_measurement":
                case "context_v2_hatch_scale":
                case "context_v2_ellipse_ratio":
                case "context_v2_block_scale":
                case "context_v2_table_width":
                case "context_v2_table_height":
                case "context_v2_spline_degree":
                    return CadContextUnsupportedReasonsV2.EntityDataLimit;
                default:
                    return CadContextUnsupportedReasonsV2.EntityReadFailed;
            }
        }

        internal static bool IsSafeRequiredName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > CadContextJsonV2Constants.MaximumNameCharacters)
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (character == '\0')
                {
                    return false;
                }
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length
                        || !char.IsLowSurrogate(value[index + 1]))
                    {
                        return false;
                    }
                }
                else if (char.IsLowSurrogate(character))
                {
                    if (index == 0 || !char.IsHighSurrogate(value[index - 1]))
                    {
                        return false;
                    }
                    continue;
                }

                var category = CharUnicodeInfo.GetUnicodeCategory(value, index);
                if (category == UnicodeCategory.Format
                    || category == UnicodeCategory.LineSeparator
                    || category == UnicodeCategory.ParagraphSeparator
                    || category == UnicodeCategory.Control)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
