import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { BookAsset, BooksApi, BookRequestType, FictionOrNonfiction } from '../../core/api/books.api';

type StepKey = 'project' | 'style' | 'content' | 'review';

const ALLOWED_UPLOAD_MIMES = [
  'text/plain',
  'text/markdown',
  'application/pdf',
  'application/msword',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
];
const ALLOWED_UPLOAD_EXTENSIONS = ['.txt', '.md', '.pdf', '.doc', '.docx'];
const MAX_UPLOAD_BYTES = 50 * 1024 * 1024;

@Component({
  selector: 'app-books-new',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <div class="p-8 max-w-3xl mx-auto">
      <div class="mb-8 flex items-center justify-between">
        <div>
          <p class="text-ink-400 text-sm">New project</p>
          <h1 class="font-display text-3xl font-semibold tracking-tight">Create a book</h1>
        </div>
        <a routerLink="/books" class="text-sm text-ink-400 hover:text-ink-100">Cancel</a>
      </div>

      <!-- Stepper -->
      <ol class="flex items-center gap-2 mb-8 text-xs uppercase tracking-wider">
        @for (s of steps; track s.key; let i = $index) {
          <li class="flex items-center gap-2">
            <span
              [class]="
                'w-7 h-7 rounded-full grid place-items-center border ' +
                (stepIndex() >= i
                  ? 'bg-brand-600 border-brand-500 text-white'
                  : 'border-ink-700 text-ink-500')
              "
            >{{ i + 1 }}</span>
            <span [class]="stepIndex() >= i ? 'text-ink-100' : 'text-ink-500'">{{ s.label }}</span>
            @if (i < steps.length - 1) {
              <span class="w-8 h-px bg-ink-700"></span>
            }
          </li>
        }
      </ol>

      <div class="glass rounded-2xl p-6 md:p-8">
        @switch (currentStep()) {
          @case ('project') {
            <form [formGroup]="projectForm" class="space-y-4">
              <label class="block">
                <span class="text-xs uppercase tracking-wider text-ink-400">Title *</span>
                <input formControlName="title" placeholder="The Apprentice's Compass"
                  class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5" />
              </label>
              <label class="block">
                <span class="text-xs uppercase tracking-wider text-ink-400">Subtitle</span>
                <input formControlName="subtitle"
                  class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5" />
              </label>
              <div class="grid grid-cols-2 gap-3">
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Genre</span>
                  <input formControlName="genre" placeholder="Memoir, business, fiction…"
                    class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5" />
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Fiction / Non-fiction</span>
                  <select formControlName="fictionOrNonfiction"
                    class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5">
                    <option [ngValue]="1">Non-fiction</option>
                    <option [ngValue]="0">Fiction</option>
                    <option [ngValue]="2">Mixed</option>
                  </select>
                </label>
              </div>
              <label class="block">
                <span class="text-xs uppercase tracking-wider text-ink-400">Target audience</span>
                <input formControlName="targetAudience" placeholder="e.g. Boilermakers entering management"
                  class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5" />
              </label>
              @if (projectId()) {
                <p class="text-[11px] text-ink-500">
                  Draft project saved. Edits to title/subtitle/genre on this step won't be re-saved
                  - the project keeps the values you set when you first advanced.
                </p>
              }
            </form>
          }
          @case ('style') {
            <form [formGroup]="styleForm" class="space-y-4">
              <div class="grid grid-cols-2 gap-3">
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Writing tone</span>
                  <input formControlName="writingTone" placeholder="warm, authoritative, conversational"
                    class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5" />
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">POV</span>
                  <select formControlName="povStyle"
                    class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5">
                    <option value="">-</option>
                    <option value="First">First person</option>
                    <option value="Third">Third person</option>
                    <option value="Omniscient">Omniscient</option>
                  </select>
                </label>
              </div>
              <div class="grid grid-cols-3 gap-3">
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Chapter length</span>
                  <select formControlName="chapterLength"
                    class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5">
                    <option value="short">Short (~2 000)</option>
                    <option value="medium">Medium (~3 500)</option>
                    <option value="long">Long (~6 000)</option>
                  </select>
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Target word count</span>
                  <input type="number" formControlName="targetWordCount" placeholder="90000"
                    class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5" />
                </label>
                <label class="block">
                  <span class="text-xs uppercase tracking-wider text-ink-400">Reading level</span>
                  <input formControlName="targetReadingLevel" placeholder="general adult"
                    class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5" />
                </label>
              </div>
              <label class="block">
                <span class="text-xs uppercase tracking-wider text-ink-400">Themes (comma separated)</span>
                <input formControlName="themesCsv" placeholder="leadership, resilience, mentorship"
                  class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5" />
              </label>
            </form>
          }
          @case ('content') {
            <form [formGroup]="contentForm" class="space-y-4">
              <label class="block">
                <span class="text-xs uppercase tracking-wider text-ink-400">Input type</span>
                <select formControlName="requestType"
                  class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5">
                  <option [ngValue]="0">Idea / prompt</option>
                  <option [ngValue]="1">Outline</option>
                  <option [ngValue]="2">Half manuscript</option>
                  <option [ngValue]="3">Expand existing book</option>
                  <option [ngValue]="4">Sermon → book</option>
                  <option [ngValue]="5">Notes → book</option>
                  <option [ngValue]="6">Blog → book</option>
                  <option [ngValue]="7">Course → book</option>
                  <option [ngValue]="8">Journal → book</option>
                  <option [ngValue]="9">Voice transcript</option>
                </select>
              </label>

              <!-- File uploads -->
              <div class="block">
                <span class="text-xs uppercase tracking-wider text-ink-400">Upload manuscripts (optional)</span>
                <label
                  for="manuscript-file-input"
                  class="mt-1 flex flex-col items-center justify-center gap-2 border border-dashed border-ink-600 hover:border-brand-500 rounded-lg px-4 py-6 cursor-pointer transition"
                  (dragover)="onDragOver($event)"
                  (drop)="onDrop($event)">
                  <svg xmlns="http://www.w3.org/2000/svg" class="w-6 h-6 text-ink-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="1.5">
                    <path stroke-linecap="round" stroke-linejoin="round"
                      d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5M16.5 12L12 16.5m0 0L7.5 12m4.5 4.5V3" />
                  </svg>
                  <span class="text-sm text-ink-300">
                    Drop .txt / .md / .pdf / .doc / .docx here, or
                    <span class="text-brand-400 underline">browse</span>
                  </span>
                  <span class="text-[11px] text-ink-500">Max 50 MB per file. Text is extracted automatically.</span>
                  <input id="manuscript-file-input" type="file" class="hidden"
                    [accept]="acceptAttr" multiple
                    (change)="onFilePicked($event)" />
                </label>

                @if (attachments().length > 0) {
                  <ul class="mt-3 space-y-1.5">
                    @for (a of attachments(); track a.id) {
                      <li class="flex items-center justify-between text-sm bg-ink-900/60 border border-ink-700 rounded-md px-3 py-2">
                        <span class="truncate">
                          {{ a.fileName }}
                          <span class="text-ink-500 text-xs">({{ formatBytes(a.fileSize) }})</span>
                        </span>
                        <button type="button" (click)="removeAttachment(a.id)"
                          class="text-xs text-rose-400 hover:text-rose-300">Remove</button>
                      </li>
                    }
                  </ul>
                }
                @if (uploadProgress().length > 0) {
                  <ul class="mt-2 space-y-1 text-xs text-ink-400">
                    @for (u of uploadProgress(); track u.name) {
                      <li>
                        <span class="text-ink-300">{{ u.name }}</span>
                        @if (u.error) { · <span class="text-rose-400">{{ u.error }}</span> }
                        @else { · uploading… }
                      </li>
                    }
                  </ul>
                }
              </div>

              <label class="block">
                <span class="text-xs uppercase tracking-wider text-ink-400">Idea / prompt</span>
                <textarea formControlName="ideaPrompt" rows="4" placeholder="What is this book about?"
                  class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5"></textarea>
              </label>
              <label class="block">
                <span class="text-xs uppercase tracking-wider text-ink-400">Existing content / notes (paste, optional)</span>
                <textarea formControlName="existingContent" rows="6" placeholder="Or paste raw text here instead of uploading."
                  class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5"></textarea>
              </label>
              <label class="block">
                <span class="text-xs uppercase tracking-wider text-ink-400">Additional AI instructions (optional)</span>
                <textarea formControlName="aIInstructions" rows="3"
                  class="mt-1 w-full bg-ink-900/60 border border-ink-700 focus:border-brand-500 focus:ring-1 focus:ring-brand-500/40 outline-none rounded-lg px-3 py-2.5"></textarea>
              </label>

              @if (!hasContentInput()) {
                <p class="text-[11px] text-rose-300">
                  Give the AI something to work with: upload a file, paste content, or type an idea prompt.
                </p>
              }
            </form>
          }
          @case ('review') {
            <div class="space-y-3 text-sm">
              <div class="grid grid-cols-2 gap-3">
                <div class="bg-ink-900/60 border border-ink-700 rounded-lg p-3">
                  <div class="text-xs uppercase tracking-wider text-ink-400">Title</div>
                  <div class="text-ink-100">{{ projectForm.value.title }}</div>
                </div>
                <div class="bg-ink-900/60 border border-ink-700 rounded-lg p-3">
                  <div class="text-xs uppercase tracking-wider text-ink-400">Genre</div>
                  <div class="text-ink-100">{{ projectForm.value.genre || '-' }}</div>
                </div>
              </div>
              <div class="bg-ink-900/60 border border-ink-700 rounded-lg p-3">
                <div class="text-xs uppercase tracking-wider text-ink-400">Tone &middot; POV &middot; Chapter length &middot; Target words</div>
                <div class="text-ink-100">
                  {{ styleForm.value.writingTone || '-' }} ·
                  {{ styleForm.value.povStyle || '-' }} ·
                  {{ styleForm.value.chapterLength }} ·
                  {{ styleForm.value.targetWordCount || '-' }}
                </div>
              </div>
              <div class="bg-ink-900/60 border border-ink-700 rounded-lg p-3">
                <div class="text-xs uppercase tracking-wider text-ink-400">Prompt</div>
                <div class="text-ink-100 whitespace-pre-wrap">{{ contentForm.value.ideaPrompt || '-' }}</div>
              </div>
              <div class="bg-ink-900/60 border border-ink-700 rounded-lg p-3">
                <div class="text-xs uppercase tracking-wider text-ink-400">
                  Uploaded files ({{ attachments().length }})
                </div>
                @if (attachments().length === 0) {
                  <div class="text-ink-500">None</div>
                } @else {
                  <ul class="text-ink-100 list-disc list-inside">
                    @for (a of attachments(); track a.id) {
                      <li>{{ a.fileName }} <span class="text-ink-500 text-xs">({{ formatBytes(a.fileSize) }})</span></li>
                    }
                  </ul>
                }
              </div>
              @if (error()) {
                <div class="text-sm text-rose-300 bg-rose-900/30 border border-rose-700/40 rounded-md px-3 py-2">
                  {{ error() }}
                </div>
              }
            </div>
          }
        }

        <div class="flex items-center justify-between mt-8">
          <button type="button"
            (click)="back()"
            [disabled]="stepIndex() === 0 || busy()"
            class="text-sm px-3 py-2 rounded-md border border-ink-700 hover:border-ink-500 disabled:opacity-30 disabled:cursor-not-allowed transition">
            Back
          </button>
          @if (currentStep() !== 'review') {
            <button type="button"
              (click)="next()"
              [disabled]="!stepValid() || busy()"
              class="bg-brand-600 hover:bg-brand-500 disabled:opacity-50 disabled:cursor-not-allowed text-white font-medium rounded-lg px-4 py-2 transition">
              {{ busy() ? 'Saving…' : 'Continue' }}
            </button>
          } @else {
            <button type="button"
              (click)="submit()"
              [disabled]="busy() || !projectId()"
              class="bg-gradient-to-r from-brand-600 to-fuchsia-600 hover:from-brand-500 hover:to-fuchsia-500 text-white font-medium rounded-lg px-4 py-2 transition">
              {{ busy() ? 'Submitting…' : 'Submit & queue AI' }}
            </button>
          }
        </div>
      </div>
    </div>
  `,
})
export class BooksNewComponent {
  private fb = inject(FormBuilder);
  private api = inject(BooksApi);
  private router = inject(Router);

  steps: { key: StepKey; label: string }[] = [
    { key: 'project', label: 'Project' },
    { key: 'style', label: 'Style' },
    { key: 'content', label: 'Content' },
    { key: 'review', label: 'Review' },
  ];

  stepIndex = signal(0);
  currentStep = computed<StepKey>(() => this.steps[this.stepIndex()].key);

  busy = signal(false);
  error = signal<string | null>(null);

  /** Set when step 1 creates the draft BookProject. Drives uploads and final Submit. */
  projectId = signal<string | null>(null);

  /** Files successfully uploaded to the draft project. */
  attachments = signal<BookAsset[]>([]);

  /** In-flight or recently failed uploads (cleared on success). */
  uploadProgress = signal<{ name: string; error?: string }[]>([]);

  acceptAttr = ALLOWED_UPLOAD_EXTENSIONS.join(',') + ',' + ALLOWED_UPLOAD_MIMES.join(',');

  projectForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(300)]],
    subtitle: [''],
    genre: [''],
    fictionOrNonfiction: [1 as FictionOrNonfiction, [Validators.required]],
    targetAudience: [''],
  });

  styleForm = this.fb.nonNullable.group({
    writingTone: [''],
    povStyle: [''],
    chapterLength: ['medium' as 'short' | 'medium' | 'long'],
    targetWordCount: this.fb.control<number | null>(null),
    targetReadingLevel: [''],
    themesCsv: [''],
  });

  contentForm = this.fb.nonNullable.group({
    requestType: [0 as BookRequestType, [Validators.required]],
    ideaPrompt: [''],
    existingContent: [''],
    aIInstructions: [''],
  });

  // FormGroup.valid is a plain getter - bridge each form's statusChanges/valueChanges into
  // signals so `computed` can react to user input.
  private projectStatus = toSignal(this.projectForm.statusChanges, {
    initialValue: this.projectForm.status,
  });
  private styleStatus = toSignal(this.styleForm.statusChanges, {
    initialValue: this.styleForm.status,
  });
  private contentValue = toSignal(this.contentForm.valueChanges, {
    initialValue: this.contentForm.value,
  });

  /** Step 3 needs at least one of: a typed prompt, pasted text, or an uploaded file. */
  hasContentInput = computed(() => {
    const v = this.contentValue();
    const prompt = (v?.ideaPrompt ?? '').trim();
    const pasted = (v?.existingContent ?? '').trim();
    return prompt.length > 0 || pasted.length > 0 || this.attachments().length > 0;
  });

  stepValid = computed(() => {
    switch (this.currentStep()) {
      case 'project':
        return this.projectStatus() === 'VALID';
      case 'style':
        return this.styleStatus() === 'VALID';
      case 'content':
        return this.hasContentInput();
      default:
        return true;
    }
  });

  next() {
    if (!this.stepValid() || this.busy()) return;

    // Crossing from step 1 → 2 is when we create the draft BookProject (once). All
    // uploads in step 3 attach to that project. Later edits to step-1 fields aren't
    // re-saved - by design for now (no PATCH endpoint), surfaced as a hint in the UI.
    if (this.currentStep() === 'project' && !this.projectId()) {
      this.busy.set(true);
      this.error.set(null);
      const p = this.projectForm.getRawValue();
      this.api
        .create({
          title: p.title,
          subtitle: p.subtitle || null,
          genre: p.genre || null,
          fictionOrNonfiction: p.fictionOrNonfiction,
          targetAudience: p.targetAudience || null,
          language: 'en',
        })
        .subscribe({
          next: (book) => {
            this.projectId.set(book.id);
            this.busy.set(false);
            this.stepIndex.update((i) => Math.min(i + 1, this.steps.length - 1));
          },
          error: (err) => {
            this.busy.set(false);
            this.error.set(
              err?.error?.detail ?? err?.error?.error ?? 'Could not create the draft project.',
            );
          },
        });
      return;
    }

    this.stepIndex.update((i) => Math.min(i + 1, this.steps.length - 1));
  }

  back() {
    if (this.busy()) return;
    this.stepIndex.update((i) => Math.max(i - 1, 0));
  }

  // ---- File handling -------------------------------------------------------

  onDragOver(e: DragEvent) {
    e.preventDefault();
    e.stopPropagation();
  }

  onDrop(e: DragEvent) {
    e.preventDefault();
    e.stopPropagation();
    const files = e.dataTransfer?.files;
    if (files && files.length) this.uploadFiles(Array.from(files));
  }

  onFilePicked(e: Event) {
    const input = e.target as HTMLInputElement;
    if (!input.files) return;
    this.uploadFiles(Array.from(input.files));
    input.value = ''; // allow re-selecting the same file
  }

  private uploadFiles(files: File[]) {
    const projectId = this.projectId();
    if (!projectId) {
      this.error.set('Draft project not created yet - go back to step 1 and click Continue.');
      return;
    }

    for (const file of files) {
      if (file.size > MAX_UPLOAD_BYTES) {
        this.recordUploadFailure(file.name, `Too large (max ${MAX_UPLOAD_BYTES / 1024 / 1024} MB).`);
        continue;
      }
      const extOk = ALLOWED_UPLOAD_EXTENSIONS.some((ext) =>
        file.name.toLowerCase().endsWith(ext),
      );
      if (!extOk) {
        this.recordUploadFailure(file.name, 'Unsupported file type.');
        continue;
      }

      this.uploadProgress.update((u) => [...u, { name: file.name }]);
      this.api.uploadAsset(projectId, file).subscribe({
        next: (asset) => {
          this.attachments.update((a) => [...a, asset]);
          this.uploadProgress.update((u) => u.filter((x) => x.name !== file.name));
        },
        error: (err) => {
          this.recordUploadFailure(
            file.name,
            err?.error?.detail ?? err?.error?.error ?? 'Upload failed.',
          );
        },
      });
    }
  }

  private recordUploadFailure(name: string, error: string) {
    this.uploadProgress.update((u) => {
      const without = u.filter((x) => x.name !== name);
      return [...without, { name, error }];
    });
  }

  removeAttachment(assetId: string) {
    const projectId = this.projectId();
    if (!projectId) return;
    this.api.deleteAsset(projectId, assetId).subscribe({
      next: () => this.attachments.update((a) => a.filter((x) => x.id !== assetId)),
      error: (err) =>
        this.error.set(err?.error?.detail ?? err?.error?.error ?? 'Could not remove attachment.'),
    });
  }

  formatBytes(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  // ---- Submit (step 4) -----------------------------------------------------

  submit() {
    const projectId = this.projectId();
    if (!projectId || this.busy()) return;
    if (!this.hasContentInput()) {
      this.error.set('Add a prompt, paste content, or upload a manuscript before submitting.');
      this.stepIndex.set(this.steps.findIndex((s) => s.key === 'content'));
      return;
    }
    this.busy.set(true);
    this.error.set(null);

    const s = this.styleForm.getRawValue();
    const c = this.contentForm.getRawValue();

    const variables = {
      schemaVersion: 1,
      writing: { chapterLength: s.chapterLength, tone: s.writingTone || undefined },
    };

    this.api
      .submit(projectId, {
        requestType: c.requestType,
        ideaPrompt: c.ideaPrompt || undefined,
        existingContent: c.existingContent || undefined,
        aIInstructions: c.aIInstructions || undefined,
        povStyle: s.povStyle || undefined,
        themesCsv: s.themesCsv || undefined,
        variables,
        priority: 5,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.router.navigate(['/books', projectId]);
        },
        error: (err) => {
          this.busy.set(false);
          this.error.set(
            err?.error?.detail ?? err?.error?.error ?? 'Submission failed. Try again?',
          );
        },
      });
  }
}
