export interface Prompt {
  id: string;
  text: string;
}
export interface Plan {
  id: string;
  theme: string;
  prompts: Prompt[];
}
export type JobStatus = 'Active' | 'Paused' | 'Cancelled' | 'Completed' | 'NeedsAttention';
export type TaskStatus =
  | 'Pending'
  | 'Running'
  | 'Completed'
  | 'Failed'
  | 'RetryWaiting'
  | 'Cancelled'
  | 'Unknown'
  | 'Paused';
export type JobAction = 'pause' | 'resume' | 'cancel';
export interface Attempt {
  id: string;
  outcome: string;
  startedAt: string;
  finishedAt: string | null;
  requestId: string | null;
  provider: string;
  estimatedCostUsd: number;
  costDisposition: string;
  model: string;
  usageJson: string | null;
  error: string | null;
}
export interface GenerationTask {
  id: string;
  status: TaskStatus;
  promptText: string;
  error: string | null;
  imagePath: string | null;
  attempts: Attempt[];
  reviews: TaskReview[];
  nextAttemptAt: string | null;
}
export interface Job {
  id: string;
  planId: string;
  status: JobStatus;
  tasks: GenerationTask[];
  mode: 'Finite' | 'Continuous';
  maxImages: number;
  budgetUsd: number | null;
  estimatedCostPerImageUsd: number;
  estimatedExposureUsd: number;
  stopReason: string | null;
}
export interface ImageCapability {
  model: string; sizes: string[]; qualities: string[]; complexities: string[];
  formats: string[]; customDimensions: boolean;
}
export interface EngineSettings {
  imageCapabilities?: ImageCapability[];
  provider: string;
  maxConcurrency: number;
  promptModel: string;
  imageModel: string;
  estimatedCostPerImageUsd: number;
  revision: number;
  estimateConfirmed: boolean;
  imageSize: string;
  imageQuality: string;
  imageComplexity: string;
  outputFormat: string;
}
export interface PlanInput {
  theme: string;
  count: number;
}

export interface SettingsInput {
  imageModel?: string;
  imageSize?: string;
  imageQuality?: string;
  imageComplexity?: string;
  maxConcurrency: number;
  estimatedCostPerImageUsd: number;
}
export interface WorkspaceClearResult {
  plans: number;
  jobs: number;
  tasks: number;
  images: number;
  remainingImageFiles: number;
}
export interface ItemDeletionResult {
  plans: number;
  jobs: number;
  images: number;
  pendingImageFiles: number;
}
export interface DeleteTarget {
  ids?: string[];
  kind: 'plan' | 'job' | 'image';
  id: string;
  label: string;
  jobs: number;
  images: number;
  running: boolean;
}
export interface JobOptions {
  mode: 'Finite' | 'Continuous';
  maxImages?: number;
  budgetUsd?: number;
}
export interface EnqueueInput {
  plan: Plan;
  options: JobOptions;
}

export interface TaskReview {
  id: string;
  attemptId: string;
  resolution: string;
  evidence: string;
  reviewedAt: string;
  retryAuthorizedAt: string | null;
}
export interface ReviewInput {
  attemptId: string;
  resolution: string;
  evidence: string;
  confirmed: boolean;
}
export interface RetryInput {
  attemptId: string;
  confirmed: boolean;
}
