namespace Matchbook.Requisitions.Domain;

/// <summary>
/// The stable codes a refused requisition command carries. Clients branch on these, so a code is never renamed
/// once published; the message next to it is for people and may change.
/// </summary>
public static class RequisitionCodes
{
    public const string NotFound = "requisition.not_found";
    public const string NotRequester = "requisition.not_requester";
    public const string NotDraft = "requisition.not_draft";
    public const string NotCancellable = "requisition.not_cancellable";
    public const string NotPendingApproval = "requisition.not_pending_approval";

    public const string SelfApproval = "requisition.self_approval";
    public const string DuplicateApprover = "requisition.duplicate_approver";
    public const string NotYourStep = "requisition.not_your_step";
    public const string ReasonRequired = "requisition.reason_required";

    public const string CostCentreUnknown = "requisition.cost_centre_unknown";
    public const string CostCentreInactive = "requisition.cost_centre_inactive";
    public const string SupplierUnknown = "requisition.supplier_unknown";
    public const string SupplierInactive = "requisition.supplier_inactive";
    public const string RequesterIsManager = "requisition.requester_is_manager";

    public const string CostCentreInvalid = "requisition.cost_centre_invalid";
    public const string SupplierInvalid = "requisition.supplier_invalid";
    public const string JustificationInvalid = "requisition.justification_invalid";
    public const string NeededByInPast = "requisition.needed_by_in_past";
    public const string LineCountInvalid = "requisition.line_count_invalid";
    public const string LineInvalid = "requisition.line_invalid";
    public const string AmountTooLarge = "requisition.amount_too_large";
}
