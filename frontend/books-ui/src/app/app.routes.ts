import { Routes } from '@angular/router';
import { LedgersPage } from './features/ledgers/ledgers.page';
import { LedgerDashboardPage } from './features/dashboard/ledger-dashboard.page';
import { AccountsPage } from './features/accounts/accounts.page';
import { JournalEntriesPage } from './features/journal-entries/journal-entries.page';
import { JournalEntryDetailPage } from './features/journal-entry-detail/journal-entry-detail.page';
import { TrialBalancePage } from './features/trial-balance/trial-balance.page';
import { GeneralLedgerPage } from './features/general-ledger/general-ledger.page';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'ledgers' },
  { path: 'ledgers', component: LedgersPage },
  { path: 'ledgers/:ledgerId/dashboard', component: LedgerDashboardPage },
  { path: 'ledgers/:ledgerId/accounts', component: AccountsPage },
  { path: 'ledgers/:ledgerId/journal-entries', component: JournalEntriesPage },
  { path: 'ledgers/:ledgerId/journal-entries/:journalEntryId', component: JournalEntryDetailPage },
  { path: 'ledgers/:ledgerId/trial-balance', component: TrialBalancePage },
  { path: 'ledgers/:ledgerId/general-ledger', component: GeneralLedgerPage },
  { path: '**', redirectTo: 'ledgers' }
];
