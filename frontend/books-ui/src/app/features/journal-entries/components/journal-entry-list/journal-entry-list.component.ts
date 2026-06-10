import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { JournalEntryFilter, JournalEntrySummary, JournalStatus } from '../../../../core/api/models';

@Component({
  selector: 'app-journal-entry-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './journal-entry-list.component.html',
  styleUrl: './journal-entry-list.component.scss'
})
export class JournalEntryListComponent {
  @Input() entries: JournalEntrySummary[] = [];
  @Input() loading = false;
  @Input() error: string | null = null;

  @Output() create = new EventEmitter<void>();
  @Output() view = new EventEmitter<JournalEntrySummary>();
  @Output() edit = new EventEmitter<JournalEntrySummary>();
  @Output() post = new EventEmitter<JournalEntrySummary>();
  @Output() filterChange = new EventEmitter<JournalEntryFilter>();
  @Output() retry = new EventEmitter<void>();

  filter: JournalEntryFilter = {
    search: '',
    from: '',
    to: '',
    status: 'All'
  };

  readonly statuses: Array<JournalStatus | 'All'> = ['All', 'Draft', 'Posted', 'Voided'];

  get filteredEntries() {
    const search = this.filter.search?.trim().toLowerCase() ?? '';
    const status = this.filter.status;
    return this.entries.filter((entry) => {
      const matchesSearch = !search
        || entry.journalNo.toLowerCase().includes(search)
        || (entry.description ?? '').toLowerCase().includes(search);
      const matchesStatus = !status || status === 'All' || entry.status === status;
      return matchesSearch && matchesStatus;
    });
  }

  get hasEntries() {
    return this.filteredEntries.length > 0;
  }

  applyFilter() {
    this.filterChange.emit({ ...this.filter });
  }

  clearFilter() {
    this.filter = {
      search: '',
      from: '',
      to: '',
      status: 'All'
    };
    this.applyFilter();
  }

  formatDate(date: string | Date) {
    if (date instanceof Date) {
      return date.toISOString().slice(0, 10);
    }
    return date ? date.slice(0, 10) : '';
  }

  difference(entry: JournalEntrySummary) {
    return entry.totalDebit - entry.totalCredit;
  }

  isBalanced(entry: JournalEntrySummary) {
    return this.difference(entry) === 0;
  }

  canPost(entry: JournalEntrySummary) {
    return entry.status === 'Draft' && this.isBalanced(entry);
  }

  actionLabel(entry: JournalEntrySummary) {
    return entry.status === 'Draft' ? 'Edit' : 'View';
  }

  statusClass(status: JournalStatus) {
    return {
      'status-draft': status === 'Draft',
      'status-posted': status === 'Posted',
      'status-voided': status === 'Voided'
    };
  }

  trackEntry(_: number, entry: JournalEntrySummary) {
    return entry.id;
  }
}
