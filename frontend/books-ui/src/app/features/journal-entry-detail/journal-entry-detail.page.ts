import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AccountApiService } from '../../core/api/account-api.service';
import { JournalApiService, SaveJournalEntryRequest } from '../../core/api/journal-api.service';
import { Account, JournalLine } from '../../core/api/models';
import { LedgerNavComponent } from '../../shared/ledger-nav.component';

@Component({
  standalone: true,
  imports: [CommonModule, FormsModule, LedgerNavComponent],
  template: `
    <main class="page">
      <app-ledger-nav [ledgerId]="ledgerId" />
      <header class="page-header">
        <div>
          <p class="eyebrow">Journal Entry Detail</p>
          <h1>{{ entryId ? 'Edit Journal Entry' : 'New Journal Entry' }}</h1>
        </div>
        <div class="button-row">
          <button type="button" (click)="save()" [disabled]="posted">Save Draft</button>
          <button type="button" class="accent" (click)="post()" [disabled]="posted || !isBalanced">Post</button>
        </div>
      </header>

      <section class="panel">
        <div class="entry-form">
          <label>JournalNo<input [(ngModel)]="journalNo" name="journalNo" /></label>
          <label>EntryDate<input type="date" [(ngModel)]="entryDate" name="entryDate" /></label>
          <label>Description<input [(ngModel)]="description" name="description" /></label>
          <label>Status<input [value]="status" disabled /></label>
        </div>
      </section>

      <section class="panel">
        <div class="balance-strip" [class.ok]="isBalanced">
          Debit {{ totalDebit | number:'1.2-2' }} / Credit {{ totalCredit | number:'1.2-2' }}
          <strong>{{ isBalanced ? 'Balanced' : 'Not balanced' }}</strong>
        </div>
        <table>
          <thead>
            <tr><th>Account</th><th class="number">Debit</th><th class="number">Credit</th><th>Description</th><th></th></tr>
          </thead>
          <tbody>
            <tr *ngFor="let line of lines; let i = index">
              <td>
                <select [(ngModel)]="line.accountId" [name]="'account' + i" [disabled]="posted">
                  <option [ngValue]="0">Select account</option>
                  <option *ngFor="let account of activeAccounts" [ngValue]="account.id">{{ account.code }} {{ account.name }}</option>
                </select>
              </td>
              <td><input class="number" type="number" min="0" [(ngModel)]="line.debit" [name]="'debit' + i" [disabled]="posted" /></td>
              <td><input class="number" type="number" min="0" [(ngModel)]="line.credit" [name]="'credit' + i" [disabled]="posted" /></td>
              <td><input [(ngModel)]="line.description" [name]="'description' + i" [disabled]="posted" /></td>
              <td class="actions"><button type="button" class="secondary" (click)="removeLine(i)" [disabled]="posted || lines.length <= 2">Remove</button></td>
            </tr>
          </tbody>
        </table>
        <button type="button" class="secondary add-line" (click)="addLine()" [disabled]="posted">Add Line</button>
        <p class="error" *ngIf="error">{{ error }}</p>
      </section>
    </main>
  `
})
export class JournalEntryDetailPage implements OnInit {
  ledgerId = 0;
  entryId?: number;
  journalNo = '';
  entryDate = new Date().toISOString().slice(0, 10);
  description = '';
  status = 'Draft';
  accounts: Account[] = [];
  lines: JournalLine[] = [this.emptyLine(), this.emptyLine()];
  error = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly accountApi: AccountApiService,
    private readonly journalApi: JournalApiService,
    private readonly cdr: ChangeDetectorRef
  ) {}

  get activeAccounts() {
    return this.accounts.filter((account) => account.isActive);
  }

  get totalDebit() {
    return this.lines.reduce((sum, line) => sum + Number(line.debit || 0), 0);
  }

  get totalCredit() {
    return this.lines.reduce((sum, line) => sum + Number(line.credit || 0), 0);
  }

  get isBalanced() {
    return this.totalDebit > 0 && this.totalDebit === this.totalCredit;
  }

  get posted() {
    return this.status === 'Posted';
  }

  ngOnInit() {
    this.ledgerId = Number(this.route.snapshot.paramMap.get('ledgerId'));
    const idParam = this.route.snapshot.paramMap.get('journalEntryId');
    this.entryId = idParam && idParam !== 'new' ? Number(idParam) : undefined;
    this.accountApi.getAccounts(this.ledgerId).subscribe((accounts) => {
      this.accounts = accounts;
      this.cdr.detectChanges();
    });
    if (this.entryId) {
      this.journalApi.getJournalEntry(this.entryId).subscribe((entry) => {
        this.journalNo = entry.journalNo;
        this.entryDate = entry.entryDate;
        this.description = entry.description;
        this.status = entry.status;
        this.lines = entry.lines.map((line) => ({
          accountId: line.accountId,
          debit: line.debit,
          credit: line.credit,
          description: line.description
        }));
        this.cdr.detectChanges();
      });
    } else {
      this.journalNo = `JV-${Date.now().toString().slice(-4)}`;
    }
  }

  addLine() {
    this.lines.push(this.emptyLine());
  }

  removeLine(index: number) {
    this.lines.splice(index, 1);
  }

  save() {
    this.error = '';
    const request = this.request();
    const onError = (response: HttpErrorResponse) => (this.error = response.error?.error ?? 'Unable to save journal entry.');
    if (this.entryId) {
      this.journalApi.updateJournalEntry(this.entryId, request).subscribe({
        next: () => this.router.navigate(['/ledgers', this.ledgerId, 'journal-entries', this.entryId]),
        error: onError
      });
      return;
    }

    this.journalApi.createJournalEntry(this.ledgerId, request).subscribe({
      next: (entry) => this.router.navigate(['/ledgers', this.ledgerId, 'journal-entries', entry.id]),
      error: onError
    });
  }

  post() {
    if (!this.entryId) {
      this.save();
      return;
    }

    this.journalApi.postJournalEntry(this.entryId).subscribe({
      next: (entry) => {
        this.status = entry.status;
        this.cdr.detectChanges();
      },
      error: (response: HttpErrorResponse) => {
        this.error = response.error?.error ?? 'Unable to post journal entry.';
        this.cdr.detectChanges();
      }
    });
  }

  private request(): SaveJournalEntryRequest {
    return {
      journalNo: this.journalNo,
      entryDate: this.entryDate,
      description: this.description,
      lines: this.lines.map((line) => ({
        accountId: Number(line.accountId),
        debit: Number(line.debit || 0),
        credit: Number(line.credit || 0),
        description: line.description ?? ''
      }))
    };
  }

  private emptyLine(): JournalLine {
    return { accountId: 0, debit: 0, credit: 0, description: '' };
  }
}
