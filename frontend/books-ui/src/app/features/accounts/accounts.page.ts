import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { AccountApiService, SaveAccountRequest } from '../../core/api/account-api.service';
import { Account, AccountType } from '../../core/api/models';
import { LedgerNavComponent } from '../../shared/ledger-nav.component';

@Component({
  standalone: true,
  imports: [CommonModule, FormsModule, LedgerNavComponent],
  template: `
    <main class="page">
      <app-ledger-nav [ledgerId]="ledgerId" />
      <header class="page-header">
        <div>
          <p class="eyebrow">Chart of Accounts</p>
          <h1>Accounts</h1>
        </div>
      </header>

      <section class="panel">
        <form class="account-form" (ngSubmit)="save()">
          <input name="code" [(ngModel)]="form.code" placeholder="Code" required />
          <input name="name" [(ngModel)]="form.name" placeholder="Name" required />
          <select name="type" [(ngModel)]="form.type">
            <option *ngFor="let type of accountTypes" [ngValue]="type">{{ type }}</option>
          </select>
          <label class="checkbox"><input type="checkbox" name="active" [(ngModel)]="form.isActive" /> Active</label>
          <button type="submit">{{ editingId ? 'Save Account' : 'Add Account' }}</button>
          <button type="button" class="secondary" (click)="reset()">Clear</button>
        </form>
      </section>

      <section class="panel">
        <table>
          <thead>
            <tr><th>Code</th><th>Name</th><th>Type</th><th>Status</th><th></th></tr>
          </thead>
          <tbody>
            <tr *ngFor="let account of accounts">
              <td>{{ account.code }}</td>
              <td>{{ account.name }}</td>
              <td>{{ account.type }}</td>
              <td><span class="status" [class.muted]="!account.isActive">{{ account.isActive ? 'Active' : 'Inactive' }}</span></td>
              <td class="actions">
                <button type="button" class="secondary" (click)="edit(account)">Edit</button>
                <button type="button" class="secondary" (click)="deactivate(account)" [disabled]="!account.isActive">Disable</button>
              </td>
            </tr>
          </tbody>
        </table>
      </section>
    </main>
  `
})
export class AccountsPage implements OnInit {
  ledgerId = 0;
  accounts: Account[] = [];
  accountTypes: AccountType[] = ['Asset', 'Liability', 'Equity', 'Revenue', 'Expense'];
  editingId?: number;
  form: SaveAccountRequest = this.emptyForm();

  constructor(
    private readonly route: ActivatedRoute,
    private readonly accountsApi: AccountApiService,
    private readonly cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    this.ledgerId = Number(this.route.snapshot.paramMap.get('ledgerId'));
    this.form = this.emptyForm();
    this.load();
  }

  load() {
    this.accountsApi.getAccounts(this.ledgerId).subscribe((accounts) => {
      this.accounts = accounts;
      this.cdr.detectChanges();
    });
  }

  save() {
    const request = { ...this.form, code: this.form.code.trim(), name: this.form.name.trim() };
    if (!request.code || !request.name) {
      return;
    }

    const afterSave = () => {
      this.reset();
      this.load();
      this.cdr.detectChanges();
    };

    if (this.editingId) {
      this.accountsApi.updateAccount(this.editingId, request).subscribe(afterSave);
      return;
    }

    this.accountsApi.createAccount(this.ledgerId, request).subscribe(afterSave);
  }

  edit(account: Account) {
    this.editingId = account.id;
    this.form = {
      code: account.code,
      name: account.name,
      type: account.type,
      description: account.description,
      isActive: account.isActive
    };
  }

  deactivate(account: Account) {
    this.accountsApi.deactivateAccount(account.id).subscribe(() => this.load());
  }

  reset() {
    this.editingId = undefined;
    this.form = this.emptyForm();
  }

  private emptyForm(): SaveAccountRequest {
    return { code: '', name: '', type: 'Asset', description: '', isActive: true };
  }
}
