import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { JournalApiService } from '../../core/api/journal-api.service';
import { JournalEntrySummary } from '../../core/api/models';
import { LedgerNavComponent } from '../../shared/ledger-nav.component';

@Component({
  standalone: true,
  imports: [CommonModule, RouterLink, LedgerNavComponent],
  template: `
    <main class="page">
      <app-ledger-nav [ledgerId]="ledgerId" />
      <header class="page-header">
        <div>
          <p class="eyebrow">Journal Entries</p>
          <h1>Entries</h1>
        </div>
        <button type="button" (click)="newEntry()">New Journal Entry</button>
      </header>

      <section class="panel">
        <table>
          <thead>
            <tr>
              <th>JournalNo</th><th>EntryDate</th><th>Description</th><th>Status</th>
              <th class="number">TotalDebit</th><th class="number">TotalCredit</th><th></th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let entry of entries">
              <td><a [routerLink]="['/ledgers', ledgerId, 'journal-entries', entry.id]">{{ entry.journalNo }}</a></td>
              <td>{{ entry.entryDate }}</td>
              <td>{{ entry.description }}</td>
              <td><span class="status">{{ entry.status }}</span></td>
              <td class="number">{{ entry.totalDebit | number:'1.2-2' }}</td>
              <td class="number">{{ entry.totalCredit | number:'1.2-2' }}</td>
              <td class="actions">
                <button type="button" class="secondary" (click)="post(entry)" [disabled]="entry.status === 'Posted'">Post</button>
              </td>
            </tr>
          </tbody>
        </table>
      </section>
    </main>
  `
})
export class JournalEntriesPage implements OnInit {
  ledgerId = 0;
  entries: JournalEntrySummary[] = [];

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
    this.journalApi.getJournalEntries(this.ledgerId).subscribe((entries) => {
      this.entries = entries;
      this.cdr.detectChanges();
    });
  }

  newEntry() {
    this.router.navigate(['/ledgers', this.ledgerId, 'journal-entries', 'new']);
  }

  post(entry: JournalEntrySummary) {
    this.journalApi.postJournalEntry(entry.id).subscribe(() => this.load());
  }
}
