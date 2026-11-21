import { TestBed } from '@angular/core/testing';
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest';
import { initialLanguage, LanguageService, LANGUAGE_KEY } from './language.service';

describe('language selection', () => {
  beforeEach(() => {
    localStorage.removeItem(LANGUAGE_KEY);
    TestBed.resetTestingModule();
  });
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.removeItem(LANGUAGE_KEY);
  });

  it('detects Chinese variants and falls back to English for unsupported languages', () => {
    for (const language of ['zh', 'zh-CN', 'zh-TW', 'ZH-hant'])
      expect(initialLanguage(null, [language])).toBe('zh');
    expect(initialLanguage(null, ['ja-JP', 'zh-CN'])).toBe('en');
    expect(initialLanguage(null, [])).toBe('en');
    expect(initialLanguage('invalid', ['zh-CN'])).toBe('zh');
  });

  it('honors a manual choice over the system language', () => {
    expect(initialLanguage('en', ['zh-CN'])).toBe('en');
    expect(initialLanguage('zh', ['en-US'])).toBe('zh');
  });

  it('starts in the system language and switches without losing its saved choice on reload', () => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['zh-CN']);
    const service = TestBed.inject(LanguageService);
    expect(service.language()).toBe('zh');
    expect(document.documentElement.lang).toBe('zh-CN');
    expect(service.translate('Plan prompts')).toBe('规划提示词');
    expect(service.translate('User original text')).toBe('User original text');
    service.select('en');
    expect(document.documentElement.lang).toBe('en');
    expect(document.title).toBe('ImageFlow — Continuous image generation');
    expect(localStorage.getItem(LANGUAGE_KEY)).toBe('en');
    TestBed.resetTestingModule();
    expect(TestBed.inject(LanguageService).language()).toBe('en');
  });

  it('switches even when browser storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const service = TestBed.inject(LanguageService);
    service.select('zh');
    expect(service.language()).toBe('zh');
    expect(document.title).toBe('ImageFlow — 持续图像生成');
    service.select('invalid');
    expect(service.language()).toBe('zh');
  });
});
