# Chart of Accounts

Status: Review draft. Ledger access rules, the Cost & Expense label, two P&L groups and initial System Account purposes are confirmed. The account tree below is the accepted planning baseline. Account Usage, optional Description, Sort Order and user-editable Code are also accepted. Other field requirements and maintenance rules remain proposed.

The tree is accepted for planning, not automatic account generation. Repeated account names and deactivation with a remaining balance remain review recommendations, not confirmed maintenance policies. These accounting features belong to a later accounting stage, not the organization foundation's phase-one API contract.

## Confirmed

Each account belongs to one ledger. Organization administrators can manage their own organization's ledgers. Members can read or edit accounts only within their assigned ledger access. Edit access does not allow a member to grant permissions.

Use **Cost & Expense** instead of Expense. The P&L has two top-level groups: **Income (add)** and **Cost & Expense (subtract)**. These are reporting groups, not debit/credit rules. Preserve signed balances and contra-account effects; do not turn every amount into a positive value.

<a id="account-identity-code-and-default-usage"></a>

## Account identity, Code and Account Usage

Confirmed: Code is entered and edited by the user. Account ID is the stable internal identity used by journals; changing Code must not move or break historical references.

Confirmed: **Account Usage** describes a business purpose, such as **Receivable** or **Revenue**. It is not a caller/module permission. It is many-to-many:

- One Usage can apply to several accounts.
- One account can have several Usages.
- Do not add the same Usage twice to the same account.
- Code is still unique within its ledger; Usage is not unique across accounts.

For example, a future Sales A/R selection can offer several accounts with Receivable usage. Save the actual selected Account ID. Concrete role-to-purpose mapping is defined with that subsystem, not inferred from the caller's name.

Blank Usage means **no purpose has been declared**. It does not mean suitable for every subsystem role, and it does not automatically prevent a manual Journal from using the account. Manual journals still pass common and Account requirements.

Do not add separate Default Usage, Default Account, Link or Account Non-Usage fields/settings. Account Usage is distinct from Type (accounting classification), System Purpose (fixed system function), permissions and Required Details (validation requirements). Changing Usage never rewrites historical Account selections.

Confirmed fields: **Description** is optional; **Sort Order** controls display order, not accounting calculations.

Subsystems own their business validation and mapping; Journal owns common posting and Account-detail validation. All applicable requirements still combine, without overrides. A suggested account remains a suggestion, not the only choice. Account validity, current ledger access and posting checks always apply. No subsystem or automatic posting feature is enabled by adding Usage.

**Confirmed:** Code must be unique within the same ledger. Different ledgers may use the same Code. Code remains user-editable; edits must satisfy this uniqueness rule.

- Allow user-defined codes. Store codes as text so leading zeroes survive.
- Enforce Code uniqueness within a ledger. Stable Account ID remains distinct from Code.
- Proposed: trim surrounding spaces and define exact comparison rules. Do not silently change an existing code.
- Proposed: show Code and Name together when selecting an account. Duplicate-name rules remain open; stable IDs must distinguish selected accounts.
- Code reuse rules remain open and must not change historical Account IDs.

Example: Renaming account 1000 from “Bank” to “Main Bank” must not move its journal history to another account.

Code length and allowed characters remain open. The chosen rule must work consistently in entry forms, imports and searches.

## Accepted planning baseline: Types and tree

The tree uses five account types: Assets, Liabilities, Equity, Income and Cost & Expense. These types drive report classification. The five top-level types and the tree below are accepted for planning. This is not a prescribed local chart of accounts.

### Account tree

Legend:
- **Type:** the accounting classification.
- **Group:** an organizing heading, not an additional account type.
- **Account:** an ordinary account in the planning baseline.
- **System:** a functional mapping on an account, not a separate type or branch.
- **Future / If needed:** a conditional item, not enabled or created now.
- **Calculated:** a report item, not a posting account.

```text
Assets [Type]
├─ Current Assets [Group]
│  ├─ Cash and Bank [Group]
│  │  ├─ Cash [Account]
│  │  └─ Bank Account [Account]
│  ├─ Accounts Receivable [System control, Future]
│  ├─ Inventory [System control, Future]
│  ├─ Prepaid Expenses [Account]
│  └─ Suspense [System]
└─ Non-current Assets [Group]
   ├─ Property, Plant & Equipment [Account]
   └─ Accumulated Depreciation [Account]

Liabilities [Type]
├─ Current Liabilities [Group]
│  ├─ Accounts Payable [System control, Future]
│  ├─ Accrued Expenses [Account]
│  └─ Tax Payable [Account]
└─ Non-current Liabilities [Group]
   └─ Long-term Loans [Account]

Equity [Type]
├─ Share Capital [Account]
├─ Retained Earnings [System]
├─ Opening Balance Clearing [System, Temporary]
└─ Current Year Profit [Calculated, Non-posting]

Income [Type, P&L add]
├─ Sales Revenue [Account]
├─ Service Revenue [Account]
└─ FX Gain [System]

Cost & Expense [Type, P&L subtract]
├─ Cost of Sales [Group]
│  └─ Cost of Goods Sold [Account]
├─ Operating Expenses [Group]
│  ├─ Salaries [Account]
│  ├─ Rent [Account]
│  └─ Depreciation [Account]
└─ Other Costs & Expenses [Group]
   ├─ Rounding Differences [System, If needed]
   └─ FX Loss [System]
```

There is no Other Income group. FX gains appear directly under Income. Asset names are generic and do not assume a particular bank or asset inventory.

This is a planning baseline, not a jurisdiction-specific template or an instruction to create every item. Future control accounts wait for their modules. Tax Payable does not by itself enable a tax-control module. The selected first-release FX policy uses one FX Gain and one FX Loss purpose, without a realized/unrealized split; see [ledger policy](16-ledger-accounting-policy.md). This does not automatically create accounts. Rounding Differences is conditional on a real automatic-rounding need.

Current Year Profit appears only to show report placement. It is calculated, not an account to post to; closing and equity calculations use the selected [calculated carryforward policy](16-ledger-accounting-policy.md#3-opening-balances-and-one-year-end-calculation). Suspense's place under Current Assets is part of the accepted baseline, not permission to hide an opposite balance or post an invalid record.

### Proposed operational safeguards

Use the accepted parent-child tree to organize accounts. A parent and its children belong to the same ledger and account type. An account cannot be its own parent or form a loop.

Only leaf accounts accept postings. Parent accounts show the total of their descendants and cannot also receive direct entries. Do not add children below an account that already has posted entries without a separately approved restructuring process.

Use debit-positive amounts for internal reconciliation. Show a clear debit or credit balance to the user. An account's usual balance direction is a display aid, not a reason to reject a valid opposite balance. Detailed report signs are described in [Reports](06-reports.md).

Custom account types, jurisdiction-specific templates, control accounts and subledgers are not included in this proposed first-release scope.

<a id="system-accounts-and-default-accounts"></a>

## System Accounts

System Accounts and the three initial purposes below remain accepted. They are distinct from Account Usage business purposes. A system function uses its mapped account identity in the same ledger, not a fixed name or code. This does not require every account at launch or authorize automatic creation. The earlier Default Accounts proposal is superseded; no separate default-account configuration is part of the current model.

Accepted System Account purposes:

| System purpose | Accepted purpose and limit |
| --- | --- |
| Retained Earnings | Prior-year results within an agreed year-end process. Use calculated carryforward under the ledger policy; do not double-count current-year profit. |
| Opening Balance Clearing | Temporary offset while setting up opening balances. It must be zero when setup is complete; it is not a way to conceal an unresolved difference. |
| Rounding Differences | Only if an approved feature actually needs automatic rounding adjustments. Do not create it by default or use it to hide an unbalanced journal. |

**Current Year Profit** is shown in the accepted tree as a calculated report item, not an ordinary posting account. Its relationship to Retained Earnings follows the selected calculated carryforward policy, with no automatic physical clearing Journal.

The earlier realized/unrealized FX split is superseded by the single FX Gain/FX Loss pair. Conditional future scope includes receivable, payable and inventory control placeholders. Their activation depends on the relevant modules. A tax-control mapping remains a further proposal. Their posting and clearing policies need separate review. Account Usage examples do not enable these modules.

Account codes, mapping permissions and safeguards for changing a used mapping remain proposals. No accounts or mappings are created by this document.

## Suspense and Error

Confirmed labels: **Suspense** is posted accounting with unresolved account classification. **Error** is incomplete data, unusable account references or definite validation/processing failures needing correction. **Pending** is an unknown or unfinished outcome and must be reconciled, not treated as Error.

Confirmed distinction:
- **Suspense Account:** a temporary accounting destination only when a transaction can otherwise be validly posted and the final classification is unknown.
- **Error review list:** records needing correction or investigation. Missing amounts, unbalanced entries, unusable accounts and failed validation do not become posted entries merely because they appear in Error.

Whether these categories share a page or use separate pages remains a presentation proposal, not an approved Frontend layout. Show **Posted**, **Not posted**, or **Pending / Unknown** based on evidence. A timeout or lost response must not be labelled Not posted without checking whether the operation committed.

Keep the source reference, relevant original values, error reason and resolution history under the same organization and ledger access rules. Retain only the data needed for resolution; never expose another organization's records or store credentials and authentication tokens in error details.

Correction requires validation again. A posted item follows the confirmed Close-based journal edit/delete policy or an adopted optional correction workflow. Preserve audit evidence; do not silently overwrite history. An unposted item may be posted only after validation succeeds. An uncertain outcome must be reconciled before retrying. Detailed clearing rules remain proposed; Suspense is not a way to force invalid transactions into reports.

## Proposed: Create, change and deactivate

See the proposed [Edit Account](11-workflows.md#8-edit-account) and [Deactivate Account](11-workflows.md#9-deactivate-account) and [Delete Account](11-workflows.md#10-delete-account) workflows. All operation restrictions remain proposed unless separately confirmed.

Ledger editors and organization administrators would create and maintain accounts. Read-only members would view them only.

Before an account has posted entries, allow corrections to its code, name, type and parent, subject to the agreed Code policy, hierarchy and existing draft-reference checks.

After first posting:

- Allow Name, Description and Sort Order edits and record changes. Existing transactions alone do not block these edits.
- Code remains user-editable, including after use; history keeps the same Account ID. Proposed: audit Code changes.
- Keep type and parent fixed in the first release as a separate proposal to avoid silently reclassifying earlier reports.
- Deactivate instead of deleting. Deactivation stops new postings but preserves history and report visibility, even if the account still has a balance.
- Revalidate a draft at posting time. A draft referencing a now-inactive account cannot post until corrected or the account is reactivated through an authorized change.
- Permit reactivation with an audit record. It does not remove the earlier deactivation history.

Proposed safeguards: validate Account Usage mappings and protect existing references. Removing a purpose must trigger revalidation where an adopted subsystem rule requires it; it is not a blanket prohibition on all new or manual entries and must not redirect historical entries. Before changing a required active System Purpose mapping or deactivating its account, validate a suitable replacement where that system function remains required. Account Usage alone does not impose a single replacement/default account. Structural changes to Type, parent, System Purpose or any future account-level currency need separate checks; after use, reject unsupported changes rather than silently reinterpreting history. Account-level currency is not a newly approved field.

Proposed deletion is limited to unused accounts with no document or System Purpose or Account Usage references. Accounts with posted history cannot be deleted, even at zero balance. Resolve draft and mapping references before considering deletion. A Group with children cannot be deleted as a cascade. Exact storage, recovery and retention mechanisms remain for later technical design; do not promise permanent erasure or recovery. Preserve audit evidence and do not reuse a deleted account's stable identity. Inactive accounts remain available in historical searches.

Historical reports would use the current account name, with stable identity and a name-change audit trail. If exact reproduction of old report labels is needed, that requires an additional snapshot or versioning requirement.

## Review checks

- Reject duplicate Codes within a ledger, including changes that would collide. Allow the same Code in another ledger.
- A leading-zero code stays unchanged after save and export.
- Cross-ledger parents, hierarchy loops and postings to a parent are rejected.
- A name or Code change does not change balances or journal references.
- A used account cannot be deleted, retyped or moved through ordinary editing.
- An inactive account still appears in historical balances and relevant reports.
- Read-only users cannot change account data; guessed identifiers do not bypass ledger access.

These scenarios have not been executed. Proposed behavior must be approved before implementation.

## Open Questions

1. What Code format and comparison rules are needed, and are duplicate names and leaf-only posting suitable?
2. Are any additional templates or subledgers needed beyond the accepted planning tree? Conditional future items are not enabled by this approval.
3. Is fixing a used account's type and parent acceptable? Code remains editable.
4. Do historical reports need the exact account labels shown when originally issued?
5. Are additional System Account functions needed? The three accepted purposes do not approve other functions.

[Next: Journals and Posting](05-journals-posting.md) · [Document index](README.md)
