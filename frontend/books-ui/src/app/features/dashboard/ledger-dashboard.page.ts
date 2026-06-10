import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { LedgerApiService } from '../../core/api/ledger-api.service';
import { Ledger } from '../../core/api/models';
import { LedgerNavComponent } from '../../shared/ledger-nav.component';

@Component({
  standalone: true,
  imports: [CommonModule, RouterLink, LedgerNavComponent],
  template: `
    <main class="page" *ngIf="Ledger">
      <app-ledger-nav [ledgerId]="Ledger.id" />
      <header class="page-header">
        <div>
          <p class="eyebrow">Ledger Dashboard</p>
          <h1>{{ Ledger.name }}</h1>
        </div>
      </header>

      <section class="grid-list">
        <a class="card action-card" [routerLink]="['/ledgers', Ledger.id, 'accounts']">Chart of Accounts</a>
        <a class="card action-card" [routerLink]="['/ledgers', Ledger.id, 'journal-entries']">Journal Entries</a>
        <a class="card action-card" [routerLink]="['/ledgers', Ledger.id, 'trial-balance']">Trial Balance</a>
        <a class="card action-card" [routerLink]="['/ledgers', Ledger.id, 'general-ledger']">General Ledger</a>
      </section>
    </main>
  `
})
export class LedgerDashboardPage implements OnInit {
  Ledger?: Ledger;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly LedgerApi: LedgerApiService,
    private readonly cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    const ledgerId = Number(this.route.snapshot.paramMap.get('ledgerId'));
    this.LedgerApi.getLedger(ledgerId).subscribe((Ledger) => {
      this.Ledger = Ledger;
      this.cdr.detectChanges();
    });
  }
}
