import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { LedgerApiService } from '../../core/api/ledger-api.service';
import { Ledger } from '../../core/api/models';

@Component({
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <main class="page">
      <header class="page-header">
        <div>
          <p class="eyebrow">Books</p>
          <h1>Ledgers</h1>
        </div>
      </header>

      <section class="panel">
        <form class="inline-form" (ngSubmit)="createLedger()">
          <input name="name" [(ngModel)]="newLedgerName" placeholder="Ledger name" required />
          <button type="submit">Add Ledger</button>
        </form>
      </section>

      <section class="grid-list">
        <article class="card" *ngFor="let Ledger of Ledgers">
          <h2>{{ Ledger.name }}</h2>
          <a class="button-link" [routerLink]="['/ledgers', Ledger.id, 'dashboard']">Open Dashboard</a>
        </article>
      </section>
      <p class="error" *ngIf="error">{{ error }}</p>
    </main>
  `
})
export class LedgersPage implements OnInit {
  Ledgers: Ledger[] = [];
  newLedgerName = '';
  error = '';

  constructor(
    private readonly LedgersApi: LedgerApiService,
    private readonly cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    this.load();
  }

  load() {
    this.error = '';
    this.LedgersApi.getLedgers().subscribe({
      next: (Ledgers) => {
        this.Ledgers = Array.isArray(Ledgers) ? Ledgers : [];
        this.cdr.detectChanges();
      },
      error: () => {
        this.error = 'Unable to load Ledgers from the API.';
        this.cdr.detectChanges();
      }
    });
  }

  createLedger() {
    const name = this.newLedgerName.trim();
    if (!name) {
      return;
    }

    this.LedgersApi.createLedger(name).subscribe(() => {
      this.newLedgerName = '';
      this.load();
      this.cdr.detectChanges();
    });
  }
}
