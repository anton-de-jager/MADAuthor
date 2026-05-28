from __future__ import annotations

from pathlib import Path
from typing import Iterable

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import cm
from reportlab.platypus import (
    BaseDocTemplate,
    Frame,
    FrameBreak,
    KeepTogether,
    ListFlowable,
    ListItem,
    NextPageTemplate,
    PageBreak,
    PageTemplate,
    Paragraph,
    Spacer,
    Table,
    TableStyle,
)


ROOT = Path(__file__).resolve().parents[1]
OUT_MANUAL = ROOT / "MADAuthorUserManual.pdf"
OUT_IDEAS = ROOT / "MADAuthorIdeas.pdf"


BRAND = colors.HexColor("#7C3AED")
BRAND_DARK = colors.HexColor("#1F1633")
ACCENT = colors.HexColor("#0EA5A4")
INK = colors.HexColor("#111827")
MUTED = colors.HexColor("#4B5563")
SOFT = colors.HexColor("#F4F1FF")
LINE = colors.HexColor("#DDD6FE")


def make_styles():
    base = getSampleStyleSheet()
    styles = {
        "title": ParagraphStyle(
            "Title",
            parent=base["Title"],
            fontName="Helvetica-Bold",
            fontSize=30,
            leading=36,
            textColor=BRAND_DARK,
            alignment=TA_CENTER,
            spaceAfter=14,
        ),
        "subtitle": ParagraphStyle(
            "Subtitle",
            parent=base["BodyText"],
            fontSize=13,
            leading=18,
            textColor=MUTED,
            alignment=TA_CENTER,
            spaceAfter=18,
        ),
        "h1": ParagraphStyle(
            "Heading1",
            parent=base["Heading1"],
            fontName="Helvetica-Bold",
            fontSize=19,
            leading=23,
            textColor=BRAND_DARK,
            spaceBefore=14,
            spaceAfter=8,
            keepWithNext=True,
        ),
        "h2": ParagraphStyle(
            "Heading2",
            parent=base["Heading2"],
            fontName="Helvetica-Bold",
            fontSize=13.5,
            leading=17,
            textColor=BRAND,
            spaceBefore=8,
            spaceAfter=5,
            keepWithNext=True,
        ),
        "body": ParagraphStyle(
            "Body",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=9.6,
            leading=13.2,
            textColor=INK,
            spaceAfter=6,
        ),
        "small": ParagraphStyle(
            "Small",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=8.4,
            leading=11,
            textColor=MUTED,
        ),
        "bullet": ParagraphStyle(
            "Bullet",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=9.2,
            leading=12.8,
            leftIndent=9,
            firstLineIndent=0,
            textColor=INK,
        ),
        "callout": ParagraphStyle(
            "Callout",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=9.2,
            leading=12.6,
            textColor=BRAND_DARK,
            borderColor=LINE,
            borderWidth=0.7,
            borderPadding=8,
            backColor=SOFT,
            spaceBefore=6,
            spaceAfter=8,
        ),
        "table_head": ParagraphStyle(
            "TableHead",
            parent=base["BodyText"],
            fontName="Helvetica-Bold",
            fontSize=8.3,
            leading=10,
            textColor=colors.white,
        ),
        "table": ParagraphStyle(
            "Table",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=8.0,
            leading=10.2,
            textColor=INK,
        ),
    }
    return styles


def p(text: str, style: str = "body"):
    return Paragraph(text.replace("\n", "<br/>"), STYLES[style])


def bullets(items: Iterable[str]):
    return ListFlowable(
        [ListItem(p(item, "bullet"), leftIndent=10) for item in items],
        bulletType="bullet",
        start="circle",
        leftIndent=16,
        bulletFontSize=5,
        spaceAfter=6,
    )


def data_table(rows: list[list[str]], widths: list[float] | None = None):
    data = []
    for i, row in enumerate(rows):
        style = "table_head" if i == 0 else "table"
        data.append([p(cell, style) for cell in row])
    table = Table(data, colWidths=widths, hAlign="LEFT", repeatRows=1)
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), BRAND),
                ("GRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#E5E7EB")),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#FAFAFF")]),
                ("LEFTPADDING", (0, 0), (-1, -1), 6),
                ("RIGHTPADDING", (0, 0), (-1, -1), 6),
                ("TOPPADDING", (0, 0), (-1, -1), 5),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
            ]
        )
    )
    return table


class MadDocTemplate(BaseDocTemplate):
    def __init__(self, filename: Path, title: str):
        super().__init__(
            str(filename),
            pagesize=A4,
            rightMargin=1.65 * cm,
            leftMargin=1.65 * cm,
            topMargin=1.85 * cm,
            bottomMargin=1.55 * cm,
            title=title,
            author="MADProspects",
        )
        frame = Frame(self.leftMargin, self.bottomMargin, self.width, self.height, id="normal")
        self.addPageTemplates([PageTemplate(id="normal", frames=[frame], onPage=self._header_footer)])
        self.doc_title = title

    def _header_footer(self, canvas, doc):
        canvas.saveState()
        canvas.setStrokeColor(LINE)
        canvas.setLineWidth(0.4)
        canvas.line(self.leftMargin, A4[1] - 1.25 * cm, A4[0] - self.rightMargin, A4[1] - 1.25 * cm)
        canvas.setFont("Helvetica", 8)
        canvas.setFillColor(MUTED)
        canvas.drawString(self.leftMargin, A4[1] - 1.05 * cm, self.doc_title)
        canvas.drawRightString(A4[0] - self.rightMargin, 0.95 * cm, f"Page {doc.page}")
        canvas.restoreState()


def title_page(title: str, subtitle: str, note: str):
    return [
        Spacer(1, 5.2 * cm),
        p(title, "title"),
        p(subtitle, "subtitle"),
        Spacer(1, 0.6 * cm),
        p(note, "callout"),
        PageBreak(),
    ]


def section(title: str, body: list):
    return [p(title, "h1"), *body]


def subsection(title: str, body: list):
    return [p(title, "h2"), *body]


def build_manual():
    story = []
    story += title_page(
        "MADAuthor User Manual",
        "A detailed guide to the AI-assisted book authoring, publishing, cover, export, and operations platform.",
        "This manual describes the application as implemented in the current MADAuthor repository: Angular web app, .NET 8 API, SQL Server persistence, Hangfire jobs, SignalR notifications, cover tooling, file uploads, exports, translations, and the MAD Cloud operator queue.",
    )

    story += section(
        "1. What MADAuthor Is",
        [
            p(
                "MADAuthor is an AI-assisted book production workspace for people who need to turn ideas, notes, sermons, courses, manuscripts, interviews, or source files into publishable book projects. It is not only a writing prompt screen. It manages the full lifecycle: intake, planning, chapter generation, review, continuity checks, metadata, cover selection, exports, translations, and operational monitoring."
            ),
            p(
                "The application has three major faces: the author-facing web app, the API and background job platform, and the administrative/operator tools. Authors work with projects and book content. The API owns identity, data, storage, export rendering, and realtime updates. Admins monitor AI jobs, scheduled work, and the MAD Cloud task queue."
            ),
            data_table(
                [
                    ["Area", "What it does"],
                    ["Author workspace", "Create projects, upload source material, approve outlines, review chapters, manage characters, choose covers, edit metadata, export files, and read the manuscript."],
                    ["AI pipeline", "Plans the book, researches when needed, drafts chapters, edits text, checks continuity, prepares publisher metadata, suggests cover direction, and can produce marketing materials."],
                    ["Publishing tools", "Maintains metadata, descriptions, keywords, copyright/front matter, cover art, KDP/Ingram print-wrap PDFs, and downloadable exports."],
                    ["Operations", "Admin queue, worker heartbeats, Hangfire dashboard, health checks, Claude task queue, prompt templates, scanner/worker toggles, and deployment workflow."],
                ],
                [3.1 * cm, 12.8 * cm],
            ),
        ],
    )

    story += section(
        "2. Main Users and Permissions",
        [
            p("MADAuthor is role-aware. A normal author should only see their own company/projects. Admin and Owner users can access operational screens that ordinary authors should not see."),
            bullets(
                [
                    "<b>Authors and creators</b> create book projects, upload material, review generated content, and export publishable files.",
                    "<b>Coaches, churches, consultants, and course creators</b> can use the intake formats to turn teaching material, sermons, training notes, workshops, or transcripts into books and companion products.",
                    "<b>Admins and Owners</b> use queue dashboards, worker controls, prompt template management, health checks, and deployment-related tools.",
                    "<b>Workers</b> are not human app users. They claim queued jobs, write progress, persist generated results, and mark jobs complete or failed.",
                ]
            ),
            p(
                "Security is based on JWT access tokens and httpOnly refresh tokens. Company and membership records provide tenant isolation. File downloads and generated assets are routed through the API rather than exposed as unprotected public folders."
            ),
        ],
    )

    story += section(
        "3. Application Map",
        [
            data_table(
                [
                    ["Route or screen", "Purpose"],
                    ["/", "/home", "Public landing page for the product."],
                    ["/login, /register, /confirm-email", "Account access, new user registration, and email confirmation."],
                    ["/dashboard", "Authenticated landing area with project status and quick access into active work."],
                    ["/books", "Book library and project list."],
                    ["/books/new", "Four-step project creation wizard."],
                    ["/books/:id", "Main book workspace: outline, chapters, metadata, covers, translations, exports, assets, and progress."],
                    ["/books/:id/read", "Immersive manuscript reader with chapter and page navigation."],
                    ["/admin/queue", "AI job queue and worker heartbeat dashboard."],
                    ["/admin/claude", "MAD Cloud operator queue for development/operations tasks, templates, imports, and deploy workflow."],
                ],
                [4.5 * cm, 11.4 * cm],
            ),
            p("The author spends most time in three places: the new-book wizard, the book detail workspace, and the reader. The admin spends most time in the queue and MAD Cloud pages."),
        ],
    )

    story += section(
        "4. Creating a Book Project",
        [
            p("The new-book wizard is intentionally structured. It captures enough information for the AI pipeline to plan and write coherently before any long-running job is queued."),
            *subsection(
                "Step 1 - Project",
                [
                    p("The project step captures the title, subtitle, genre, whether the book is fiction or nonfiction, the intended audience, and the basic project identity. Once this step is complete, MADAuthor can create a draft project record so uploads and later settings have a project to attach to."),
                    bullets(
                        [
                            "Use the title and subtitle fields as working values. They can be refined later by the Publisher metadata agent.",
                            "Set the genre and fiction/nonfiction type carefully. These values influence outline structure, voice, metadata, and cover search prompts.",
                            "Audience should be concrete. 'New managers in manufacturing' is more useful than 'everyone'.",
                        ]
                    ),
                ],
            ),
            *subsection(
                "Step 2 - Style",
                [
                    p("The style step captures creative direction: tone, point of view, target chapter length, target word count, reading level, themes, and other writing controls. These values become part of the BookRequest variables used by the prompt templates."),
                    bullets(
                        [
                            "Tone guides the feel of the writing, such as warm, practical, academic, devotional, cinematic, humorous, direct, or inspirational.",
                            "Point of view matters for memoir, fiction, business books, and instructional books. The pipeline should know whether it is writing first person, third person, or a guide-style voice.",
                            "Target word count and chapter length help the Planner and Writer agents decide chapter count and depth.",
                            "Themes and keywords help the AI keep emphasis consistent across the manuscript and later metadata.",
                        ]
                    ),
                ],
            ),
            *subsection(
                "Step 3 - Content",
                [
                    p("The content step is the intake heart of MADAuthor. The user selects a request type and supplies the raw material that the book should be based on."),
                    data_table(
                        [
                            ["Request type", "Best use"],
                            ["Idea", "Start from a concept, market angle, theme, or book premise."],
                            ["Outline", "Start from an existing chapter structure that needs expansion."],
                            ["Half manuscript", "Continue, restructure, or complete a partially written book."],
                            ["Expand existing book", "Turn a short book, booklet, or notes into a fuller manuscript."],
                            ["Sermon to book", "Convert sermon notes/transcripts into a devotional, teaching book, or study guide."],
                            ["Notes to book", "Use rough notes, voice notes, workshops, or planning material as the source."],
                            ["Blog, course, journal, voice transcript", "Turn existing content streams into long-form book projects."],
                        ],
                        [4.2 * cm, 11.7 * cm],
                    ),
                    p("Uploads support text and document formats such as TXT, Markdown, PDF, DOC, and DOCX, with a project upload cap of 50 MB per file. Extracted text is available to the AI pipeline so uploaded source material can be stitched into the request instead of being ignored."),
                ],
            ),
            *subsection(
                "Step 4 - Review",
                [
                    p("The review step lets the author confirm the project, style, content, and instructions before the AI pipeline is queued. This is the last cheap moment to fix unclear direction."),
                    bullets(
                        [
                            "Check that the project goal, tone, audience, and content type agree with each other.",
                            "Add AI instructions for non-obvious requirements: denominational boundaries, citation expectations, forbidden topics, desired structure, or brand voice.",
                            "Submit queues the AI work and returns the project to the main book workspace where progress can be monitored.",
                        ]
                    ),
                ],
            ),
        ],
    )

    story += section(
        "5. The AI Production Pipeline",
        [
            p("MADAuthor uses a staged pipeline rather than one huge prompt. That makes the system easier to monitor, retry, and improve. Each agent has a clear responsibility and writes structured output back to the database."),
            data_table(
                [
                    ["Stage", "Result"],
                    ["Intake", "BookRequest captures prompt, source text, uploads, variables, features, priority, and platform goals."],
                    ["Planner", "Creates chapter plan, narrative arc, section logic, research needs, and in fiction projects, core character scaffolding."],
                    ["Researcher", "Builds dossiers, facts, citations, and source notes where the request requires research or authority."],
                    ["Writer", "Drafts chapters in Markdown against word targets, style controls, and approved outline direction."],
                    ["Editor", "Improves grammar, flow, clarity, consistency, and readability without losing the intended voice."],
                    ["Continuity", "Reads final chapters together and flags contradictions, missing setups, timeline problems, duplicated material, or unresolved arcs."],
                    ["Publisher", "Creates KDP description, short description, keywords, subtitle suggestions, front matter, author bio scaffold, endorsements scaffold, copyright/ISBN page text, and category ideas."],
                    ["Cover", "Creates cover search/generation prompts and stores selectable cover assets."],
                    ["Marketing", "Can produce launch copy, social posts, email drafts, ad concepts, and campaign checklists."],
                    ["Exports", "Renders downloadable manuscript formats and print-oriented outputs."],
                ],
                [3.1 * cm, 12.8 * cm],
            ),
            p("Progress is written to the AI job queue and pushed to the browser over SignalR. This allows the author to see status, stage, percentage, and errors without repeatedly refreshing the page."),
        ],
    )

    story += section(
        "6. Book Workspace",
        [
            p("The book detail page is the central cockpit for an active project. It combines manuscript progress, outline approval, chapter lists, metadata, covers, translations, assets, and export actions."),
            bullets(
                [
                    "<b>Progress card:</b> shows current stage, percentage, status, and errors from the queue.",
                    "<b>Outline approval:</b> lets the author inspect the plan before the system moves deeper into drafting. If approval is required, the pipeline should wait for it.",
                    "<b>Edit details:</b> lets project metadata be corrected as the book evolves.",
                    "<b>Authors and body font:</b> controls author attribution and export typography for reader-facing documents.",
                    "<b>Chapters:</b> shows generated chapter summaries and content status. Chapters can be reviewed and regenerated when needed.",
                    "<b>Assets:</b> lists uploaded files and generated assets with download/delete controls.",
                    "<b>Exports:</b> queues and downloads document formats when content is ready.",
                ]
            ),
            p("A strong workflow is to approve the outline only after checking that each chapter has a distinct purpose, the order builds logically, and the promised outcome for the reader is visible."),
        ],
    )

    story += section(
        "7. Outline Editing",
        [
            p("Before approval, the outline editor allows the author to rename chapters, reorder them, add new chapters, remove unwanted chapters, and edit chapter summaries. Chapter numbers are recomputed when saved."),
            bullets(
                [
                    "Every chapter needs a title before the outline can be saved.",
                    "Summaries are optional but valuable because they steer chapter drafting.",
                    "Reordering should be used to improve narrative or instructional flow before chapter writing begins.",
                    "Deleting a chapter with meaningful content prompts for confirmation to avoid accidental loss.",
                ]
            ),
            p("For nonfiction, the outline should move from promise to foundation to application to next steps. For fiction, it should build tension, reversals, character choice, and resolution."),
        ],
    )

    story += section(
        "8. Characters",
        [
            p("For fiction and narrative nonfiction, MADAuthor stores book characters so the writing and continuity stages can stay consistent. Characters can include names, roles, descriptions, relationships, arcs, and notes."),
            p("Character management is especially useful when the book contains recurring people, composite characters, interview subjects, leaders, students, congregants, customers, or fictional cast members. A character record gives the pipeline a stable reference point instead of relying on memory across chapters."),
        ],
    )

    story += section(
        "9. Cover Designer",
        [
            p("The cover picker combines source images, AI generation, Unsplash search, template-based design, front/back previews, selected cover persistence, and print-wrap PDF output."),
            data_table(
                [
                    ["Capability", "Details"],
                    ["Unsplash search", "Search royalty-free images using title, genre, tone, or custom query. Photographer attribution is stored and displayed."],
                    ["AI generation", "Generate a source image from an optional prompt and style such as cinematic, minimalist, oil painting, photographic, illustration, or vintage book cover."],
                    ["Template gallery", "Choose from Bold Gradient, Classic Centered, Modern Minimal, Penguin Stripe, Magazine Block, Author Spotlight, Night Owl, and Golden Age design directions."],
                    ["Live preview", "Render front and back cover previews from the selected source image, book title, subtitle, author, and template."],
                    ["Apply design", "Persist the selected front cover design as the active cover asset."],
                    ["Print wrap", "Render a KDP/Ingram-style wrap PDF that combines back cover, spine, and front cover with page count and paper type controls."],
                ],
                [3.6 * cm, 12.3 * cm],
            ),
            p("A practical cover workflow is: search or generate several source images, select the strongest visual, preview multiple templates, apply the best front cover, then render a wrap PDF only after page count is known."),
        ],
    )

    story += section(
        "10. Publisher Metadata",
        [
            p("The metadata editor exposes the Publisher agent's JSON output in an author-friendly way. It is designed to preserve unknown fields so future publisher data can round-trip without loss."),
            bullets(
                [
                    "<b>KDP description:</b> long HTML-capable product description, with a 4,000 character counter.",
                    "<b>Short description:</b> concise hook suitable for website cards, ads, or sales pages.",
                    "<b>Refined subtitle:</b> optional improved subtitle that can override the original working subtitle.",
                    "<b>Keywords:</b> comma-separated keyword list, with a target of no more than seven KDP keywords.",
                    "<b>Dedication and acknowledgements:</b> Markdown-friendly front matter text.",
                    "<b>ISBN/copyright page:</b> copyright and publication text.",
                    "<b>Author bio:</b> target 80-120 words, with live word count.",
                    "<b>Endorsements scaffold:</b> placeholders for testimonials, reviewers, or launch quotes.",
                    "<b>Find blanks:</b> scans for placeholders such as [PEN NAME] or [BETA READER 1].",
                ]
            ),
            p("This screen turns AI-generated publishing copy into editable production data. The author should replace placeholders, verify claims, check keyword relevance, and make sure all names and legal statements are correct before export or upload to retailers."),
        ],
    )

    story += section(
        "11. Reader Experience",
        [
            p("The reader view presents a manuscript in a more book-like format than the management workspace. It includes a chapter sidebar, paged viewing, page flip animation, chapter and page navigation, keyboard arrow support, chapter word counts, and regeneration controls."),
            p("Use the reader for quality control. It helps the author notice pacing, repetition, chapter length, heading rhythm, and awkward transitions that are harder to see in admin-style tables."),
        ],
    )

    story += section(
        "12. Assets and Uploads",
        [
            p("Book assets store source files and generated project files. The API supports listing, uploading, downloading, and deleting assets. Extracted text from supported documents can feed the book request so the AI pipeline has access to source material."),
            bullets(
                [
                    "Supported source types include common text and document formats such as PDF, Word, Markdown, and TXT.",
                    "Files are stored through a storage abstraction. The current phase uses local filesystem storage under project folders, but the architecture can move to Azure Blob, S3, or another provider.",
                    "File access should happen through API endpoints or signed URLs so tenant isolation and authorization are preserved.",
                ]
            ),
        ],
    )

    story += section(
        "13. Exports",
        [
            p("MADAuthor includes renderers for multiple publishing and review formats. Exports are queued so long-running rendering does not block the browser."),
            data_table(
                [
                    ["Format", "Use"],
                    ["PDF", "General reading, review, proofing, and sharing."],
                    ["Print PDF", "Print-oriented interior output for KDP or Ingram-style workflows."],
                    ["EPUB", "Ebook distribution and reader apps."],
                    ["DOCX", "Word-based editing, editorial review, and collaborator markup."],
                    ["HTML", "Web preview, landing pages, or further conversion."],
                    ["Markdown", "Portable plain-text manuscript storage and developer-friendly review."],
                ],
                [3.1 * cm, 12.8 * cm],
            ),
            p("Exports depend on manuscript readiness. If chapters are still drafting or publisher metadata has not been produced, export buttons may be disabled or the output may be incomplete."),
        ],
    )

    story += section(
        "14. Translation",
        [
            p("The book workspace includes translation controls once final chapters exist. Current UI options include Spanish, French, German, Portuguese, Italian, Dutch, Polish, Turkish, Arabic, Japanese, Korean, Mandarin Chinese, and Afrikaans."),
            p("Translation should be treated as a publishing workflow, not a magic button. The exported translation needs human review for idiom, cultural assumptions, scripture/citation conventions, legal claims, and market-specific metadata."),
        ],
    )

    story += section(
        "15. Admin Queue and Worker Operations",
        [
            p("The admin queue shows AI jobs and worker heartbeats. It is built for visibility into long-running work that may span planning, writing, editing, export, and other tasks."),
            bullets(
                [
                    "Workers claim jobs atomically so two workers do not process the same job.",
                    "Jobs move through states such as Pending, Claimed, InProgress, Completed, Failed, or Cancelled.",
                    "Progress, stage, retries, and errors are persisted for troubleshooting.",
                    "Failed jobs can be retried when the problem is temporary, such as a network interruption or provider timeout.",
                    "Worker heartbeat data helps admins see whether scheduled workers are alive.",
                ]
            ),
            p("The API also exposes health endpoints, including readiness checks used after deployment. Hangfire provides a separate dashboard for deterministic background jobs such as exports, cleanup, notifications, and metrics."),
        ],
    )

    story += section(
        "16. MAD Cloud Operator Queue",
        [
            p("MAD Cloud is the application-facing operator queue for Claude/Codex-style development and operations tasks. It is separate from the book-generation AIJobQueue. It helps manage internal tasks, prompt templates, attachments, scanner/worker toggles, and deployment workflow."),
            data_table(
                [
                    ["Feature", "Details"],
                    ["Task list", "Shows active, to-be-deployed, and terminal tasks with priorities, attachments, status, and relative updated time."],
                    ["Statuses", "Pending, InProgress, ToBeDeployed, Completed, Cancelled, Failed, and Deferred."],
                    ["Priorities", "Critical, High, Normal, and Low indicators."],
                    ["Realtime updates", "SignalR pushes task created, updated, and deleted events."],
                    ["Attachments", "Files can be added to new or existing tasks."],
                    ["Bulk import", "Imports a JSON array or an object with an items key; reports created and skipped tasks."],
                    ["Templates", "Stores reusable prompt/task templates and can open a new task from a template."],
                    ["Worker controls", "Toggles workerActive, scannerActive, and deployNext settings."],
                ],
                [3.6 * cm, 12.3 * cm],
            ),
        ],
    )

    story += section(
        "17. Architecture and Deployment",
        [
            p("MADAuthor is currently structured as an Angular SPA under apps/web and a .NET 8 API under apps/api/MadAuthor.Api, with infrastructure, workers, prompt packages, and Codex desktop worker scripts in separate folders."),
            data_table(
                [
                    ["Component", "Responsibility"],
                    ["Angular SPA", "Browser UI, route guards, book workspace, admin screens, SignalR client, and API calls."],
                    [".NET 8 API", "Authentication, project APIs, storage, queue orchestration, SignalR hub, Hangfire dashboard, and health endpoints."],
                    ["SQL Server", "Users, companies, projects, requests, chapters, assets, covers, exports, notifications, AI jobs, worker heartbeats, audit logs, and Claude tasks."],
                    ["Hangfire", "Deterministic background jobs such as export rendering and operational jobs."],
                    ["Claude worker", "Agentic planning, writing, editing, metadata, and operator task execution using the database as the contract."],
                    ["FTP/Plesk deployment", "Builds and uploads API/FE artifacts, recycles IIS using app_offline.htm, and warms health endpoints."],
                ],
                [3.7 * cm, 12.2 * cm],
            ),
            p("Production URLs are https://madauthor.madprospects.com for the web app and https://madauthorapi.madprospects.com for the API. Readiness is checked at /api/health/ready. The Hangfire dashboard is at /hangfire and should remain gated to Admin/Owner users."),
        ],
    )

    story += section(
        "18. Practical Author Workflow",
        [
            data_table(
                [
                    ["Phase", "Checklist"],
                    ["Prepare", "Clarify audience, outcome, genre, tone, source files, and publishing goal."],
                    ["Create", "Use the four-step wizard and provide specific AI instructions."],
                    ["Plan", "Review and edit outline before approval; make chapters distinct and useful."],
                    ["Draft", "Monitor progress; wait for chapter generation and editing to complete."],
                    ["Review", "Use the reader; check pacing, repetition, claims, names, scripture/citations, and continuity."],
                    ["Package", "Edit metadata, choose cover, generate wrap if needed, prepare exports."],
                    ["Publish", "Verify retailer requirements, final proof, ISBN/copyright text, and metadata before upload."],
                ],
                [3.0 * cm, 12.9 * cm],
            ),
            p("The best results come from treating MADAuthor as a production partner. Give it clear direction, review each stage, and use the tooling to turn generated drafts into intentional books."),
        ],
    )

    story += section(
        "19. Troubleshooting",
        [
            bullets(
                [
                    "<b>Login or refresh issues:</b> confirm the API URL and frontend URL match the current environment, and that CORS allows the web origin.",
                    "<b>Uploads fail:</b> verify file type, file size, authentication, and server storage permissions.",
                    "<b>AI job stuck:</b> check worker heartbeat, queue status, job errors, retry count, and worker settings.",
                    "<b>Exports missing:</b> confirm chapters exist and are final enough for the selected export type.",
                    "<b>Cover images missing:</b> check API asset URLs, selected cover status, Unsplash/API provider keys, and whether image endpoints require authorization.",
                    "<b>Production health fails:</b> verify deployed connection strings, SQL reachability from the host, app pool status, and readiness endpoint response.",
                ]
            ),
            p("When troubleshooting production, remember that the database host is reachable from the deployed server, not necessarily from a developer machine. The readiness endpoint is the practical production connectivity check."),
        ],
    )

    story += section(
        "20. Detailed Intake Field Reference",
        [
            p("The quality of the output is strongly tied to the quality of the intake data. MADAuthor stores the visible wizard fields and a richer JSON variable set behind the scenes. These fields help prompt templates adapt to different book types without hard-coding a single creative style."),
            data_table(
                [
                    ["Field group", "What to capture", "Why it matters"],
                    ["Book identity", "Title, subtitle, genre, fiction/nonfiction, language, audience, and publishing goal.", "These values drive outline shape, reader promise, metadata, cover direction, and export naming."],
                    ["Voice and style", "Tone, point of view, reading level, humor, emotion, spirituality, academic depth, practicality, and desired examples.", "Keeps the book from sounding generic and helps every chapter feel like it belongs to the same author."],
                    ["Length and structure", "Target word count, chapter length, desired sections, workbook requirements, questions, summaries, and action steps.", "Guides the Planner and Writer so the final manuscript has the right scale."],
                    ["Source material", "Idea prompt, existing content, extracted upload text, notes, sermon transcript, course material, or manuscript fragment.", "Provides grounding. The AI should not invent what is already available in the user's material."],
                    ["Publishing settings", "Trim size, margins, typography, target platforms, ISBN/copyright needs, metadata, and retailer constraints.", "Prevents late-stage export surprises and lets the Publisher agent prepare practical output."],
                    ["Special features", "Study guide, references, devotional, children's version, illustrations, audio, slides, teacher guide, translations, and marketing assets.", "Allows the project to become a family of products rather than a single PDF."],
                ],
                [3.0 * cm, 6.0 * cm, 6.9 * cm],
            ),
            p("A strong intake note includes the reader, the transformation promised to the reader, the author's authority, examples to include, boundaries to avoid, and any non-negotiable phrases or beliefs."),
        ],
    )

    story += section(
        "21. Book Type Playbooks",
        [
            p("Different book types should be reviewed with different expectations. The same software screens are used, but the author's quality checklist changes."),
            data_table(
                [
                    ["Book type", "Recommended workflow"],
                    ["Memoir", "Capture timeline, major turning points, people, places, sensitive details, and lessons. Review for privacy, factual sequence, emotional arc, and whether every chapter earns its place."],
                    ["Business book", "Define reader pain, framework, examples, case studies, practical steps, and credibility markers. Review for actionable structure, repeated framework language, and claim support."],
                    ["Devotional", "Set theological tone, scripture translation preference, devotional rhythm, prayer style, reflection questions, and group-use expectations. Review every scripture reference and doctrinal claim."],
                    ["Sermon series", "Upload transcripts or notes, preserve the pastoral voice, identify repeated themes, and decide whether the output is a book, study guide, devotional, or all three."],
                    ["Course-to-book", "Bring modules, lessons, exercises, slides, and assignments. Review for flow because course order is not always the best book order."],
                    ["Fiction", "Define genre promise, POV, cast, world rules, conflict, plot beats, and desired ending. Review for continuity, motivation, pacing, and voice."],
                    ["Workbook/journal", "Focus on prompts, worksheets, spacing, instructions, answer areas, and action plans. Exports need layout review more than prose review."],
                ],
                [3.0 * cm, 12.9 * cm],
            ),
        ],
    )

    story += section(
        "22. Quality Review Checklist",
        [
            bullets(
                [
                    "Does the outline deliver the exact reader promise described in the intake?",
                    "Does every chapter have a distinct job, or do several chapters repeat the same point?",
                    "Is the opening chapter strong enough to make the reader trust the journey?",
                    "Are uploaded sources reflected accurately and respectfully?",
                    "Are names, dates, places, organizations, statistics, scripture references, and quotations correct?",
                    "Are placeholder fields such as [PEN NAME], [SPOUSE NAME], or [BETA READER] removed before export?",
                    "Does the title/subtitle/description/keywords package match what the book actually delivers?",
                    "Does the cover communicate the genre and reader promise at thumbnail size?",
                    "Are export files named clearly and downloaded after generation?",
                    "Has a human read the final output in reader mode before publishing?",
                ]
            ),
            p("A useful internal rule is: AI can accelerate production, but the author owns truth, taste, and permission. MADAuthor should make those review responsibilities visible instead of hiding them."),
        ],
    )

    story += section(
        "23. Data Entities in Plain English",
        [
            data_table(
                [
                    ["Entity", "Plain-language meaning"],
                    ["User / Company / CompanyMember", "Who can log in, which tenant they belong to, and what role they have."],
                    ["Author", "The public or pen-name identity attached to a book."],
                    ["BookProject", "The central record for a book: identity, state, workflow stage, progress, target audience, genre, language, publishing goal, and deadlines."],
                    ["BookRequest", "The instruction package that starts or shapes AI work: prompt, source content, variables, requested features, status, and priority."],
                    ["BookChapter", "A planned, drafted, edited, or final manuscript chapter with number, title, summary, content, and status."],
                    ["BookCharacter", "A stable record for people or fictional characters used by writing and continuity agents."],
                    ["BookAsset", "Uploaded or generated files tied to a project, including extracted text where available."],
                    ["BookCover", "Selected or generated cover source/design assets, attribution, status, style, and designed output URL."],
                    ["BookExport", "A queued or completed export file such as PDF, EPUB, DOCX, HTML, Markdown, or print PDF."],
                    ["AIJobQueue", "The work contract used by workers to plan, write, edit, translate, export, or otherwise process the book."],
                    ["WorkerHeartbeat", "A signal that a worker is alive and when it last checked in."],
                    ["ClaudeTask", "The separate operator/development task queue shown in MAD Cloud."],
                    ["AuditLog", "Operational record of important actions for traceability."],
                ],
                [4.0 * cm, 11.9 * cm],
            ),
        ],
    )

    story += section(
        "24. API and Background Capability Reference",
        [
            p("The API surface maps closely to user workflows. The names below are conceptual groupings rather than an exhaustive endpoint list, but they describe what the backend is responsible for."),
            data_table(
                [
                    ["Controller area", "Responsibilities"],
                    ["Auth", "Login, session/refresh handling, registration, email confirmation, superadmin/bootstrap flows, and user identity."],
                    ["Books", "Project creation, project details, chapters, outline approval/editing, regeneration, authors, project settings, and core book state."],
                    ["BookAssets", "Upload, list, download, delete, and text extraction for source files and generated assets."],
                    ["BookCharacters", "CRUD for character/person records associated with a project."],
                    ["Covers", "Unsplash search, AI image generation, selecting covers, rendering design previews, applying designs, and wrap PDF generation."],
                    ["Exports", "Queue, list, download, and delete generated export files."],
                    ["PublisherMetadata", "Read and update the Publisher agent's metadata JSON."],
                    ["Translation", "Queue translated versions after chapters are final enough."],
                    ["Notifications", "SignalR notification delivery and group membership."],
                    ["Admin", "Queue monitoring, worker heartbeats, failed job retry, and privileged operational actions."],
                    ["Settings", "Application settings used by MAD Cloud worker controls."],
                    ["ClaudeTasks / ClaudePromptTemplates", "Operator queue CRUD, attachments, import, statuses, templates, and task realtime updates."],
                    ["Health", "Liveness/readiness checks for deployment and database connectivity verification."],
                ],
                [4.1 * cm, 11.8 * cm],
            ),
        ],
    )

    story += section(
        "25. Admin Operating Rhythm",
        [
            p("A production admin should have a small routine that catches most issues early."),
            data_table(
                [
                    ["Frequency", "Checks"],
                    ["Daily", "Open admin queue, confirm worker heartbeat, inspect failed jobs, confirm health readiness, and check recent exports."],
                    ["After deploy", "Visit the SPA, login, call readiness endpoint, test one authenticated API action, and confirm CORS/browser console is clean."],
                    ["Weekly", "Review job durations, storage growth, pending tasks, prompt/template changes, and user-reported quality issues."],
                    ["Before major prompt changes", "Run a small golden set: idea-to-outline, upload-to-outline, chapter draft, metadata generation, cover preview, export."],
                    ["Before public launch", "Confirm backups, admin/superadmin access, tenant isolation, SMTP, storage permissions, production URLs, and deploy rollback procedure."],
                ],
                [3.0 * cm, 12.9 * cm],
            ),
            p("Operational reliability matters because book generation can take time. Users will forgive a long job if they can see honest progress and receive a usable result; they will not forgive silence."),
        ],
    )

    MadDocTemplate(OUT_MANUAL, "MADAuthor User Manual").build(story)


def build_ideas():
    story = []
    story += title_page(
        "MADAuthor Ideas",
        "Enhancement roadmap and MADProspects Universe integration concepts.",
        "This document proposes product, AI, publishing, operations, and ecosystem ideas. It is deliberately expansive: some items are quick wins, while others are platform bets that connect MADAuthor with the broader MADProspects universe.",
    )

    story += section(
        "1. Product Vision",
        [
            p("MADAuthor can become the publishing engine for the MADProspects Universe: a system that turns knowledge, conversations, campaigns, training, sermons, recruiting material, and business expertise into books and companion products."),
            p("The strongest direction is not only 'AI writes books'. The stronger platform promise is 'MADProspects captures valuable knowledge and MADAuthor packages it into professional, reusable, monetizable intellectual property.'"),
            bullets(
                [
                    "Authoring engine for books, workbooks, devotionals, manuals, playbooks, proposals, onboarding packs, and curriculum.",
                    "Publishing layer for lead magnets, authority assets, campaign content, and client deliverables.",
                    "Knowledge repackaging tool that turns MADProspects data into long-form assets with structure and metadata.",
                    "Shared AI/prompt infrastructure that improves across all MAD applications.",
                ]
            ),
        ],
    )

    story += section(
        "2. High-Impact UX Enhancements",
        [
            data_table(
                [
                    ["Idea", "Why it matters"],
                    ["Project timeline", "Show the full journey from intake through exports with completed/current/blocked stages."],
                    ["Inline chapter editor", "Let users edit generated Markdown directly in the browser with autosave, comments, and compare-to-original."],
                    ["Version history", "Allow rollback between outline, chapter, metadata, and cover versions."],
                    ["Manuscript quality dashboard", "Highlight repetition, chapter imbalance, reading level, missing citations, placeholder text, and continuity warnings."],
                    ["Guided approval gates", "Make outline, first chapter sample, metadata, and final proof approval explicit checkpoints."],
                    ["Project templates", "One-click setup for memoir, devotional, business book, sermon series, workbook, course book, fiction, journal, and children's book."],
                    ["Smart next action", "At the top of each project, tell the user the most important next step: approve outline, replace placeholders, review chapter 3, choose cover, or export."],
                ],
                [4.0 * cm, 11.9 * cm],
            ),
            p("The product should always make the next useful action obvious. Authors should feel momentum, not a pile of tabs."),
        ],
    )

    story += section(
        "3. AI Pipeline Improvements",
        [
            bullets(
                [
                    "<b>First chapter sample approval:</b> generate one sample chapter after the outline and ask the author to approve voice before drafting the whole book.",
                    "<b>Style memory:</b> let users upload writing samples and create reusable voice profiles for future projects.",
                    "<b>Source-grounded drafting:</b> require the Writer to cite which uploaded asset or research note informed each major section.",
                    "<b>Continuity repair loop:</b> automatically create targeted edit jobs for contradictions found by the Continuity agent.",
                    "<b>Claim checker:</b> flag statistics, legal/medical/financial claims, names, dates, and unverifiable statements.",
                    "<b>Scripture/citation guardrails:</b> for Christian or academic books, track translation, reference style, and citation confidence.",
                    "<b>Human-in-the-loop critique:</b> offer editorial lenses such as developmental editor, copy editor, theological reviewer, business reader, educator, or target-market beta reader.",
                    "<b>Prompt evaluation suite:</b> keep golden test projects and compare agent output after prompt/template changes.",
                ]
            ),
            p("Pipeline quality should be measured with durable checks: chapter completeness, word target variance, placeholder removal, outline adherence, source usage, and export readiness."),
        ],
    )

    story += section(
        "4. Research and Source Management",
        [
            data_table(
                [
                    ["Feature", "Description"],
                    ["Source library", "Every uploaded file becomes searchable, taggable, and quote-addressable."],
                    ["Evidence map", "Show which chapters use which uploaded sources, interview notes, sermons, or web research."],
                    ["Citation manager", "Generate endnotes, bibliography, scripture references, links, and citation styles."],
                    ["Interview ingestion", "Upload audio, transcribe, summarize, extract quotes, and mark sensitive/private segments."],
                    ["OCR cleanup", "Improve scanned PDFs by detecting poor OCR, duplicate headers, page numbers, and footers."],
                    ["Fact packet export", "Give reviewers a document containing claims, sources, and verification notes."],
                ],
                [4.0 * cm, 11.9 * cm],
            ),
        ],
    )

    story += section(
        "5. Editing, Collaboration, and Review",
        [
            bullets(
                [
                    "Add reviewer invitations with role-specific permissions: read-only, commenter, editor, publisher, cover designer, admin.",
                    "Enable comment threads on chapters, metadata fields, covers, and exports.",
                    "Provide redline exports showing AI edits versus author edits.",
                    "Create beta-reader packets with selected chapters, feedback questions, and deadline tracking.",
                    "Add editorial checklists for memoir, devotional, business, fiction, workbook, and children's formats.",
                    "Support compare views: draft vs edited, current vs previous, original source vs generated chapter.",
                    "Track decisions: accepted, rejected, deferred, needs human review.",
                ]
            ),
            p("Collaboration is a likely monetization lever. Teams, publishers, coaches, editors, and agencies will pay for permissions, workflow, and accountability."),
        ],
    )

    story += section(
        "6. Publishing and Commerce",
        [
            data_table(
                [
                    ["Idea", "Potential value"],
                    ["Retail metadata pack", "Export KDP/Ingram/Google Play/Apple Books metadata as platform-specific checklists."],
                    ["Launch kit", "Generate emails, ads, landing page copy, press release, author Q&A, and social calendar."],
                    ["ISBN and imprint manager", "Track ISBNs, imprint names, copyright owners, editions, and publication dates."],
                    ["Royalty calculator", "Estimate print cost, list price, royalties, and break-even campaigns."],
                    ["Proof checklist", "Page count, trim size, bleed, margins, cover wrap dimensions, metadata, and placeholder scan."],
                    ["Marketplace services", "Offer human editing, cover design, layout, launch management, translation review, and publishing assistance."],
                ],
                [4.0 * cm, 11.9 * cm],
            ),
        ],
    )

    story += section(
        "7. Cover, Media, and Derivative Products",
        [
            bullets(
                [
                    "Advanced cover studio with spine text, barcode placement, trim presets, CMYK/preflight checks, and retailer-specific warnings.",
                    "Series branding system that keeps fonts, colors, author mark, and layout consistent across multiple books.",
                    "Audiobook pipeline: narration script cleanup, voice selection, chapter audio generation/recording, proofing, and ACX-style packaging.",
                    "Video trailer generator: book hook, cover animation, captions, voiceover, and platform-specific aspect ratios.",
                    "Course generator: slides, workbook, facilitator guide, quizzes, assignments, certificate, and learning path from the manuscript.",
                    "Children's edition generator: age band, illustration brief, simpler language, parent/teacher notes, and safety review.",
                    "Workbook and devotional editions: reflection questions, journaling prompts, exercises, prayers, weekly plans, and group discussion guides.",
                ]
            ),
        ],
    )

    story += section(
        "8. Operations and Platform Enhancements",
        [
            data_table(
                [
                    ["Area", "Ideas"],
                    ["Queue reliability", "Dead-letter queue, job deduplication, retry policies by job type, stuck-job detector, and replay tooling."],
                    ["Worker scaling", "Multiple worker profiles: planner, writer, editor, export, media, and operator/deploy workers."],
                    ["Admin observability", "Per-tenant usage, token spend, average job duration, failure rate, export volume, and storage growth."],
                    ["Prompt governance", "Template versions, approval workflow, rollback, test runs, and output scorecards."],
                    ["Billing hooks", "Track credits by job type, export type, storage, translation, media generation, and human services."],
                    ["Compliance", "Audit logs, content policy review, source licensing, PII detection, and tenant data export/delete tools."],
                ],
                [3.5 * cm, 12.4 * cm],
            ),
        ],
    )

    story += section(
        "9. MADProspects Universe Integrations",
        [
            p("The best ecosystem strategy is to make MADAuthor the long-form publishing layer for other MADProspects applications. Each app can feed structured data, conversations, and assets into MADAuthor; MADAuthor returns books, playbooks, reports, campaigns, courses, and authority assets."),
            data_table(
                [
                    ["Application area", "Integration ideas"],
                    ["MADRecruiting", "Turn job specs, onboarding material, interview insights, candidate training, employer branding, and leadership content into onboarding handbooks, assessment workbooks, recruiter playbooks, and niche authority books."],
                    ["MADProspects CRM / lead engine", "Generate personalized lead magnets, proposal books, account-based mini-books, nurture email sequences, case study compilations, and prospect education packs from CRM notes and campaign data."],
                    ["MADCreate / creative studio", "Use MADAuthor manuscripts to generate landing pages, ad sets, cover variants, social packs, launch visuals, and branded author media."],
                    ["MADLearn", "Convert books into courses, lessons, quizzes, slides, assignments, certificates, facilitator guides, and learner progress paths."],
                    ["MADCloud", "Centralize users, tenants, billing, assets, notifications, deployment tasks, and AI worker settings across all MAD apps."],
                    ["MADAI / prompt platform", "Share prompt templates, model routing, evaluation harnesses, safety rules, usage metrics, and reusable agents across the universe."],
                    ["MADPulse analytics", "Show author progress, campaign impact, reader engagement, export usage, worker health, sales funnel influence, and content ROI."],
                    ["Church/ministry apps", "Transform sermon series, devotionals, discipleship tracks, Bible studies, small-group curricula, testimonies, and ministry training into books and study guides."],
                    ["Client services / agency ops", "Offer done-for-you publishing packages, editorial workflows, launch campaigns, and content repurposing for clients."],
                    ["Knowledge base / support", "Turn internal SOPs, FAQs, support chats, and implementation notes into manuals, training books, and customer success guides."],
                ],
                [4.0 * cm, 11.9 * cm],
            ),
        ],
    )

    story += section(
        "10. Integration Patterns",
        [
            bullets(
                [
                    "<b>Shared identity:</b> one MAD account, company, role, and billing model across apps.",
                    "<b>Shared asset library:</b> files, images, source documents, covers, transcripts, and exports available across authorized products.",
                    "<b>Event bus:</b> when a CRM campaign closes, a recruiting project launches, or a course completes, MADAuthor can suggest a book/playbook output.",
                    "<b>Reusable content graph:</b> prospects, companies, jobs, sermons, campaigns, courses, and notes become typed source nodes for generated products.",
                    "<b>Universal task queue:</b> MAD Cloud tasks can represent build, deploy, content, editorial, publishing, and support work across products.",
                    "<b>API contracts:</b> standard 'CreateAuthoringProject' and 'ExportDerivativeProduct' endpoints let other apps launch MADAuthor workflows safely.",
                ]
            ),
            p("Avoid one-off integrations. Create a small set of durable contracts: source bundle in, project created, job queued, progress event, export ready, asset returned."),
        ],
    )

    story += section(
        "11. Prioritized Roadmap",
        [
            data_table(
                [
                    ["Horizon", "Recommended work"],
                    ["Next 30 days", "Polish login/CORS/deploy reliability, seed superadmin, stabilize local dev, finish upload-to-request stitching, improve export readiness checks, and add a clear next-action banner."],
                    ["Next 60 days", "Inline chapter editor, version history, first chapter sample approval, source library, metadata completeness score, and better admin queue observability."],
                    ["Next 90 days", "Course/workbook/devo derivative products, collaboration/reviewer permissions, advanced cover wrap studio, launch kit generation, and MADProspects CRM/recruiting source bundle integration."],
                    ["Platform bets", "Shared MAD identity, asset graph, prompt governance, billing/credits, universal analytics, and multi-worker orchestration across the MADProspects Universe."],
                ],
                [3.0 * cm, 12.9 * cm],
            ),
        ],
    )

    story += section(
        "12. Monetization Ideas",
        [
            bullets(
                [
                    "Tier by active projects, export formats, AI credits, storage, translation volume, media generation, and collaboration seats.",
                    "Sell publishing packages: manuscript polish, cover design, KDP setup, launch kit, and translation review.",
                    "Offer agency/team tiers for coaches, churches, publishers, recruiters, and consultants managing many authors or clients.",
                    "Create add-ons for audiobook, course conversion, workbook/devotional editions, and professional review.",
                    "Bundle MADAuthor with MADRecruiting or CRM campaigns as authority-building packages.",
                ]
            ),
        ],
    )

    story += section(
        "13. Risks and Guardrails",
        [
            data_table(
                [
                    ["Risk", "Guardrail"],
                    ["Generic AI output", "Use better intake, voice profiles, sample approval, source grounding, and human review checkpoints."],
                    ["Copyright/source misuse", "Track source licenses, attribution, quotes, and source-derived passages."],
                    ["False claims", "Add claim detection, citation requirements, and review labels for sensitive domains."],
                    ["Tenant data leakage", "Keep company isolation, scoped storage, authorization checks, and audit logs central."],
                    ["Publishing mistakes", "Use proof checklists, placeholder scans, metadata validation, cover preflight, and export QA."],
                    ["Runaway job costs", "Add credit accounting, job estimates, quotas, retry limits, and admin spend dashboards."],
                ],
                [4.0 * cm, 11.9 * cm],
            ),
            p("The product should be ambitious, but publishing is trust-heavy. The application needs visible quality gates so users know what is AI-generated, what is source-backed, what needs review, and what is ready to publish."),
        ],
    )

    story += section(
        "14. North Star",
        [
            p("The north star is a MADProspects content engine where every valuable interaction can become durable IP: a lead magnet, book, course, workbook, proposal, devotional, training guide, playbook, or campaign. MADAuthor is the place where that IP becomes structured, polished, exportable, and commercially useful."),
            p("The practical path is to stabilize the current book workflow, add author trust features, then connect MADAuthor to the rest of the MAD universe through shared identity, assets, source bundles, tasks, analytics, and billing."),
        ],
    )

    story += section(
        "15. MADRecruiting Integration Blueprint",
        [
            p("MADRecruiting can become one of the strongest feeder systems for MADAuthor because recruiting produces structured knowledge: job descriptions, interview notes, candidate profiles, employer value propositions, onboarding content, compliance material, and training gaps."),
            data_table(
                [
                    ["Source in MADRecruiting", "MADAuthor output"],
                    ["Job spec and success profile", "Role handbook, onboarding guide, interview preparation booklet, manager briefing, and candidate expectation guide."],
                    ["Candidate interview notes", "Candidate development plan, coaching workbook, strengths profile, and personalized onboarding roadmap."],
                    ["Employer brand material", "Company culture book, recruitment lead magnet, hiring manager playbook, and careers-page content pack."],
                    ["Training gaps", "Micro-course, workbook, SOP manual, and practical assessment guide."],
                    ["Recruiter process notes", "Recruiting methodology book, internal training manual, and client-facing recruitment playbook."],
                ],
                [4.5 * cm, 11.4 * cm],
            ),
            p("A practical first integration is a 'Create onboarding guide' button inside MADRecruiting that sends a source bundle to MADAuthor: job spec, company overview, hiring manager notes, competencies, and candidate persona."),
        ],
    )

    story += section(
        "16. CRM and Lead Engine Blueprint",
        [
            p("The CRM side of MADProspects can use MADAuthor to turn campaign knowledge into authority assets. This is especially valuable for account-based marketing, consultants, coaches, B2B service providers, and agencies."),
            bullets(
                [
                    "Generate a prospect-specific mini-book from CRM notes, pain points, industry, objections, and proposed solution.",
                    "Create lead magnets from frequently asked questions, sales call transcripts, webinars, and case studies.",
                    "Build personalized proposal books that combine diagnosis, education, implementation plan, proof, and pricing context.",
                    "Turn closed-won projects into anonymized case study collections.",
                    "Feed book launch assets back into CRM as nurture sequences, retargeting copy, and follow-up tasks.",
                    "Score content ROI by linking downloads, email engagement, meetings booked, deals influenced, and revenue.",
                ]
            ),
            p("MADAuthor should return structured outputs to the CRM: asset URL, title, summary, campaign copy, landing page copy, and recommended follow-up sequence."),
        ],
    )

    story += section(
        "17. MADLearn Integration Blueprint",
        [
            data_table(
                [
                    ["MADAuthor manuscript element", "MADLearn derivative"],
                    ["Chapter", "Lesson with objectives, summary, transcript, quiz, and assignment."],
                    ["Action steps", "Practice exercise, worksheet, or implementation task."],
                    ["Reflection questions", "Discussion prompt, journal assignment, or coaching question."],
                    ["Framework", "Module structure, visual model, facilitator notes, and assessment rubric."],
                    ["Case studies", "Scenario exercises, branching discussions, and learner submissions."],
                    ["Glossary", "Flash cards, quick reference guide, and knowledge checks."],
                ],
                [4.5 * cm, 11.4 * cm],
            ),
            p("The long-term opportunity is a single 'Book to course' workflow where MADAuthor exports a course package directly into MADLearn with lessons, quizzes, slides, facilitator notes, and downloadable workbook sections."),
        ],
    )

    story += section(
        "18. Ministry and Community Blueprint",
        [
            p("MADAuthor is naturally suited to sermon, devotional, discipleship, and teaching content. Many ministries already produce weekly content but do not package it into durable resources."),
            bullets(
                [
                    "Sermon series to devotional book with daily readings, prayers, and reflection questions.",
                    "Bible study guide with leader notes, participant questions, weekly assignments, and scripture references.",
                    "Testimony collection with privacy review, consent tracking, and thematic organization.",
                    "New believer handbook, volunteer training manual, leadership curriculum, and small group launch pack.",
                    "Youth or children's adaptation with age-level language, parent notes, and illustration prompts.",
                    "Multilingual devotional export with translation review workflow.",
                ]
            ),
            p("Guardrails are important in this segment: scripture translation preference, doctrinal boundaries, attribution, sensitive testimony handling, and human pastoral review should be first-class workflow settings."),
        ],
    )

    story += section(
        "19. Shared Platform Services",
        [
            data_table(
                [
                    ["Shared service", "What it enables"],
                    ["Identity and roles", "One login, consistent tenant membership, unified admin roles, and cross-app permissions."],
                    ["Asset library", "A shared source of documents, images, audio, video, transcripts, exports, and generated media."],
                    ["AI orchestration", "Common model routing, prompt templates, evaluations, safety checks, retries, and usage tracking."],
                    ["Billing and credits", "One credit ledger for writing, exports, translations, media generation, storage, and human services."],
                    ["Notifications", "A single notification center for job progress, reviews, tasks, approvals, exports, and deploys."],
                    ["Analytics", "Unified dashboards for usage, content ROI, funnel impact, worker health, and customer outcomes."],
                    ["Task system", "One operator queue for product work, customer work, publishing tasks, and deploy control."],
                ],
                [4.0 * cm, 11.9 * cm],
            ),
        ],
    )

    story += section(
        "20. Suggested Data Contracts",
        [
            p("Integrations should pass structured source bundles into MADAuthor. A source bundle is more durable than a pasted prompt because it can preserve provenance, permissions, and future re-use."),
            data_table(
                [
                    ["Contract", "Fields"],
                    ["CreateAuthoringProject", "TenantId, sourceApp, title, audience, goal, projectType, language, sourceBundleId, requestedOutputs, dueDate, ownerUserId."],
                    ["SourceBundle", "Documents, transcripts, CRM notes, job specs, tags, source permissions, attribution, extraction status, and sensitivity labels."],
                    ["QueueDerivativeOutput", "ProjectId, outputType, format, styleProfile, platformTarget, reviewRequired, and callback destination."],
                    ["ExportReadyEvent", "ProjectId, outputType, assetUrl, summary, metadata, status, createdDate, and originating source IDs."],
                    ["ReviewRequest", "ProjectId, reviewerRole, targetSection, dueDate, requiredChecklist, and notification channel."],
                ],
                [4.2 * cm, 11.7 * cm],
            ),
            p("These contracts make the universe composable. Any app can create a source bundle; MADAuthor can transform it; the originating app can consume the finished asset."),
        ],
    )

    story += section(
        "21. Metrics and KPIs",
        [
            data_table(
                [
                    ["Metric", "Why it matters"],
                    ["Project completion rate", "Shows whether users move from idea to export or abandon halfway."],
                    ["Time to first outline", "Measures how quickly the platform gives visible value."],
                    ["Time to approved manuscript", "Measures true production throughput."],
                    ["Average retries per job type", "Highlights unstable prompts, providers, or infrastructure."],
                    ["Export format mix", "Shows whether users care most about PDF, DOCX, EPUB, print, or derivative products."],
                    ["Placeholder defects caught", "Measures value of metadata/front-matter QA."],
                    ["Source-grounding coverage", "Shows how much of a manuscript is connected to supplied material."],
                    ["Content ROI", "Links generated assets to leads, meetings, course enrollments, applications, sales, or donations."],
                    ["Human service attach rate", "Measures opportunities for editing, cover, publishing, and launch packages."],
                ],
                [4.0 * cm, 11.9 * cm],
            ),
        ],
    )

    story += section(
        "22. Backlog of Concrete Features",
        [
            data_table(
                [
                    ["Feature", "Notes"],
                    ["Autosave rich chapter editor", "Markdown editor with preview, comments, version history, and export-aware formatting."],
                    ["Chapter regenerate with instruction", "Regenerate a chapter with a targeted note while preserving outline and project voice."],
                    ["Metadata completeness meter", "Score KDP description, keywords, subtitle, bio, copyright, blanks, and categories."],
                    ["Cover A/B board", "Compare cover variants at thumbnail size and collect reviewer votes."],
                    ["Export preflight", "Warn about missing metadata, blank chapters, placeholder fields, weak cover, and unsupported characters."],
                    ["Reviewer portal", "Limited-access review links for beta readers, editors, pastors, coaches, and clients."],
                    ["Source quote browser", "Search uploaded sources and insert verified quotes into chapters."],
                    ["Prompt template lab", "Run templates against sample projects and compare outputs before promoting them."],
                    ["Job cost estimator", "Estimate credits/time before launching a large project or translation."],
                    ["Tenant style guide", "Reusable brand voice, spelling preferences, banned phrases, scripture translation, and formatting rules."],
                    ["Series manager", "Group books under a series with consistent metadata, covers, fonts, and launch assets."],
                    ["Launch calendar", "Tasks and dates for cover reveal, beta reads, preorders, email sequence, ads, and publication."],
                    ["Human services marketplace", "Request editing, cover design, layout, publishing setup, or marketing help from vetted providers."],
                ],
                [4.3 * cm, 11.6 * cm],
            ),
        ],
    )

    MadDocTemplate(OUT_IDEAS, "MADAuthor Ideas").build(story)


STYLES = make_styles()


if __name__ == "__main__":
    build_manual()
    build_ideas()
    print(OUT_MANUAL)
    print(OUT_IDEAS)
