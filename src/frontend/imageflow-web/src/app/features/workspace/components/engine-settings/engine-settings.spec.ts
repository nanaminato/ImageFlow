import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { EngineSettings } from './engine-settings';
import { EngineSettings as Settings, SettingsInput } from '../../../../core/models/generation.models';

const config: Settings = {
  provider: 'OpenAI', promptModel: 'gpt-6-luna', imageModel: 'gpt-image-2.6-flare',
  maxConcurrency: 2, estimatedCostPerImageUsd: 0.01, revision: 1, estimateConfirmed: true,
  imageSize: '1024x1024', imageQuality: 'medium', imageComplexity: 'high', outputFormat: 'png',
  imageCapabilities: [
    { model: 'gpt-image-2.6-flare', sizes: ['1024x1024'], qualities: ['medium', 'high'], complexities: ['low', 'high'], formats: ['png'], customDimensions: true },
    { model: 'gpt-image-2.5-flare', sizes: ['1024x1024'], qualities: ['medium', 'high'], complexities: ['none'], formats: ['png'], customDimensions: true },
  ],
};
describe('image generation settings', () => {
  afterEach(() => TestBed.resetTestingModule());
  async function setup() {
    const fixture = TestBed.createComponent(EngineSettings);
    fixture.componentRef.setInput('settings', config);
    fixture.detectChanges(); await fixture.whenStable();
    return fixture;
  }
  it('switches model-specific options and emits the selected size and quality', async () => {
    const fixture = await setup(); const component = fixture.componentInstance;
    let saved: SettingsInput | undefined;
    component.save.subscribe(value => saved = value);
    component.model = 'gpt-image-2.5-flare'; component.changeModel();
    expect(component.complexity).toBe('none');
    component.size = '1536x864'; component.quality = 'high'; component.submit();
    expect(saved).toMatchObject({ imageModel: 'gpt-image-2.5-flare', imageSize: '1536x864', imageQuality: 'high', imageComplexity: 'none' });
    expect(component.ratio('1536x864')).toBe('16:9');
  });
  it('rejects invalid custom dimensions and accepts valid custom dimensions', async () => {
    const component = (await setup()).componentInstance;
    const saved: SettingsInput[] = []; component.save.subscribe(value => saved.push(value));
    component.size = 'custom'; component.width = 1025; component.height = 1024; component.submit();
    component.width = 3840; component.height = 3840; component.submit();
    expect(saved).toHaveLength(0);
    component.width = 1920; component.height = 1088; component.submit();
    expect(saved[0].imageSize).toBe('1920x1088');
  });
  it('limits presets and custom dimensions to the configured gateway capabilities', async () => {
    const fixture = await setup();
    fixture.componentRef.setInput('settings', { ...config, revision: 2, imageCapabilities: config.imageCapabilities!.map(c => ({ ...c, customDimensions: false })) });
    fixture.detectChanges(); await fixture.whenStable();
    const component = fixture.componentInstance;
    expect(component.sizes).toEqual(['1024x1024']);
    component.size = 'custom'; component.width = 1536; component.height = 864;
    expect(component.dimensionsValid).toBe(false);
  });
});
