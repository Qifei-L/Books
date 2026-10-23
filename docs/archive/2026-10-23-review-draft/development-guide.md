# How the Books documents fit software development

Status: Learning and delivery guide. This follows common software-engineering practice, not a universal mandatory standard. Books is still at requirements and design review; code and tests are not complete.

Use one thread through the documents: **a user clicks Save, the input is preserved, and valid accounting changes take effect together**.

## 1. Requirements / PRD: what and why

**Purpose:** describe the user's goal, scope and observable rules without prescribing tables or libraries.  
**When:** before committing to implementation; update when a business decision changes.  
**Done when:** the scope, expected outcomes and genuine open choices are clear enough to review.

Books example: normal Save attempts posting; Save Draft does not. A failed edit must preserve the previous valid accounting result.

Read [Journal requirements](05-journals-posting.md), [project scope](01-project-scope.md) and the other module requirements from the [contents](README.md#3-business-requirements). These files form the PRD set; no duplicate monolithic PRD is needed.

## 2. System design: responsibilities and connections

**Purpose:** decide which module owns each responsibility and which invariants cross module boundaries.  
**When:** once the main requirements are understood.  
**Done when:** the boundaries and end-to-end data flow explain how the rules can hold, with assumptions labelled.

Books example: a future subsystem owns its business document; the shared Journal service owns posting validation and effective accounting changes. Neither Frontend nor a source subsystem writes balances directly.

Read [System Design Principles](00-system-design-principles.md). This is the architecture overview, not an implementation report.

## 3. Detailed technical design: an implementable proposal

**Purpose:** specify typed contracts, versions, storage constraints, transaction boundaries and failure behavior.  
**When:** before coding the relevant slice.  
**Done when:** engineering can trace normal, concurrent and failed operations, and knows which assumptions must be tested.

Books example: save a durable candidate, validate common + subsystem + Account rules, then atomically commit official source and Journal effects. A lost response requires reconciliation, not another blind posting.

Read [Journal posting design](design/journal-posting-contract.md) and the [foundation contract](design/phase-1-technical-contract.md). Both remain proposals. Provider interoperability and crash behavior are not verified.

## 4. Development tasks: small verifiable slices

**Purpose:** turn the design into ordered work with a clear result and verification for each task.  
**When:** after design review, before or during separately authorized coding.  
**Done when:** each task has scope, dependencies, expected evidence and a clear boundary.

Existing [foundation milestones](design/phase-1-technical-contract.md#11-implementation-milestones-after-coding-approval) cover organization/access work.

Illustrative future Journal tasks, not coding authorization:

1. Define typed candidates and requirement resolution. Evidence: tests for union, conflicts and safe field errors.
2. Add durable candidate/version handling. Evidence: concurrent draft saves cannot overwrite each other.
3. Add atomic source/Journal effects and request fencing. Evidence: failure injection preserves previous official versions and prevents duplicate effects.
4. Connect report reconciliation. Evidence: TB, GL Detail and statements match the same effective result.

Do not start all modules together. Sales, Receipt and inventory are not added by this example.

## 5. Testing and acceptance: demonstrate the rules

**Purpose:** collect evidence, not just describe expected behavior.  
**When:** design cases early; execute against the implemented version later.  
**Done when:** expected and actual results, tested commit/environment, failures and untested areas are recorded and reviewed.

Books example: interrupt Save before and after commit, then retry. Verify exactly one effective result and preserved input. Test an invalid edit, Close race and revoked access.

Read [Testing and acceptance](08-testing-acceptance.md). Every current case remains Not run. A good design or static document review is not a passing runtime test.

## 6. Release and operations: run and recover safely

**Purpose:** define deployment, observability, backup, recovery and ownership.  
**When:** plan before release; execute only after separate release authorization.  
**Done when:** release evidence and rollback/recovery procedures are reviewed for the actual environment.

Books example: verify database recovery and attachment-link integrity without claiming an external upload joins the accounting transaction.

Read [Deployment and operations](09-deployment-operations.md). No deployment, credentials or recovery drill has been performed by these documents.

## ADR: record why a design choice was made

An Architecture Decision Record is a small record of **context, decision, status, alternatives and consequences**. Use one for an important choice, not every field.

The [decision record](10-decisions.md) is the current source of approval status. Its entries may be expanded into individual ADRs when a consequential design choice is settled; do not duplicate the entire history.

For example, a future ADR for atomic official source + Journal updates would explain why split independent commits risk disagreement, why durable candidate input is separate, and what the same-database assumption limits. The business invariant is accepted; the precise transaction/fencing implementation remains proposed. An ADR must preserve that distinction rather than label the whole implementation Accepted.

## A practical review loop

Requirement → system responsibility → technical mechanism → task → test evidence → release decision.

When a requirement changes, update its technical design and tests together. Keep stable links, describe what changed and distinguish Confirmed, Proposed and Not run. Prefer a small complete slice over more documents or unreviewed code.

[Documentation contents](README.md)
