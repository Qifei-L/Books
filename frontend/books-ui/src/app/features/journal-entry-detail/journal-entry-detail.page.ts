import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AccountApiService } from '../../core/api/account-api.service';
import { JournalApiService, SaveJournalEntryRequest } from '../../core/api/journal-api.service';
import { LedgerApiService } from '../../core/api/ledger-api.service';
import { Account, JournalEntry, Ledger } from '../../core/api/models';
import { LedgerNavComponent } from '../../shared/ledger-nav.component';
import { JournalEntryEditorComponent } from '../journal-entries/components/journal-entry-editor/journal-entry-editor.component';

@Component({
  standalone: true,
  imports: [CommonModule, LedgerNavComponent, JournalEntryEditorComponent],
  template: `
    <main class="page">
      <app-ledger-nav [ledgerId]="ledgerId" />
      <app-journal-entry-editor
        [ledgerId]="ledgerId"
        [ledgerName]="ledger?.name ?? ''"
        [journalEntry]="journalEntry"
        [accounts]="accounts"
        [loading]="loading"
        (saveDraft)="saveDraft($event)"
        (post)="postEntry($event)"
        (cancel)="back()"
      />
      <p class="error" *ngIf="error">{{ error }}</p>
    </main>
  `
})
export class JournalEntryDetailPage implements OnInit {
  ledgerId = 0;
  entryId?: number;
  ledger?: Ledger;
  journalEntry?: JournalEntry;
  accounts: Account[] = [];
  loading = false;
  error = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly accountApi: AccountApiService,
    private readonly journalApi: JournalApiService,
    private readonly ledgerApi: LedgerApiService,
    private readonly cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    this.ledgerId = Number(this.route.snapshot.paramMap.get('ledgerId'));
    const idParam = this.route.snapshot.paramMap.get('journalEntryId');
    this.entryId = idParam && idParam !== 'new' ? Number(idParam) : undefined;
    this.load();
  }

  saveDraft(request: SaveJournalEntryRequest) {
    this.error = '';
    this.loading = true;
    const onError = (response: HttpErrorResponse) => {
      this.error = response.error?.error ?? 'Unable to save journal entry.';
      this.loading = false;
      this.cdr.detectChanges();
    };

    if (this.entryId) {
      this.journalApi.updateJournalEntry(this.entryId, request).subscribe({
        next: () => {
          this.refreshEntry(this.entryId!);
        },
        error: onError
      });
      return;
    }

    this.journalApi.createJournalEntry(this.ledgerId, request).subscribe({
      next: (entry) => {
        this.loading = false;
        this.router.navigate(['/ledgers', this.ledgerId, 'journal-entries', entry.id]);
      },
      error: onError
    });
  }

  postEntry(request: SaveJournalEntryRequest) {
    this.error = '';
    this.loading = true;
    const onError = (response: HttpErrorResponse) => {
      this.error = response.error?.error ?? 'Unable to post journal entry.';
      this.loading = false;
      this.cdr.detectChanges();
    };

    if (this.entryId) {
      this.journalApi.updateJournalEntry(this.entryId, request).subscribe({
        next: () => this.postExistingEntry(this.entryId!),
        error: onError
      });
      return;
    }

    this.journalApi.createJournalEntry(this.ledgerId, request).subscribe({
      next: (entry) => this.postExistingEntry(entry.id),
      error: onError
    });
  }

  back() {
    this.router.navigate(['/ledgers', this.ledgerId, 'journal-entries']);
  }

  private load() {
    this.loading = true;
    this.ledgerApi.getLedger(this.ledgerId).subscribe((ledger) => {
      this.ledger = ledger;
      this.cdr.detectChanges();
    });
    this.accountApi.getAccounts(this.ledgerId).subscribe((accounts) => {
      this.accounts = accounts;
      this.cdr.detectChanges();
    });
    if (this.entryId) {
      this.journalApi.getJournalEntry(this.entryId).subscribe((entry) => {
        this.journalEntry = entry;
        this.loading = false;
        this.cdr.detectChanges();
      });
      return;
    }

    this.loading = false;
  }

  private postExistingEntry(id: number) {
    this.journalApi.postJournalEntry(id).subscribe({
      next: (entry) => {
        this.entryId = entry.id;
        this.journalEntry = entry;
        this.loading = false;
        this.router.navigate(['/ledgers', this.ledgerId, 'journal-entries', entry.id]);
      },
      error: (response: HttpErrorResponse) => {
        this.error = response.error?.error ?? 'Unable to post journal entry.';
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  private refreshEntry(id: number) {
    this.journalApi.getJournalEntry(id).subscribe({
      next: (entry) => {
        this.journalEntry = entry;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (response: HttpErrorResponse) => {
        this.error = response.error?.error ?? 'Unable to load journal entry.';
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }
}
