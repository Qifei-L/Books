import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TrialBalanceRow } from '../../core/api/models';
import { ReportApiService } from '../../core/api/report-api.service';
import { LedgerNavComponent } from '../../shared/ledger-nav.component';

@Component({
  standalone: true,
  imports: [CommonModule, LedgerNavComponent],
  template: `
    <main class="page">
      <app-ledger-nav [ledgerId]="ledgerId" />
      <header class="page-header">
        <div>
          <p class="eyebrow">Reports</p>
          <h1>Trial Balance</h1>
        </div>
      </header>

      <section class="panel">
        <table>
          <thead><tr><th>AccountCode</th><th>AccountName</th><th class="number">Debit</th><th class="number">Credit</th></tr></thead>
          <tbody>
            <tr *ngFor="let row of rows">
              <td>{{ row.accountCode }}</td>
              <td>{{ row.accountName }}</td>
              <td class="number">{{ row.debit | number:'1.2-2' }}</td>
              <td class="number">{{ row.credit | number:'1.2-2' }}</td>
            </tr>
          </tbody>
          <tfoot>
            <tr><th colspan="2">Total</th><th class="number">{{ totalDebit | number:'1.2-2' }}</th><th class="number">{{ totalCredit | number:'1.2-2' }}</th></tr>
          </tfoot>
        </table>
      </section>
    </main>
  `
})
export class TrialBalancePage implements OnInit {
  ledgerId = 0;
  rows: TrialBalanceRow[] = [];

  constructor(
    private readonly route: ActivatedRoute,
    private readonly reportApi: ReportApiService,
    private readonly cdr: ChangeDetectorRef
  ) {}

  get totalDebit() {
    return this.rows.reduce((sum, row) => sum + row.debit, 0);
  }

  get totalCredit() {
    return this.rows.reduce((sum, row) => sum + row.credit, 0);
  }

  ngOnInit() {
    this.ledgerId = Number(this.route.snapshot.paramMap.get('ledgerId'));
    this.reportApi.getTrialBalance(this.ledgerId).subscribe((rows) => {
      this.rows = rows;
      this.cdr.detectChanges();
    });
  }
}
