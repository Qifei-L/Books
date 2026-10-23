# Party: complete requirements review

Status: Complete review draft, not an implemented feature. The accepted JournalLine Party relationship is separated from proposed master-data rules. No code, migration, real data change or deployment is authorized.

**Purpose of this document:** define what the Party module does and how users know it worked.  
**Read next:** [Party technical design](design/party-technical-design.md) for the proposed C# contracts, database constraints and transactions.  
**Completion standard:** requirements, exceptions and acceptance cases are reviewable; unapproved choices and untested behavior are explicit. This does not mean the feature is complete in software.

## 1. Scope

Party is a minimal record of the person or organization on the other side of an accounting entry. It answers “who?”, while Account answers “where is it recorded?”.

Proposed first version:

- Create, find, read and edit a Party.
- Deactivate and reactivate it.
- Remove an unused Party from normal use under the safe-delete proposal.
- Reference Party from Journal lines and later source documents.
- Keep identity and change history stable.

Not included: CRM, contact-person lists, multiple address books, credit limits, credit scoring, approvals, stored editable balances, invoice allocation, collections, automatic merges or cross-company sharing. No customer/supplier registration workflow is added.

The same Party may act as customer, supplier or both. This is a proposed reuse model, not a mandatory role enum. Do not create duplicates merely to separate those roles.

## 2. Confirmed Journal relationship

- JournalLine has optional **PartyId**, not separate CustomerId and SupplierId fields.
- A line requires Party when the applicable business and Account rules require it. This includes relevant receivable/payable lines; it is not a requirement for every income or expense line.
- Different lines in the same Journal may refer to different Parties.
- A future Sales source carries its customer to its receivable line. A Receipt settling that source retains its corresponding Party and A/R account.
- Party identity alone does not identify an invoice, establish allocation or mark anything paid.
- Common Journal, subsystem and Account validation combine without overrides. Party does not bypass current ledger permissions, account rules, one transaction currency/shared rate, balance validation or Close.
- A failed Journal edit preserves its previous valid source and posting. A rejected Party reference cannot create a partial accounting change.

The complete Sales/Receipt and allocation policies are not selected by these relationships. [Journal requirements](05-journals-posting.md)

## 3. Proposed field dictionary

The following precise defaults are recommendations for review, not previously confirmed business rules. Technical names and storage are defined in the linked design.

| Field | Required? | Proposed meaning and constraint |
| --- | --- | --- |
| Party ID | Server assigned | Stable UUID, immutable. Never use Name, Code or Tax Number as the identity. |
| Organization / Ledger | Server-validated scope | One owning ledger and its organization; immutable after creation. No move-to-another-ledger operation. |
| Name | Yes | 1–200 Unicode scalar values after trimming outer whitespace and NFC normalization. Preserve case and internal spacing. |
| Code | No | Up to 64 ASCII letters/digits or hyphen, underscore, period. Trim and compare using uppercase canonical form within the ledger. |
| Tax Number | No | Up to 80 Unicode scalar values after trimming. Preserve text and leading zeros. No jurisdiction-specific validity claim. |
| Address | No | Up to 1,000 Unicode scalar values of multiline text. No separate address catalog. |
| Contact | No | Up to 500 Unicode scalar values of plain text, such as a contact name, email or phone. No automatic messaging. |
| Active | Yes | Proposed initial value true. Controls new selection, not historical visibility. |
| Version | Server assigned | Increments after each actual state change; successful no-op state actions keep it unchanged. Used to prevent stale overwrite. |
| Created / updated actor and time | Server assigned | Audit identity and UTC system timestamps, not accounting dates. |
| Deleted state | Server managed | Tombstone for an eligible unused record; not permanent removal of audit evidence. |

Optional blank values normalize to null. Name cannot be blank. Name, Tax Number, Address and Contact are not unique. Two genuine Parties may share a name or tax-reference string; do not auto-merge them.

**Code policy is proposed:** when present, canonical Code is unique per ledger. The same Code can exist in another ledger. The current canonical Code at deletion remains reserved; former codes changed before deletion are not all reserved by a separate alias registry. Reusing a tombstone's Code would require an explicit future policy. This is separate from the already confirmed Account Code rule.

Display Name and, when present, Code together. Tax Number and contact data are not public search metadata; return them only within authorized ledger access. No real personal or customer records belong in documentation examples.

## 4. Permissions and ownership

The existing organization and ledger permission model remains authoritative.

Proposed Party operation split:

- Organization administrators and members with ledger edit access may create, change, activate, deactivate and safely delete Parties in that ledger.
- Read-only members may list and read permitted Party records but cannot change them.
- No access to another ledger's Party through a guessed ID, matching Code, source link, search count, request replay or MCP call.
- Party maintenance grants no membership, permission-management, Close or reopening authority.
- The backend checks current access on every request and again within the mutation boundary.

These maintenance rights are a proposal inside the existing roles, not a new role hierarchy.

## 5. User operations

### Create

Enter Name and optional details. The server validates scope and fields. On success, return the new Party ID, stored normalized values and Version.

Within the retained idempotency window, an exact retry creates no duplicate. Same request identity with changed content is a conflict. An uncertain response must be checked before starting a new creation request. After the replay window expires, a missing receipt is not proof that creation failed; do not automatically recreate an unresolved request. Different requests with similar names are not automatically duplicates.

### Find and read

Search by Name or Code within the current ledger. Proposed initial filter: case-insensitive name/code prefix, active/all/inactive state and stable pagination. Default the selection list to active records; historical detail can still resolve inactive records.

Show a clear empty list when nothing matches. A denied request is not an empty successful list. Version and current state come from the server, not a stale selection cache.

### Edit

Allow Name, Code, Tax Number, Address and Contact changes under the proposed permissions. Never change ID or owning ledger. Require the expected Version. If another change has occurred, reject the stale update and let the user refresh; do not silently overwrite.

Master changes do not repost Journals, alter balances or replace historical Party IDs. New edits must satisfy current field/code rules.

### Deactivate and reactivate

Proposed deactivation prevents new Party selection while retaining history. Do not require or calculate a zero balance to deactivate, because Party is not a separate balance store. Show affected open work where authorized; never silently rewrite it.

Existing drafts and posting attempts are revalidated. Proposed rule: a new or changed Party reference must be active when posting. An unchanged reference on an already effective posting may remain during a permitted correction, if all other rules pass. This exception does not let a new draft use an inactive Party or bypass a changed source relationship.

Historical reads always preserve the reference. Reactivation restores availability for future valid work; it does not post drafts or grant ledger access.

### Delete an unused Party

Proposed delete means a tombstone, not physical erasure. Require confirmation in a future Frontend and recheck on the server.

Allow it only when there is no retained draft/candidate, source record, Journal/revision/history record, allocation reference or attachment link that needs the Party identity. Party has no direct attachment feature in this version, but an existing integrated reference must not be ignored. Audit of Party master changes alone is retained and is not a reason to purge the Party's identity.

Block deletion of a used/referenced Party even if inactive or if a computed balance would be zero. Do not cascade-delete references, detach them silently or auto-merge identities. Keep a minimal tombstone, audit and reserved Code; a deleted Party cannot be reactivated through the ordinary Active action. Recovery of a mistaken deletion is a separate future policy.

## 6. History and display

Proposed display rule:

- Current Party lists and selection show the latest master Name/Code.
- An effective source/Journal revision retains the Party ID plus the relevant Name/Code snapshot accepted for that revision.
- Historical transaction views show the saved snapshot, with an authorized link to the current Party. Do not silently rewrite snapshots after a rename.
- A new permitted transaction revision records its own snapshot and retains the prior revision in history.
- Tax Number/Address/Contact snapshots are not copied onto every Journal line by default. A future source document can define which facts it needs to preserve.

Snapshot representation is a technical proposal. Current-master labels and historical labels must be clearly distinguished.

## 7. Failure behavior

Party writes return success only after the Party state, audit and successful request receipt commit together. Validation errors leave the record unchanged. A stale Version or duplicate Code reports a conflict with safe field details.

Party master validation errors are ordinary API results, not automatic Journal Error entries. A Journal attempt that fails its Party validation follows the Journal Error rules. Unknown Journal outcomes remain Pending until reconciled.

If permissions change, a stale form or cached request does not permit new access. No response or audit message may disclose another ledger's Party or authentication secrets.

## 8. Acceptance matrix

All cases are **Not run**. Master-data policies are Proposed; confirmed Journal relationships retain their existing status.

| ID | Scenario | Expected result |
| --- | --- | --- |
| PARTY-01 | Create with Name and no optional fields. | One stable Party identity; normalized fields and Version returned. |
| PARTY-02 | Blank Name, excess length or invalid Code characters. | Safe field error; no mutation or success audit. |
| PARTY-03 | Same canonical Code in one ledger versus another ledger. | First is a conflict; second allowed under the proposed Code policy. |
| PARTY-04 | Same Name or Tax Number on two distinct records. | Not automatically rejected or merged. |
| PARTY-05 | Read-only write or cross-ledger ID/search/cursor. | Denied without leaking other-ledger content. |
| PARTY-06 | Two updates using one old Version. | At most one wins; other receives a conflict. |
| PARTY-07 | Repeat the same create request or lose its response. | Resolve one committed result, not a duplicate. |
| PARTY-08 | Add Party to two Journal lines, or omit when optional. | Different valid Parties allowed; optional remains optional. |
| PARTY-09 | Omit Party on a Party-required line. | Journal validation fails with line-specific error; no new posting. |
| PARTY-10 | A source-linked Receipt changes Party/A/R unexpectedly. | Source relationship is rejected; no partial replacement. |
| PARTY-11 | Deactivate then create a new reference. | New selection/posting fails under the proposed Active policy. |
| PARTY-12 | Read history or correct a permitted posting with unchanged inactive Party. | Historical identity remains; unchanged-reference exception applies only as specified. |
| PARTY-13 | Rename Party after posting. | Current list changes; historical snapshot and accounting amounts do not. |
| PARTY-14 | Delete referenced Party, including retained draft/history references. | Blocked; references and audit remain intact. |
| PARTY-15 | Race delete/deactivate with candidate/reference creation or posting. | One serialized valid outcome; no dangling or unchecked reference. |
| PARTY-16 | Delete unused Party, then retry or try to reuse its Code. | Stable tombstone/receipt; reserved Code is not reused under this proposal. |
| PARTY-17 | Fail audit or transaction commit. | No partial master change or false success; resolve an uncertain outcome before retry. |

The [technical design](design/party-technical-design.md) defines the proposed implementation checks and concurrency tests. No runtime evidence exists yet.

## 9. Business review choices

The technical proposal supplies defaults; the owner need not design APIs, locks or tables.

1. Is the proposed optional, ledger-unique Code useful, including reserving deleted codes?
2. Should new work with inactive Parties be blocked while allowing the unchanged-reference correction exception?
3. Is the minimal Name/Code/tax reference/address/contact master enough, with no additional CRM fields?

Specific business/Account rules that require Party are decided with their workflows. Full invoice allocation and source correction ownership remain separate questions, not added features here.

[Documentation contents](README.md) · [Journal requirements](05-journals-posting.md)

