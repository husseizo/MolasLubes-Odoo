using System.Text.RegularExpressions;

namespace MolasLubes.Infrastructure.Integrations.SapB1.Errors;

public static class SapErrorTranslator
{
    public static SapIntegrationException Translate(string sapMessage, int sapCode, Exception? inner = null)
    {
        var msg = (sapMessage ?? "").Trim();

        // Very common SAP messages patterns
        if (ContainsAny(msg, "Posting period", "period is locked", "is not open"))
            return new SapIntegrationException(
                SapErrorTaxonomy.PERIOD_LOCKED,
                msg,
                "Posting period is closed. Please open the period or use a return/correction flow.",
                retryable: false,
                inner: inner);

        if (ContainsAny(msg, "already exists", "Duplicate", "unique", "This entry already exists"))
            return new SapIntegrationException(
                SapErrorTaxonomy.DUPLICATE_REQUEST,
                msg,
                "This request was already processed. Returning existing document is recommended.",
                retryable: true,
                inner: inner);

        if (ContainsAny(msg, "negative inventory", "Quantity falls into negative inventory", "Insufficient quantity"))
            return new SapIntegrationException(
                SapErrorTaxonomy.INVENTORY_ERROR,
                msg,
                "Insufficient stock in the selected warehouse/bin.",
                retryable: false,
                inner: inner);

        if (ContainsAny(msg, "Base document", "Base entry", "linked", "already closed", "cannot be copied"))
            return new SapIntegrationException(
                SapErrorTaxonomy.DOC_CHAIN_VIOLATION,
                msg,
                "Document chain violation. Check BaseEntry/BaseType and whether the base document is already processed.",
                retryable: false,
                inner: inner);

        if (ContainsAny(msg, "reconciled", "reconciliation", "cannot cancel payment", "payment already applied"))
            return new SapIntegrationException(
                SapErrorTaxonomy.FIN_RECONCILED,
                msg,
                "Payment is already reconciled/applied. Cancel is not allowed.",
                retryable: false,
                inner: inner);

        if (ContainsAny(msg, "required", "invalid", "must be", "cannot be empty"))
            return new SapIntegrationException(
                SapErrorTaxonomy.VALIDATION_ERROR,
                msg,
                "Validation error. Check required fields.",
                retryable: false,
                inner: inner);

        return new SapIntegrationException(
            SapErrorTaxonomy.SAP_UNKNOWN,
            msg,
            "SAP rejected the request. Check SAP message for details.",
            retryable: true,
            inner: inner);
    }

    private static bool ContainsAny(string msg, params string[] parts)
    {
        foreach (var p in parts)
        {
            if (msg.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }
}