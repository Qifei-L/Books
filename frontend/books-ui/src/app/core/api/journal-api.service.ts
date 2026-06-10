import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { JournalEntry, JournalEntrySummary, JournalLine } from './models';

export interface SaveJournalEntryRequest {
  journalNo: string;
  entryDate: string;
  description: string;
  lines: Pick<JournalLine, 'accountId' | 'debit' | 'credit' | 'description'>[];
}

@Injectable({ providedIn: 'root' })
export class JournalApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  getJournalEntries(ledgerId: number) {
    return this.http.get<JournalEntrySummary[]>(`${this.baseUrl}/ledgers/${ledgerId}/journal-entries`);
  }

  getJournalEntry(id: number) {
    return this.http.get<JournalEntry>(`${this.baseUrl}/journal-entries/${id}`);
  }

  createJournalEntry(ledgerId: number, request: SaveJournalEntryRequest) {
    return this.http.post<JournalEntry>(`${this.baseUrl}/ledgers/${ledgerId}/journal-entries`, request);
  }

  updateJournalEntry(id: number, request: SaveJournalEntryRequest) {
    return this.http.put<void>(`${this.baseUrl}/journal-entries/${id}`, request);
  }

  postJournalEntry(id: number) {
    return this.http.post<JournalEntry>(`${this.baseUrl}/journal-entries/${id}/post`, {});
  }
}
