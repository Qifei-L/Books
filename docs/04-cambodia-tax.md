# Cambodia tax

Research summary from 2026-10-23. **This is not tax advice.** Confirm each item with a Cambodian tax adviser before M8.

## Requirements found

| Topic | Requirement | Source |
| --- | --- | --- |
| VAT rate | Standard 10%. Temporary 4% on fuel from March 2026. | [Kreston Q3 2026](https://www.krestoncambodia.com/2026/09/17/cambodia-tax-briefing-q3-2026/) |
| KHR on invoices | Self-assessment taxpayers must show the invoice total, including VAT, in KHR. Lines may stay in USD. | [DFDL, Notification 4908 (2019)](https://www.dfdl.com/insights/legal-and-tax-updates/cambodia-tax-alert-use-of-khmer-riel-in-tax-invoices/) |
| Exchange rates | Invoices use the NBC daily rate (or a market rate not lower than it). Monthly taxes use the NBC rate on the last day of the month. Salary tax uses the rate on the 15th. Annual tax uses 31 December. | [DFDL, Instruction 26118 (2022)](https://www.dfdl.com/insights/legal-and-tax-updates/cambodia-official-exchange-rate-update/) |
| Monthly return | Filed and paid by the 20th of the following month. If the 20th is a non-working day, the next working day. | [PwC](https://taxsummaries.pwc.com/cambodia/corporate/tax-administration) |
| Prepayment of tax on income | 1% of monthly turnover, excluding VAT. Its suspension is reported as extended to the end of 2028. | [PwC](https://taxsummaries.pwc.com/cambodia/corporate/tax-administration), [Kreston](https://www.krestoncambodia.com/2026/09/17/cambodia-tax-briefing-q3-2026/) |
| Withholding tax (resident payee) | Services, interest, royalties 15%. Rent 10%. Services backed by a valid VAT invoice from a registered taxpayer are exempt. | [PwC](https://taxsummaries.pwc.com/cambodia/corporate/withholding-taxes) |
| Withholding tax (non-resident) | 14%. | [PwC](https://taxsummaries.pwc.com/cambodia/corporate/withholding-taxes) |
| WHT timing | Declare WHT when the expense is accrued, not when it is paid. Input VAT is claimable only once a valid tax invoice is received. | [DFDL, Instruction 37705 (2 Dec 2025)](https://www.dfdl.com/insights/legal-and-tax-updates/cambodia-procedures-for-recording-and-declaring-taxes-on-transactions-that-occur-and-are-recorded-in-accounting-prior-to-payment/) |
| E-invoicing | CamInvoice is mandatory for B2G and selected taxpayers. General B2B is voluntary, with a mandate expected around 2026–2027. | [Kreston](https://www.krestoncambodia.com/2026/09/17/cambodia-tax-briefing-q3-2026/) |
| Tax year | Calendar year by default; a different year-end needs approval. | [PwC](https://taxsummaries.pwc.com/cambodia/corporate/tax-administration) |

## What this changes in the design

1. **Base currency is likely USD, with KHR used for tax.** Each sales and purchase invoice stores `khr_rate` and `khr_rate_date` (the NBC daily rate) and a KHR total. This rate is tax evidence, separate from the journal's transaction rate.
2. **Tax reporting needs a rate table.** An `exchange_rates(date, from, to, rate, source)` table holds NBC rates. Monthly figures use the month-end rate, and the tax report shows both USD and KHR.
3. **Withholding tax belongs to Purchases.** A purchase invoice line can carry a WHT code. WHT payable is recognized at accrual, and the payment to the supplier is net of WHT.
4. **Input VAT deductibility is a tax code.** A creditable code (`vat_input`) posts VAT to input VAT; a separate non-creditable code (`vat_input_non_deductible`) posts it to the non-deductible VAT expense account. There is no per-line `has_valid_tax_invoice` flag: choosing the code is the decision, and it is visible in the line, the ledger and the return.
5. **Tax codes have effective dates**, for example fuel at 4% from March 2026.
6. **Monthly tax summary**: output VAT, input VAT, net VAT, WHT by type (resident services/rent/interest/royalty, non-resident), and the prepayment base (turnover excluding VAT). Each box shows its USD and KHR amounts and drills down to source lines.
7. **CamInvoice is not in v1.** Invoices keep a stable ID, number, KHR total and party tax ID, so integration can be added later.

## To confirm with an adviser

- Whether the company keeps its books in USD or KHR, and any approval needed for USD books.
- The exact boxes on the current monthly return form, so the tax module's box mapping matches it.
- Whether the prepayment suspension applies to our companies, and whether a nil return is still required.
- Which other monthly taxes apply to us (salary, specific tax, accommodation, public lighting) and whether any belong in v1.
