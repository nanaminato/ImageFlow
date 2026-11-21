import { DOCUMENT, registerLocaleData } from '@angular/common';
import zhLocale from '@angular/common/locales/zh';
import { Injectable, inject, signal } from '@angular/core';
import { zh } from './zh';

registerLocaleData(zhLocale);

export type Language = 'en' | 'zh';
export const LANGUAGE_KEY = 'imageflow.language';

export function initialLanguage(saved: string | null, languages: readonly string[]): Language {
  if (saved === 'en' || saved === 'zh') return saved;
  return languages[0]?.toLowerCase().startsWith('zh') ? 'zh' : 'en';
}

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly document = inject(DOCUMENT);
  private readonly current = signal<Language>('en');
  readonly language = this.current.asReadonly();

  constructor() {
    const browser = this.document.defaultView;
    let saved: string | null = null;
    try {
      saved = browser?.localStorage.getItem(LANGUAGE_KEY) ?? null;
    } catch {
      /* Storage may be disabled. */
    }
    this.current.set(
      initialLanguage(saved, browser?.navigator.languages ?? [browser?.navigator.language ?? 'en']),
    );
    this.updateDocument();
  }

  select(language: string): void {
    if (language !== 'en' && language !== 'zh') return;
    this.current.set(language);
    try {
      this.document.defaultView?.localStorage.setItem(LANGUAGE_KEY, language);
    } catch {
      /* Switching still works in memory. */
    }
    this.updateDocument();
  }

  translate(text: string): string {
    return this.language() === 'zh' ? (zh[text] ?? text) : text;
  }

  private updateDocument(): void {
    this.document.documentElement.lang = this.language() === 'zh' ? 'zh-CN' : 'en';
    this.document.title = this.translate('ImageFlow — Continuous image generation');
  }
}
