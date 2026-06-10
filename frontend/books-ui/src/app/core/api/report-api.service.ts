import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { GeneralLedgerRow, TrialBalanceRow } from './models';

@Injectable({ providedIn: 'root' })
export class ReportApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  getTrialBalance(ledgerId: number) {
    return this.http.get<TrialBalanceRow[]>(`${this.baseUrl}/ledgers/${ledgerId}/reports/trial-balance`);
  }

  getGeneralLedger(ledgerId: number, accountId: number) {
    return this.http.get<GeneralLedgerRow[]>(`${this.baseUrl}/ledgers/${ledgerId}/reports/general-ledger`, {
      params: { accountId }
    });
  }
}
