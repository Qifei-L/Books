import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { Account, AccountType } from './models';

export interface SaveAccountRequest {
  code: string;
  name: string;
  type: AccountType;
  description?: string;
  isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class AccountApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  getAccounts(ledgerId: number) {
    return this.http.get<Account[]>(`${this.baseUrl}/ledgers/${ledgerId}/accounts`);
  }

  createAccount(ledgerId: number, request: SaveAccountRequest) {
    return this.http.post<Account>(`${this.baseUrl}/ledgers/${ledgerId}/accounts`, request);
  }

  updateAccount(id: number, request: SaveAccountRequest) {
    return this.http.put<void>(`${this.baseUrl}/accounts/${id}`, request);
  }

  deactivateAccount(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/accounts/${id}`);
  }
}
