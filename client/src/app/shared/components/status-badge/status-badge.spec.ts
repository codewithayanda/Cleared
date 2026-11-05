import { TestBed } from '@angular/core/testing';
import { InvoiceStatus } from '@core/models/invoice.model';
import { StatusBadge } from './status-badge';

describe('StatusBadge', () => {
  function textOf(status: InvoiceStatus, overdue?: boolean): string {
    const fixture = TestBed.createComponent(StatusBadge);
    fixture.componentRef.setInput('status', status);
    if (overdue !== undefined) {
      fixture.componentRef.setInput('overdue', overdue);
    }
    fixture.detectChanges();

    return (fixture.nativeElement as HTMLElement).textContent!.replace(/\s+/g, ' ').trim();
  }

  it('writes the status in words', () => {
    expect(textOf('PartiallyPaid')).toBe('Partially Paid');
  });

  it('says nothing about being overdue unless it is told', () => {
    expect(textOf('Issued')).toBe('Issued');
    expect(textOf('Issued', false)).toBe('Issued');
  });

  it('adds an Overdue pill beside the status when the invoice is overdue', () => {
    expect(textOf('Issued', true)).toBe('Issued Overdue');
    expect(textOf('PartiallyPaid', true)).toBe('Partially Paid Overdue');
  });
});
