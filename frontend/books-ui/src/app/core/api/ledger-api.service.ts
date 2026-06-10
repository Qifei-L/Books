import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { Ledger } from './models';

@Injectable({ providedIn: 'root' })
export class LedgerApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  getLedgers() {
    return this.http.get<Ledger[]>(`${this.baseUrl}/ledgers`);
  }

  getLedger(id: number) {
    return this.http.get<Ledger>(`${this.baseUrl}/ledgers/${id}`);
  }

  createLedger(name: string) {
    return this.http.post<Ledger>(`${this.baseUrl}/ledgers`, { name });
  }
}
