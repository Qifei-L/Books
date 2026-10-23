# Chart of Accounts

Status: Review draft. Ledger access rules are confirmed. Account structure and maintenance rules below are proposed and need approval.

The proposed tree, repeated account names, fixed codes after use and deactivation with a remaining balance are new review recommendations. They are alternatives for discussion, not previously confirmed accounting policies. These accounting features belong to a later accounting stage, not the organization foundation's phase-one API contract.

## Confirmed

Each account belongs to one ledger. Organization administrators can manage their own organization's ledgers. Members can read or edit accounts only within their assigned ledger access. Edit access does not allow a member to grant permissions.

## Proposed: Account identity and code

Each account has a permanent internal identity, a code and a name. Journals refer to that identity, not just the name or code.

- Allow user-defined codes. Store codes as text so leading zeroes survive.
- Require a unique code within a ledger, including inactive accounts. The same code can exist in a different ledger.
- Trim surrounding spaces and use a documented case-insensitive uniqueness rule. Do not silently change an existing code.
- Allow repeated names when codes differ. Show code and name together when selecting an account.
- Do not reuse a retired code for a different account in the first release.

Example: Renaming account 1000 from “Bank” to “Main Bank” must not move its journal history to another account.

Code length and allowed characters remain open. The chosen rule must work consistently in entry forms, imports and searches.

## Proposed: Types and groups

Start with five account types: assets, liabilities, equity, income and expenses. These types drive report classification. They are a product proposal, not a prescribed local chart of accounts.

Use a parent-child tree to organize accounts. A parent and its children belong to the same ledger and account type. An account cannot be its own parent or form a loop.

Only leaf accounts accept postings. Parent accounts show the total of their descendants and cannot also receive direct entries. Do not add children below an account that already has posted entries without a separately approved restructuring process.

Use debit-positive amounts for internal reconciliation. Show a clear debit or credit balance to the user. An account's usual balance direction is a display aid, not a reason to reject a valid opposite balance. Detailed report signs are described in [Reports](06-reports.md).

Custom account types, jurisdiction-specific templates, control accounts and subledgers are not included in this proposed first-release scope.

## Proposed: Create, change and deactivate

Ledger editors and organization administrators would create and maintain accounts. Read-only members would view them only.

Before an account has posted entries, allow corrections to its code, name, type and parent, subject to uniqueness, hierarchy and existing draft-reference checks.

After first posting:

- Allow a name or description correction and record the old and new values.
- Keep code, type and parent fixed in the first release. This avoids silently reclassifying earlier reports.
- Deactivate instead of deleting. Deactivation stops new postings but preserves history and report visibility, even if the account still has a balance.
- Revalidate a draft at posting time. A draft referencing a now-inactive account cannot post until corrected or the account is reactivated through an authorized change.
- Permit reactivation with an audit record. It does not remove the earlier deactivation history.

For simplicity, use deactivation for unused accounts too. Hard deletion is not part of this proposal. Inactive accounts remain available in historical searches.

Historical reports would use the current account name, with stable identity and a name-change audit trail. If exact reproduction of old report labels is needed, that requires an additional snapshot or versioning requirement.

## Review checks

- A duplicate code in the same ledger is rejected; the same code in another ledger is allowed.
- A leading-zero code stays unchanged after save and export.
- Cross-ledger parents, hierarchy loops and postings to a parent are rejected.
- A name change does not change balances or journal references.
- A used account cannot be deleted, retyped or moved through ordinary editing.
- An inactive account still appears in historical balances and relevant reports.
- Read-only users cannot change account data; guessed identifiers do not bypass ledger access.

These scenarios have not been executed. Proposed behavior must be approved before implementation.

## Open Questions

1. Are custom codes, duplicate names and leaf-only posting suitable? What code format is needed?
2. Are the five account types enough, or are templates, control accounts or subledgers required?
3. Is fixing a used account's code, type and parent acceptable?
4. Do historical reports need the exact account labels shown when originally issued?

[Next: Journals and Posting](05-journals-posting.md) · [Document index](README.md)
