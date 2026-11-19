import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/workspace/workspace-page').then((m) => m.WorkspacePage),
  },
  { path: '**', redirectTo: '' },
];
