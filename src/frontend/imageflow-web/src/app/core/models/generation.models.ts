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
  'Pending' | 'Running' | 'Completed' | 'Failed' | 'RetryWaiting' | 'Cancelled' | 'Unknown';
export type JobAction = 'pause' | 'resume' | 'cancel';
export interface Attempt {
  id: string;
  outcome: string;
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
}
export interface Job {
  id: string;
  planId: string;
  status: JobStatus;
  tasks: GenerationTask[];
}
export interface EngineSettings {
  provider: string;
  maxConcurrency: number;
  promptModel: string;
  imageModel: string;
}
export interface PlanInput {
  theme: string;
  count: number;
}
