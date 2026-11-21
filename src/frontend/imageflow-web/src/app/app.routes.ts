import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/workspace/workspace-page').then((m) => m.WorkspacePage),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'create' },
      {
        path: 'create',
        loadComponent: () =>
          import('./features/workspace/pages/create-page').then((m) => m.CreatePage),
      },
      {
        path: 'prompts',
        loadComponent: () =>
          import('./features/workspace/pages/prompts-page').then((m) => m.PromptsPage),
      },
      {
        path: 'queue',
        loadComponent: () =>
          import('./features/workspace/pages/queue-page').then((m) => m.QueuePage),
      },
      {
        path: 'gallery',
        loadComponent: () =>
          import('./features/workspace/pages/gallery-page').then((m) => m.GalleryPage),
      },
    ],
  },
  { path: '**', redirectTo: 'create' },
];
