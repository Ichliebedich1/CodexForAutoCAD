using System;
using System.Globalization;
using Codex.AutoCAD.Contracts;

namespace Codex.AutoCAD.Host2016
{
    internal static class HostCommandFailureStages
    {
        internal const string DrawingIndexStart = "drawing_index_start";
        internal const string DrawingQuery = "drawing_query";
        internal const string DrawingQueryNext = "drawing_query_next";
        internal const string HostCommand = "host_command";
    }

    internal sealed class HostCommandDiagnostic
    {
        internal HostCommandDiagnostic(
            string errorCode,
            string errorStage,
            DiagnosticDataClassification diagnosticClassification,
            DiagnosticRedactionKinds diagnosticRedactions)
        {
            ErrorCode = errorCode;
            ErrorStage = errorStage;
            DiagnosticClassification = diagnosticClassification;
            DiagnosticRedactions = diagnosticRedactions;
        }

        internal string ErrorCode { get; private set; }

        internal string ErrorStage { get; private set; }

        internal DiagnosticDataClassification DiagnosticClassification { get; private set; }

        internal DiagnosticRedactionKinds DiagnosticRedactions { get; private set; }

        internal string FormatForUser(string operationName, string safetyStatement)
        {
            var operation = string.IsNullOrWhiteSpace(operationName)
                ? "Host 命令"
                : operationName.Trim();
            var safety = string.IsNullOrWhiteSpace(safetyStatement)
                ? "操作已按 fail-closed 终止。"
                : safetyStatement.Trim();
            var formatted = operation
                + "失败（error_code="
                + ErrorCode
                + ", error_stage="
                + ErrorStage
                + ", diagnostic_classification="
                + DiagnosticClassification
                + ", diagnostic_redactions="
                + ((int)DiagnosticRedactions).ToString(CultureInfo.InvariantCulture)
                + "）。"
                + safety
                + " 原始异常详情已隐藏。";
            return DiagnosticSanitizer
                .SanitizeText(
                    DiagnosticDataClassification.Exception,
                    formatted)
                .SafeText;
        }
    }

    internal static class HostCommandDiagnosticFormatter
    {
        internal static HostCommandDiagnostic FromUnexpectedException(
            Exception exception,
            string errorStage)
        {
            var sanitized = DiagnosticSanitizer.SanitizeException(
                DiagnosticDataClassification.Exception,
                exception);
            return new HostCommandDiagnostic(
                "internal_error",
                NormalizeStage(errorStage),
                sanitized.Classification,
                sanitized.Redactions);
        }

        private static string NormalizeStage(string errorStage)
        {
            if (string.Equals(
                    errorStage,
                    HostCommandFailureStages.DrawingIndexStart,
                    StringComparison.Ordinal)
                || string.Equals(
                    errorStage,
                    HostCommandFailureStages.DrawingQuery,
                    StringComparison.Ordinal)
                || string.Equals(
                    errorStage,
                    HostCommandFailureStages.DrawingQueryNext,
                    StringComparison.Ordinal))
            {
                return errorStage;
            }

            return HostCommandFailureStages.HostCommand;
        }
    }
}
