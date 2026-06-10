import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SaveJournalEntryRequest } from '../../../../core/api/journal-api.service';
import { Account, JournalEntry, JournalLine, JournalStatus } from '../../../../core/api/models';

type EditorLine = Pick<JournalLine, 'accountId' | 'debit' | 'credit' | 'description'>;

@Component({
  selector: 'app-journal-entry-editor',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './journal-entry-editor.component.html',
  styleUrl: './journal-entry-editor.component.scss'
})
export class JournalEntryEditorComponent implements OnChanges {
  @Input({ required: true }) ledgerId!: number;
  @Input() journalEntry?: JournalEntry;
  @Input() accounts: Account[] = [];
  @Input() loading = false;
  @Input() ledgerName = '';

  @Output() saveDraft = new EventEmitter<SaveJournalEntryRequest>();
  @Output() post = new EventEmitter<SaveJournalEntryRequest>();
  @Output() cancel = new EventEmitter<void>();

  journalNo = '';
  entryDate = this.today();
  description = '';
  status: JournalStatus = 'Draft';
  lines: EditorLine[] = [this.emptyLine(), this.emptyLine()];

  get title() {
    return this.journalEntry ? 'Edit Journal Entry' : 'New Journal Entry';
  }

  get posted() {
    return this.status === 'Posted';
  }

  get activeAccounts() {
    return this.accounts.filter((account) => account.isActive);
  }

  get debitTotal() {
    return this.validLines.reduce((sum, line) => sum + this.amount(line.debit), 0);
  }

  get creditTotal() {
    return this.validLines.reduce((sum, line) => sum + this.amount(line.credit), 0);
  }

  get difference() {
    return Math.abs(this.debitTotal - this.creditTotal);
  }

  get isBalanced() {
    return this.debitTotal > 0 && this.debitTotal === this.creditTotal;
  }

  get effectiveLines() {
    return this.lines.filter((line) => !this.isLineEmpty(line));
  }

  get validLines() {
    return this.effectiveLines.filter((line) => this.isLineValid(line));
  }

  get canSaveDraft() {
    return !this.posted && this.validLines.length > 0 && !this.hasNegativeAmount && !this.hasBothDebitAndCredit;
  }

  get canPost() {
    return !this.posted && this.postReasons.length === 0;
  }

  get postReasons() {
    const reasons: string[] = [];
    if (this.validLines.length < 2) {
      reasons.push('Need at least two lines');
    }
    if (this.hasMissingAccount) {
      reasons.push('Missing account');
    }
    if (this.hasNegativeAmount) {
      reasons.push('Negative amounts are not allowed');
    }
    if (this.hasBothDebitAndCredit) {
      reasons.push('One line cannot have both debit and credit');
    }
    if (!this.isBalanced) {
      reasons.push('Not balanced');
    }
    return reasons;
  }

  get draftNote() {
    if (this.effectiveLines.length === 0) {
      return 'Add at least one valid line to save a draft.';
    }
    if (this.effectiveLines.length !== this.validLines.length) {
      return 'Incomplete lines will not be saved as draft.';
    }
    if (!this.isBalanced) {
      return 'Draft can be saved before the entry is balanced.';
    }
    return '';
  }

  private get hasMissingAccount() {
    return this.effectiveLines.some((line) => !line.accountId);
  }

  private get hasNegativeAmount() {
    return this.effectiveLines.some((line) => this.amount(line.debit) < 0 || this.amount(line.credit) < 0);
  }

  private get hasBothDebitAndCredit() {
    return this.effectiveLines.some((line) => this.amount(line.debit) > 0 && this.amount(line.credit) > 0);
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['journalEntry']) {
      this.patchFromJournalEntry();
    }
  }

  trackLine(index: number) {
    return index;
  }

  accountLabel(account: Account) {
    return `${account.code} - ${account.name}`;
  }

  onDebitChange(line: EditorLine) {
    line.debit = Math.max(0, this.amount(line.debit));
    if (line.debit > 0) {
      line.credit = 0;
    }
  }

  onCreditChange(line: EditorLine) {
    line.credit = Math.max(0, this.amount(line.credit));
    if (line.credit > 0) {
      line.debit = 0;
    }
  }

  addLine() {
    this.lines.push(this.emptyLine());
  }

  removeLine(index: number) {
    if (this.lines.length <= 2 || this.posted) {
      return;
    }
    this.lines.splice(index, 1);
  }

  submitDraft() {
    if (!this.canSaveDraft) {
      return;
    }
    this.saveDraft.emit(this.createRequest());
  }

  submitPost() {
    if (!this.canPost) {
      return;
    }
    this.post.emit(this.createRequest());
  }

  isLineEmpty(line: EditorLine) {
    return !line.accountId
      && this.amount(line.debit) === 0
      && this.amount(line.credit) === 0
      && !line.description?.trim();
  }

  isLineValid(line: EditorLine) {
    const debit = this.amount(line.debit);
    const credit = this.amount(line.credit);
    return !!line.accountId
      && debit >= 0
      && credit >= 0
      && ((debit > 0 && credit === 0) || (credit > 0 && debit === 0));
  }

  lineState(line: EditorLine) {
    if (this.isLineEmpty(line)) {
      return 'muted';
    }
    return this.isLineValid(line) ? 'valid' : 'attention';
  }

  private createRequest(): SaveJournalEntryRequest {
    return {
      journalNo: this.journalNo.trim(),
      entryDate: this.entryDate,
      description: this.description.trim(),
      lines: this.validLines.map((line) => ({
        accountId: Number(line.accountId),
        debit: this.amount(line.debit),
        credit: this.amount(line.credit),
        description: line.description?.trim() ?? ''
      }))
    };
  }

  private patchFromJournalEntry() {
    if (!this.journalEntry) {
      this.journalNo = '';
      this.entryDate = this.today();
      this.description = '';
      this.status = 'Draft';
      this.lines = [this.emptyLine(), this.emptyLine()];
      return;
    }

    this.journalNo = this.journalEntry.journalNo;
    this.entryDate = this.toDateInputValue(this.journalEntry.entryDate);
    this.description = this.journalEntry.description ?? '';
    this.status = this.journalEntry.status;
    this.lines = this.ensureTwoLines(this.journalEntry.lines.map((line) => ({
      accountId: line.accountId,
      debit: this.amount(line.debit),
      credit: this.amount(line.credit),
      description: line.description ?? ''
    })));
  }

  private ensureTwoLines(lines: EditorLine[]) {
    const next = [...lines];
    while (next.length < 2) {
      next.push(this.emptyLine());
    }
    return next;
  }

  private emptyLine(): EditorLine {
    return { accountId: 0, debit: 0, credit: 0, description: '' };
  }

  private amount(value: number | string | null | undefined) {
    const next = Number(value ?? 0);
    return Number.isFinite(next) ? next : 0;
  }

  private today() {
    return new Date().toISOString().slice(0, 10);
  }

  private toDateInputValue(value: string) {
    return value ? value.slice(0, 10) : this.today();
  }
}
