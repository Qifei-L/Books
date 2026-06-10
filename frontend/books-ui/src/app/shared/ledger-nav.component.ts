import { Component, Input } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-ledger-nav',
  standalone: true,
  imports: [RouterLink],
  template: `
    <nav class="top-nav">
      <a routerLink="/ledgers">Ledgers</a>
      <a [routerLink]="['/ledgers', ledgerId, 'dashboard']">Dashboard</a>
      <a [routerLink]="['/ledgers', ledgerId, 'accounts']">Chart of Accounts</a>
      <a [routerLink]="['/ledgers', ledgerId, 'journal-entries']">Journal Entries</a>
      <a [routerLink]="['/ledgers', ledgerId, 'trial-balance']">Trial Balance</a>
      <a [routerLink]="['/ledgers', ledgerId, 'general-ledger']">General Ledger</a>
    </nav>
  `
})
export class LedgerNavComponent {
  @Input({ required: true }) ledgerId!: number;
}
