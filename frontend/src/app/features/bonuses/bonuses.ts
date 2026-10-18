import { ChangeDetectorRef, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { catchError, finalize, of, startWith, Subject, switchMap } from 'rxjs';
import { Bonus, BonusFilter } from '../../core/models/bonus';
import { BonusStates, BonusTypes, MasterStateCodes } from '../../core/models/catalog';
import { PagedResponse } from '../../core/models/paged-response';
import { BonusApi } from '../../core/services/bonus-api';
import { CatalogApi } from '../../core/services/catalog-api';
import { Notification } from '../../core/services/notification';
import { NotificationBanner } from '../../core/notification-banner';
import { money, percent, periodPattern } from './bonus-preview';
import { RegisterBonus } from './register-bonus';

@Component({
  selector: 'app-bonuses',
  imports: [ReactiveFormsModule, CdkMenu, CdkMenuItem, CdkMenuTrigger, RegisterBonus, NotificationBanner],
  templateUrl: './bonuses.html',
  styleUrl: './bonuses.css',
})
export class Bonuses {
  private readonly catalogApi = inject(CatalogApi);
  private readonly api = inject(BonusApi);
  private readonly notification = inject(Notification);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly requests = new Subject<number>();
  private applied: BonusFilter = {};
  protected readonly form = inject(FormBuilder).nonNullable.group({
    departmentCode: '', employeeNumber: ['', [Validators.pattern(/^[1-9]\d*$/), Validators.max(2147483647)]],
    period: ['', Validators.pattern(periodPattern)], stateCode: '',
  });
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly registering = signal(false);
  private readonly confirmation = viewChild.required<ElementRef<HTMLDialogElement>>('confirmation');
  protected readonly selected = signal<{ bonus: Bonus; action: 'pay' | 'cancel' | 'delete' } | null>(null);
  protected readonly labels = { pay: 'Pagar', cancel: 'Anular', delete: 'Eliminar' };
  protected readonly consequences = {
    pay: 'El bruto se repartirá entre los proyectos del empleado según sus horas y el bono quedará Pagado.',
    cancel: 'El bono quedará Anulado: seguirá en el listado, pero ya no admitirá cambios.',
    delete: 'El bono dejará de aparecer en el listado, aunque su registro se conserva.',
  };
  protected readonly page = signal<PagedResponse<Bonus> | null>(null);
  protected readonly money = money;
  protected readonly states = BonusStates;
  protected readonly types = BonusTypes;
  protected readonly percent = percent;
  protected readonly filtered = signal(false);

  protected readonly departments = toSignal(this.catalogApi.getDepartments().pipe(catchError(() => of([]))), { initialValue: [] });
  protected readonly bonusStates = toSignal(this.catalogApi.getStates(MasterStateCodes.Bonus).pipe(catchError(() => of([]))), { initialValue: [] });

  constructor() {
    this.requests.pipe(startWith(1), switchMap(page => {
      this.loading.set(true);
      this.failed.set(false);
      return this.api.list(this.applied, page).pipe(
        catchError(() => { this.failed.set(true); return of(null); }),
        finalize(() => this.loading.set(false)),
      );
    }), takeUntilDestroyed()).subscribe(page => {
      if (page && page.pageNumber > Math.max(page.totalPages, 1)) this.load(Math.max(page.totalPages, 1));
      else {
        this.filtered.set(Object.keys(this.applied).length > 0);
        this.page.set(page);
      }
    });
  }

  protected search() {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    const values = this.form.getRawValue();
    this.applied = Object.fromEntries(Object.entries({ ...values,
      employeeNumber: values.employeeNumber ? Number(values.employeeNumber) : undefined,
    }).filter(([, value]) => value !== '' && value !== undefined));
    this.load(1);
  }

  protected clear() { this.form.reset(); this.search(); }
  protected load(page: number) { this.requests.next(page); }
  protected closeRegistration(saved: boolean) { this.registering.set(false); if (saved) this.load(1); }

  protected change(bonus: Bonus, action: 'pay' | 'cancel' | 'delete') {
    if (this.busy() || this.loading() || bonus.stateCode !== BonusStates.Registered) return;
    this.notification.clear();
    this.selected.set({ bonus, action });
    // El contenido debe estar listo antes de recibir el foco.
    this.changeDetector.detectChanges();
    // Se abre cuando el menú ya devolvió el foco a su botón; así el diálogo lo recupera al cerrarse.
    queueMicrotask(() => this.confirmation().nativeElement.showModal());
  }

  protected dismissConfirmation() {
    this.confirmation().nativeElement.close();
    this.selected.set(null);
  }

  protected confirmChange() {
    const selected = this.selected();
    if (!selected || this.busy() || this.loading()) return;
    const { bonus, action } = selected;
    this.dismissConfirmation();
    this.busy.set(true);
    this.api[action](bonus.bonusId).pipe(
      finalize(() => this.busy.set(false)),
    ).subscribe({
      next: () => { this.notification.show('success', { pay: 'Bono pagado.', cancel: 'Bono anulado.', delete: 'Bono eliminado.' }[action]); this.load(this.page()?.pageNumber ?? 1); },
      error: () => this.load(this.page()?.pageNumber ?? 1),
    });
  }
}
