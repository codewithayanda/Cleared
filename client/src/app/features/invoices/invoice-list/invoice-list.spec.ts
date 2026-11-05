import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Invoice } from '@core/models/invoice.model';
import { InvoiceService } from '@core/services/invoice.service';
import { anInvoice } from '@core/testing/invoices';
import { InvoiceList } from './invoice-list';

const onTime = anInvoice({ id: 'a', number: 'INV-2026-0001' });
const late = anInvoice({
  id: 'b',
  number: 'INV-2026-0002',
  dueDate: '2026-10-15',
  isOverdue: true,
});
const partPaidLate = anInvoice({
  id: 'c',
  number: 'INV-2026-0003',
  status: 'PartiallyPaid',
  dueDate: '2026-10-20',
  isOverdue: true,
});
const draft = anInvoice({ id: 'd', number: null, status: 'Draft', dueDate: null });

describe('InvoiceList', () => {
  function render(invoices: Invoice[]) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: InvoiceService, useValue: { list: () => of(invoices) } },
      ],
    });
    const fixture = TestBed.createComponent(InvoiceList);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const squash = (text: string | null) => (text ?? '').replace(/\s+/g, ' ').trim();
    const rows = () => [...element.querySelectorAll('tbody tr')];

    return {
      rows: () => rows().map((row) => squash(row.textContent)),
      dueDateCell: (index: number) => rows()[index].querySelectorAll('td')[2],
      filterLabel: () => squash(element.querySelector('label')!.textContent),
      tickOverdueOnly: () => {
        element.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
        fixture.detectChanges();
      },
      search: (text: string) => {
        const box = element.querySelector<HTMLInputElement>('input[type="text"]')!;
        box.value = text;
        box.dispatchEvent(new Event('input'));
        fixture.detectChanges();
      },
      text: () => squash(element.textContent),
    };
  }

  it('flags an overdue invoice with a pill and a red due date, and leaves the others alone', () => {
    const { rows, dueDateCell } = render([onTime, late]);

    expect(rows()[0]).not.toContain('Overdue');
    expect(dueDateCell(0).classList).not.toContain('text-status-overdue');
    expect(rows()[1]).toContain('Overdue');
    expect(dueDateCell(1).classList).toContain('text-status-overdue');
  });

  it('flags an overdue invoice that has been part paid, and still says it is part paid', () => {
    const { rows } = render([partPaidLate]);

    expect(rows()[0]).toContain('Partially Paid');
    expect(rows()[0]).toContain('Overdue');
  });

  it('leaves a draft alone, which has no due date to show', () => {
    const { rows, dueDateCell } = render([draft]);

    expect(rows()[0]).not.toContain('Overdue');
    expect(dueDateCell(0).textContent).not.toMatch(/\d/);
  });

  it('counts the overdue invoices on the filter', () => {
    const { filterLabel } = render([onTime, late, partPaidLate]);

    expect(filterLabel()).toBe('Overdue only (2)');
  });

  it('shows every invoice until Overdue only is ticked, and every invoice again when it is not', () => {
    const { rows, tickOverdueOnly } = render([onTime, late, partPaidLate]);

    expect(rows()).toHaveLength(3);

    tickOverdueOnly();

    expect(rows()).toHaveLength(2);
    expect(rows()[0]).toContain('INV-2026-0002');
    expect(rows()[1]).toContain('INV-2026-0003');

    tickOverdueOnly();

    expect(rows()).toHaveLength(3);
  });

  it('says so when Overdue only is ticked and nothing is overdue', () => {
    const { rows, tickOverdueOnly } = render([onTime]);

    tickOverdueOnly();

    expect(rows()).toEqual(['No overdue invoices.']);
  });

  it('combines Overdue only with the search by number', () => {
    const { rows, tickOverdueOnly, search } = render([onTime, late, partPaidLate]);
    tickOverdueOnly();

    search('0003');

    expect(rows()).toHaveLength(1);
    expect(rows()[0]).toContain('INV-2026-0003');

    search('0001');

    expect(rows()).toEqual(['No overdue invoices match your search.']);
  });

  it('says no invoice matches when a search finds nothing', () => {
    const { rows, search } = render([onTime, late]);

    search('nothing like this');

    expect(rows()).toEqual(['No invoices match your search.']);
  });

  it('still welcomes someone with no invoices at all', () => {
    const { text } = render([]);

    expect(text()).toContain('No invoices yet');
  });
});
