import { CommonModule } from '@angular/common';
import { Component, Input, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BookDetail } from '../../core/api/books.api';
import { MadCloudTaskService, MadCloudWorkflowArea } from '../../core/api/madcloud-task.service';

interface ReadinessItem {
  label: string;
  done: boolean;
  detail: string;
}

@Component({
  selector: 'app-production-center',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <section class="production-center" aria-label="Production readiness center">
      <div class="production-center__header">
        <div>
          <p class="production-center__eyebrow">Publishing command center</p>
          <h2>Next best action: {{ nextAction().label }}</h2>
          <p>{{ nextAction().detail }}</p>
        </div>
        <div class="production-center__score" [class.is-ready]="readinessScore() >= 80">
          <strong>{{ readinessScore() }}%</strong>
          <span>ready</span>
        </div>
      </div>

      <div class="production-center__grid">
        <article>
          <h3>Production gates</h3>
          <ul>
            <li *ngFor="let item of readinessItems()">
              <span [class.done]="item.done">{{ item.done ? '✓' : '•' }}</span>
              <div>
                <strong>{{ item.label }}</strong>
                <small>{{ item.detail }}</small>
              </div>
            </li>
          </ul>
        </article>

        <article>
          <h3>Derivative products</h3>
          <div class="chips">
            <span *ngFor="let item of derivativeIdeas">{{ item }}</span>
          </div>
        </article>

        <article>
          <h3>MADProspects integrations</h3>
          <div class="integration-list">
            <div *ngFor="let item of integrations">
              <strong>{{ item.name }}</strong>
              <small>{{ item.detail }}</small>
            </div>
          </div>
        </article>
      </div>
      <div class="production-center__actions">
        <a [routerLink]="['/books', book.id, 'read']">Reader</a>
        <a [routerLink]="['/books', book.id, 'metadata']">Metadata</a>
        <a [routerLink]="['/books', book.id, 'covers']">Covers</a>
        <a [routerLink]="['/books', book.id, 'assets']">Assets</a>
        <a [routerLink]="['/books', book.id, 'exports']">Exports</a>
        <a [routerLink]="['/books', book.id, 'translate']">Translation</a>
        <button type="button" (click)="createMadCloudTask('Metadata')" [disabled]="taskBusy">
          {{ taskBusy ? 'Creating...' : 'Metadata/launch task' }}
        </button>
      </div>
      @if (taskMessage) {
        <p class="production-center__message">{{ taskMessage }}</p>
      }
    </section>
  `,
  styles: [`
    :host { display: block; }
    .production-center {
      border: 1px solid rgba(124, 58, 237, .24);
      border-radius: 8px;
      background:
        linear-gradient(135deg, rgba(15, 23, 42, .92), rgba(30, 27, 75, .92)),
        #0f172a;
      box-shadow: 0 24px 60px rgba(0, 0, 0, .22);
      color: #f8fafc;
      overflow: hidden;
    }
    .production-center__header {
      display: flex;
      justify-content: space-between;
      gap: 1rem;
      align-items: flex-start;
      padding: 1.25rem;
      border-bottom: 1px solid rgba(255, 255, 255, .08);
    }
    .production-center__eyebrow {
      margin: 0 0 .35rem;
      color: #67e8f9;
      font-size: .72rem;
      font-weight: 900;
      text-transform: uppercase;
      letter-spacing: 0;
    }
    h2, h3, p { letter-spacing: 0; }
    h2 { margin: 0; font-size: clamp(1.2rem, 2.2vw, 1.65rem); }
    h3 { margin: 0 0 .75rem; font-size: .95rem; color: #ddd6fe; }
    p { margin: .45rem 0 0; color: #cbd5e1; line-height: 1.5; }
    .production-center__score {
      width: 86px;
      min-width: 86px;
      aspect-ratio: 1;
      display: grid;
      place-content: center;
      text-align: center;
      border-radius: 8px;
      background: rgba(251, 191, 36, .14);
      border: 1px solid rgba(251, 191, 36, .28);
      color: #fde68a;
    }
    .production-center__score.is-ready {
      background: rgba(16, 185, 129, .14);
      border-color: rgba(16, 185, 129, .35);
      color: #a7f3d0;
    }
    .production-center__score strong { font-size: 1.45rem; line-height: 1; }
    .production-center__score span { font-size: .72rem; font-weight: 900; text-transform: uppercase; }
    .production-center__grid {
      display: grid;
      grid-template-columns: minmax(0, 1.15fr) minmax(0, .85fr) minmax(0, 1fr);
      gap: 1px;
      background: rgba(255,255,255,.08);
    }
    article { background: rgba(15, 23, 42, .78); padding: 1rem; min-width: 0; }
    .production-center__actions {
      display: flex;
      flex-wrap: wrap;
      gap: .5rem;
      padding: 1rem;
      border-top: 1px solid rgba(255, 255, 255, .08);
    }
    .production-center__actions a,
    .production-center__actions button {
      border: 1px solid rgba(103, 232, 249, .22);
      border-radius: 8px;
      padding: .55rem .75rem;
      background: rgba(8, 145, 178, .1);
      color: #cffafe;
      font-size: .8rem;
      font-weight: 900;
    }
    .production-center__actions button:disabled { opacity: .6; cursor: wait; }
    .production-center__message { padding: 0 1rem 1rem; margin: 0; color: #86efac; font-size: .82rem; }
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: .7rem; }
    li { display: flex; gap: .65rem; align-items: flex-start; }
    li span:first-child {
      width: 1.4rem;
      height: 1.4rem;
      display: inline-grid;
      place-items: center;
      border-radius: 999px;
      background: rgba(148, 163, 184, .14);
      color: #cbd5e1;
      font-weight: 900;
      flex: 0 0 auto;
    }
    li span.done { background: rgba(16, 185, 129, .18); color: #a7f3d0; }
    strong { display: block; color: #f8fafc; }
    small { display: block; color: #94a3b8; line-height: 1.4; margin-top: .1rem; }
    .chips { display: flex; flex-wrap: wrap; gap: .5rem; }
    .chips span {
      border: 1px solid rgba(103, 232, 249, .25);
      border-radius: 999px;
      padding: .45rem .65rem;
      background: rgba(8, 145, 178, .12);
      color: #cffafe;
      font-size: .78rem;
      font-weight: 800;
    }
    .integration-list { display: grid; gap: .7rem; }
    @media (max-width: 900px) {
      .production-center__header { display: grid; }
      .production-center__score { width: auto; min-width: 0; aspect-ratio: auto; padding: .75rem; }
      .production-center__grid { grid-template-columns: 1fr; }
    }
  `],
})
export class ProductionCenterComponent {
  @Input({ required: true }) book!: BookDetail;
  taskBusy = false;
  taskMessage = '';

  constructor(private madcloud: MadCloudTaskService) {}

  readonly derivativeIdeas = [
    'Workbook',
    'Course outline',
    'Launch kit',
    'Beta-reader pack',
    'Devotional edition',
    'Recruiting playbook',
    'CRM lead magnet',
    'MADLearn lessons',
  ];

  readonly integrations = [
    { name: 'MADRecruiting', detail: 'Convert role knowledge into onboarding guides and recruiter playbooks.' },
    { name: 'MADProspects CRM', detail: 'Use the book as a lead magnet, proposal book, or nurture sequence source.' },
    { name: 'MADLearn', detail: 'Turn chapters into lessons, quizzes, slides, and facilitator notes.' },
    { name: 'MADCloud', detail: 'Route all AI execution, callbacks, and operator work through one service boundary.' },
  ];

  readonly readinessItems = computed<ReadinessItem[]>(() => {
    const b = this.book;
    const chaptersWithContent = b.chapters.filter((chapter) => chapter.wordCount > 0 || chapter.status >= 2).length;
    return [
      {
        label: 'Project brief',
        done: !!(b.title && b.genre && b.targetAudience && b.writingTone),
        detail: 'Title, genre, audience, and tone guide every downstream agent.',
      },
      {
        label: 'Outline gate',
        done: !b.requireOutlineApproval || !!b.outlineApprovedAt,
        detail: b.requireOutlineApproval ? 'Approve or edit the outline before full drafting.' : 'Outline approval is not required for this project.',
      },
      {
        label: 'Manuscript content',
        done: b.chapters.length > 0 && chaptersWithContent === b.chapters.length,
        detail: `${chaptersWithContent} of ${b.chapters.length} chapters contain manuscript text.`,
      },
      {
        label: 'Publishing metadata',
        done: b.workflowStage >= 5,
        detail: 'KDP description, keywords, bio, copyright, and blanks should be reviewed.',
      },
      {
        label: 'Export readiness',
        done: chaptersWithContent > 0,
        detail: 'Generate PDF, DOCX, EPUB, Markdown, HTML, and print proofs when content is ready.',
      },
    ];
  });

  readonly readinessScore = computed(() => {
    const items = this.readinessItems();
    if (!items.length) return 0;
    return Math.round((items.filter((item) => item.done).length / items.length) * 100);
  });

  readonly nextAction = computed(() => {
    return this.readinessItems().find((item) => !item.done) ?? {
      label: 'Final proof and launch packaging',
      done: true,
      detail: 'Review exports, cover, metadata, and derivative products before publication.',
    };
  });

  createMadCloudTask(area: MadCloudWorkflowArea) {
    if (this.taskBusy) return;
    this.taskBusy = true;
    this.taskMessage = '';
    this.madcloud.createBookTask(
      this.book,
      area,
      `Prepare publishing metadata, launch copy, and next-step guidance for "${this.book.title}".`,
    ).subscribe({
      next: () => {
        this.taskBusy = false;
        this.taskMessage = 'MADCloud task created.';
      },
      error: () => {
        this.taskBusy = false;
        this.taskMessage = 'Could not create MADCloud task.';
      },
    });
  }
}
