import { HttpErrorResponse } from '@angular/common/http';
import { WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject, of } from 'rxjs';
import { Account } from '@core/models/auth.model';
import { TenantResponse } from '@core/models/tenant.model';
import { AccountService } from '@core/services/account.service';
import { TenantService } from '@core/services/tenant.service';
import { CompanySettings } from './company-settings';

const tenant: TenantResponse = {
  id: 'tenant-1',
  companyName: 'Acme Ltd',
  tradingName: null,
  vatStatus: 'NotRegistered',
  vatNumber: null,
  address: '1 Main Road',
  bankName: null,
  bankAccountNumber: null,
  bankBranchCode: null,
};

const refusedForEmail = () =>
  new HttpErrorResponse({ status: 403, error: { code: 'email_not_verified' } });

describe('CompanySettings', () => {
  let account: WritableSignal<Account | null>;
  let update: Subject<TenantResponse>;

  beforeEach(() => {
    account = signal<Account | null>(null);
    TestBed.configureTestingModule({
      imports: [CompanySettings],
      providers: [
        {
          provide: TenantService,
          useValue: {
            getMine: () => of(tenant),
            updateProfile: () => (update = new Subject<TenantResponse>()),
          },
        },
        { provide: AccountService, useValue: { account } },
      ],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(CompanySettings);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const save = () => {
      element.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
      fixture.detectChanges();
    };

    return { fixture, save, text: () => element.textContent ?? '' };
  }

  it('tells someone whose email is not confirmed that banking details wait for it', () => {
    account.set({ email: 'a@b.co.za', emailVerified: false });

    const { text } = render();

    expect(text()).toContain('Confirm your email address to change banking details');
  });

  it('says nothing about it once the email is confirmed', () => {
    account.set({ email: 'a@b.co.za', emailVerified: true });

    const { text } = render();

    expect(text()).not.toContain('Confirm your email address');
  });

  it('says nothing about it while the account is not yet known', () => {
    const { text } = render();

    expect(text()).not.toContain('Confirm your email address');
  });

  it('says so when the changes were saved', () => {
    const { fixture, save, text } = render();

    save();
    update.next(tenant);
    update.complete();
    fixture.detectChanges();

    expect(text()).toContain('Saved.');
  });

  it('explains plainly why banking details were refused for an unconfirmed email', () => {
    const { fixture, save, text } = render();

    save();
    update.error(refusedForEmail());
    fixture.detectChanges();

    expect(text()).toContain('Confirm your email address first');
    expect(text()).not.toContain('Saved.');
  });

  it('says plainly that saving failed for any other reason', () => {
    const { fixture, save, text } = render();

    save();
    update.error(new HttpErrorResponse({ status: 500 }));
    fixture.detectChanges();

    expect(text()).toContain('Could not save. Please try again.');
  });

  it('clears an old message when the person tries again', () => {
    const { fixture, save, text } = render();

    save();
    update.error(new HttpErrorResponse({ status: 500 }));
    fixture.detectChanges();
    save();

    expect(text()).not.toContain('Could not save');
  });
});
