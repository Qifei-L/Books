import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { AccountApiService } from '../../core/api/account-api.service';
import { Account, GeneralLedgerRow } from '../../core/api/models';
import { ReportApiService } from '../../core/api/report-api.service';
import { LedgerNavComponent } from '../../shared/ledger-nav.component';

@Component({
  standalone: true,
  imports: [CommonModule, FormsModule, LedgerNavComponent],
  template: `
    <main class="page">
      <app-ledger-nav [ledgerId]="ledgerId" />
      <header class="page-header">
        <div>
          <p class="eyebrow">Reports</p>
          <h1>General Ledger</h1>
        </div>
      </header>

      <section class="panel">
        <div class="inline-form">
          <select [(ngModel)]="selectedAccountId" name="accountId" (change)="loadLedger()">
            <option *ngFor="let account of accounts" [ngValue]="account.id">{{ account.code }} {{ account.name }}</option>
          </select>
        </div>
      </section>

      <section class="panel">
        <table>
          <thead>
            <tr><th>Date</th><th>JournalNo</th><th>Description</th><th class="number">Debit</th><th class="number">Credit</th><th class="number">Balance</th></tr>
          </thead>
          <tbody>
            <tr *ngFor="let row of rows">
              <td>{{ row.entryDate }}</td>
              <td>{{ row.journalNo }}</td>
              <td>{{ row.description }}</td>
              <td class="number">{{ row.debit | number:'1.2-2' }}</td>
              <td class="number">{{ row.credit | number:'1.2-2' }}</td>
              <td class="number">{{ row.balance | number:'1.2-2' }}</td>
            </tr>
          </tbody>
        </table>
      </section>
    </main>
  `
})
export class GeneralLedgerPage implements OnInit {
  ledgerId = 0;
  accounts: Account[] = [];
  rows: GeneralLedgerRow[] = [];
  selectedAccountId = 0;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly accountApi: AccountApiService,
    private readonly reportApi: ReportApiService,
    private readonly cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    this.ledgerId = Number(this.route.snapshot.paramMap.get('ledgerId'));
    this.accountApi.getAccounts(this.ledgerId).subscribe((accounts) => {
      this.accounts = accounts;
      this.selectedAccountId = accounts.find((account) => account.code === '1000')?.id ?? accounts[0]?.id ?? 0;
      this.loadLedger();
      this.cdr.detectChanges();
    });
  }

  loadLedger() {
    if (!this.selectedAccountId) {
      this.rows = [];
      return;
    }

    this.reportApi
      .getGeneralLedger(this.ledgerId, this.selectedAccountId)
      .subscribe((rows) => {
        this.rows = rows;
        this.cdr.detectChanges();
      });
  }
}
