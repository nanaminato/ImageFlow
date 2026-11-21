import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { TranslatePipe } from '../../../../core/i18n/translate.pipe';

@Component({
  selector: 'app-bulk-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  template: `
    <div class="bulk-actions" role="group" [attr.aria-label]="'Bulk selection' | t">
      <label class="selection-label">
        <input type="checkbox" [checked]="allVisible()" [indeterminate]="someVisible() && !allVisible()"
          [disabled]="busy() || !visibleIds().length" (change)="toggleAll.emit(visibleIds())" />
        {{ 'Select all results' | t }}
      </label>
      <span role="status">{{ 'Selected' | t }}: {{ selectedCount() }}</span>
      <button type="button" class="filter-reset" (click)="clear.emit()" [disabled]="busy() || !selectedCount()">{{ 'Clear selection' | t }}</button>
      <button type="button" class="item-delete-button" (click)="remove.emit()" [disabled]="busy() || !selectedCount() || selectedCount() > 1000">{{ 'Delete selected' | t }}</button>
      @if (selectedCount() > visibleSelected()) { <span class="hint">{{ 'Selection includes results hidden by filters.' | t }}</span> }
      @if (selectedCount() > 1000) { <span class="error">{{ 'Select at most 1000 items per batch.' | t }}</span> }
    </div>
  `,
})
export class BulkActions {
  readonly visibleIds = input.required<string[]>();
  readonly selectedIds = input.required<ReadonlySet<string>>();
  readonly selectedCount = input.required<number>();
  readonly busy = input(false);
  readonly toggleAll = output<string[]>();
  readonly clear = output<void>();
  readonly remove = output<void>();
  readonly allVisible = computed(() => this.visibleIds().length > 0 && this.visibleIds().every(id => this.selectedIds().has(id)));
  readonly someVisible = computed(() => this.visibleIds().some(id => this.selectedIds().has(id)));
  readonly visibleSelected = computed(() => this.visibleIds().filter(id => this.selectedIds().has(id)).length);
}
