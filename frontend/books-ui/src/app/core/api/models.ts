export type AccountType = 'Asset' | 'Liability' | 'Equity' | 'Income' | 'Revenue' | 'Expense';
export type JournalStatus = 'Draft' | 'Posted' | 'Reversed' | 'Voided';

export interface Ledger {
  id: number;
  name: string;
  isActive: boolean;
}

export interface Account {
  id: number;
  ledgerId: number;
  code: string;
  name: string;
  type: AccountType;
  description: string;
  isActive: boolean;
}

export interface JournalLine {
  id?: number;
  journalEntryId?: number;
  accountId: number;
  account?: Account;
  debit: number;
  credit: number;
  description: string;
}

export interface JournalEntry {
  id: number;
  ledgerId: number;
  journalNo: string;
  entryDate: string;
  description: string;
  status: JournalStatus;
  lines: JournalLine[];
}

export interface JournalEntrySummary {
  id: number;
  ledgerId: number;
  journalNo: string;
  entryDate: string;
  description: string;
  status: JournalStatus;
  totalDebit: number;
  totalCredit: number;
}

export interface JournalEntryFilter {
  search?: string;
  from?: string;
  to?: string;
  status?: JournalStatus | 'All' | '';
}

export interface TrialBalanceRow {
  accountCode: string;
  accountName: string;
  debit: number;
  credit: number;
}

export interface GeneralLedgerRow {
  entryDate: string;
  journalNo: string;
  description: string;
  debit: number;
  credit: number;
  balance: number;
}
