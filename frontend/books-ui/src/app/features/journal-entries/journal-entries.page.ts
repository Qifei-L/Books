import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { JournalApiService } from '../../core/api/journal-api.service';
import { JournalEntryFilter, JournalEntrySummary } from '../../core/api/models';
import { LedgerNavComponent } from '../../shared/ledger-nav.component';
import { JournalEntryListComponent } from './components/journal-entry-list/journal-entry-list.component';

@Component({
  standalone: true,
  imports: [CommonModule, LedgerNavComponent, JournalEntryListComponent],
  template: `
    <main class="page">
      <app-ledger-nav [ledgerId]="ledgerId" />
      <header class="page-header">
        <div>
          <p class="eyebrow">Journal Entries</p>
          <h1>Entries</h1>
        </div>
      </header>

      <app-journal-entry-list
        [entries]="entries"
        [loading]="loading"
        [error]="error"
        (create)="newEntry()"
        (view)="viewEntry($event)"
        (edit)="editEntry($event)"
        (post)="postEntry($event)"
        (filterChange)="applyFilter($event)"
        (retry)="load()"
      />
    </main>
  `
})
export class JournalEntriesPage implements OnInit {
  ledgerId = 0;
  entries: JournalEntrySummary[] = [];
  loading = false;
  error: string | null = null;
  filter: JournalEntryFilter = {};

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly journalApi: JournalApiService,
    private readonly cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    this.ledgerId = Number(this.route.snapshot.paramMap.get('ledgerId'));
    this.load();
  }

  load() {
    this.loading = true;
    this.error = null;
    this.journalApi.getJournalEntries(this.ledgerId, this.filter).subscribe({
      next: (entries) => {
        this.entries = entries;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (response: HttpErrorResponse) => {
        this.error = response.error?.error ?? 'Unable to load journal entries.';
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  applyFilter(filter: JournalEntryFilter) {
    this.filter = filter;
    this.load();
  }

  newEntry() {
    this.router.navigate(['/ledgers', this.ledgerId, 'journal-entries', 'new']);
  }

  viewEntry(entry: JournalEntrySummary) {
    this.router.navigate(['/ledgers', this.ledgerId, 'journal-entries', entry.id]);
  }

  editEntry(entry: JournalEntrySummary) {
    this.viewEntry(entry);
  }

  postEntry(entry: JournalEntrySummary) {
    this.journalApi.postJournalEntry(entry.id).subscribe({
      next: () => this.load(),
      error: (response: HttpErrorResponse) => {
        this.error = response.error?.error ?? 'Unable to post journal entry.';
        this.cdr.detectChanges();
      }
    });
  }
}
