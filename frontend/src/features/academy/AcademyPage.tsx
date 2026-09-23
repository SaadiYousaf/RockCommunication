import { Link, useNavigate } from "react-router-dom";
import {
  Badge, Button, Card, CardBody, EmptyState, Icon, PageHeader, QueryState, Skeleton, Stat,
} from "../../shared/ui";
import type { BadgeTone, IconName } from "../../shared/ui";
import { useAcademyCertificatesQuery, useAcademyCoursesQuery, useAcademyProgressQuery } from "../../shared/api/baseApi";
import type { AcademyCourse } from "../../shared/api/types";
import { ACADEMY_MSG as M } from "./messages";

/**
 * The Academy home: what you've learned, what to do next, and the courses chosen for your role.
 *
 * The single most important element is the resume card at the top. Someone coming back to learning
 * shouldn't have to remember where they stopped — the server already knows the next unread lesson,
 * so one click gets them back into it.
 */

const LEVEL_LABEL: Record<AcademyCourse["level"], string> = {
  Beginner: M.levelBeginner,
  Intermediate: M.levelIntermediate,
  Advanced: M.levelAdvanced,
  Admin: M.levelAdmin,
};

const LEVEL_TONE: Record<AcademyCourse["level"], BadgeTone> = {
  Beginner: "success",
  Intermediate: "info",
  Advanced: "accent",
  Admin: "brand",
};

/** A thin progress rail. Reads at a glance without needing the numbers beside it. */
function ProgressBar({ done, total }: { done: number; total: number }) {
  const pct = total === 0 ? 0 : Math.round((done / total) * 100);
  return (
    <div
      className="h-1.5 w-full overflow-hidden rounded-full bg-ink-100"
      role="progressbar"
      aria-valuenow={pct}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-label={M.progressOf(done, total)}
    >
      <div
        className="h-full rounded-full bg-gradient-to-r from-brand-500 to-brand-600 transition-[width] duration-500 ease-out-quint"
        style={{ width: `${pct}%` }}
      />
    </div>
  );
}

function CourseCard({ course }: { course: AcademyCourse }) {
  const started = course.completedLessons > 0;
  const cta = course.isComplete ? M.reviewCourse : started ? M.continueCourse : M.startCourse;

  return (
    <Card
      interactive
      accent={course.isComplete ? "success" : started ? "brand" : null}
      className="h-full"
    >
      <Link to={`/academy/${course.key}`} className="block h-full focus:outline-none">
        <CardBody className="flex h-full flex-col gap-4">
          <div className="flex items-start gap-3">
            <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-brand-50 to-brand-100 text-brand-600 ring-1 ring-brand-200/60">
              <Icon name={course.icon as IconName} className="h-5 w-5" />
            </span>
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-2">
                <h3 className="truncate font-semibold text-ink-900">{course.title}</h3>
                {course.isComplete && <Badge tone="success">{M.courseComplete}</Badge>}
              </div>
              <p className="mt-1 text-sm leading-6 text-ink-600 line-clamp-2">{course.summary}</p>
            </div>
          </div>

          <div className="mt-auto flex flex-col gap-2.5">
            <ProgressBar done={course.completedLessons} total={course.lessonCount} />
            <div className="flex items-center justify-between gap-2 text-xs text-ink-500">
              <span className="inline-flex items-center gap-2">
                <Badge tone={LEVEL_TONE[course.level]}>{LEVEL_LABEL[course.level]}</Badge>
                <span>{M.lessonCount(course.lessonCount)}</span>
                <span aria-hidden>·</span>
                <span>{M.minutes(course.estimatedMinutes)}</span>
              </span>
              <span className="inline-flex items-center gap-1 font-medium text-brand-600">
                {cta}
                <Icon name="chevronRight" className="h-3.5 w-3.5" />
              </span>
            </div>
          </div>
        </CardBody>
      </Link>
    </Card>
  );
}

export function AcademyPage() {
  const navigate = useNavigate();
  const courses = useAcademyCoursesQuery();
  const progress = useAcademyProgressQuery();
  const certificates = useAcademyCertificatesQuery();

  const next = progress.data;
  const resumeTo = next?.nextLessonCourseKey && next?.nextLessonKey
    ? `/academy/${next.nextLessonCourseKey}/${next.nextLessonKey}`
    : null;
  const anyStarted = (next?.lessonsCompleted ?? 0) > 0;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={M.title}
        description={M.subtitle}
        actions={
          (certificates.data?.length ?? 0) > 0 ? (
            <Button variant="secondary" onClick={() => navigate("/academy/certificates")}>
              <Icon name="star" className="h-4 w-4" />
              {M.certificates}
            </Button>
          ) : undefined
        }
      />

      {/* Resume — the one thing a returning learner is here to do. */}
      {resumeTo && (
        <Card elevated accent="brand">
          <CardBody className="flex flex-wrap items-center justify-between gap-4">
            <div className="min-w-0">
              <p className="text-xs font-medium uppercase tracking-wide text-ink-500">
                {anyStarted ? M.keepGoing : M.startLearning}
              </p>
              <p className="mt-1 truncate text-lg font-semibold text-ink-900">{next?.nextLessonTitle}</p>
            </div>
            <Button onClick={() => navigate(resumeTo)}>
              {anyStarted ? M.resumeCta(next!.nextLessonTitle!) : M.firstLessonCta}
              <Icon name="arrowRight" className="h-4 w-4" />
            </Button>
          </CardBody>
        </Card>
      )}

      {/* Progress at a glance. */}
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <Stat
          label={M.statCourses}
          value={`${next?.coursesCompleted ?? 0} / ${next?.coursesAvailable ?? 0}`}
          icon={<Icon name="book" className="h-4 w-4" />}
        />
        <Stat
          label={M.statLessons}
          value={`${next?.lessonsCompleted ?? 0} / ${next?.lessonsAvailable ?? 0}`}
          icon={<Icon name="check" className="h-4 w-4" />}
          tone="success"
        />
        <Stat
          label={M.statMinutes}
          value={M.minutes(next?.minutesSpent ?? 0)}
          icon={<Icon name="clock" className="h-4 w-4" />}
          tone="accent"
        />
        <Stat
          label={M.statCertificates}
          value={next?.certificatesEarned ?? 0}
          icon={<Icon name="star" className="h-4 w-4" />}
          tone="warning"
          to="/academy/certificates"
        />
      </div>

      {/* The path. */}
      <section className="flex flex-col gap-3">
        <div>
          <h2 className="text-base font-semibold text-ink-900">{M.yourPath}</h2>
          <p className="text-sm text-ink-600">{M.pathHint}</p>
        </div>

        <QueryState
          isLoading={courses.isLoading}
          isError={courses.isError}
          error={courses.error}
          isEmpty={(courses.data?.length ?? 0) === 0}
          resource={M.resourceName}
          onRetry={courses.refetch}
          loading={
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-44 rounded-2xl" />)}
            </div>
          }
          empty={
            <Card><EmptyState
              icon={<Icon name="book" className="h-6 w-6" />}
              title={M.noCoursesTitle}
              description={M.noCoursesDesc}
            /></Card>
          }
        >
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {courses.data?.map((c) => <CourseCard key={c.id} course={c} />)}
          </div>
        </QueryState>
      </section>

      {/* The pre-existing one-page overview, linked rather than duplicated into a lesson. */}
      <Card interactive accent="accent">
        <Link to="/guide" className="block focus:outline-none">
          <CardBody className="flex flex-wrap items-center justify-between gap-4">
            <div className="flex items-start gap-3">
              <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-accent-50 to-accent-100 text-accent-600 ring-1 ring-accent-200/60">
                <Icon name="doc" className="h-5 w-5" />
              </span>
              <div>
                <h3 className="font-semibold text-ink-900">{M.overviewTitle}</h3>
                <p className="mt-1 max-w-2xl text-sm leading-6 text-ink-600">{M.overviewDesc}</p>
              </div>
            </div>
            <span className="inline-flex items-center gap-1 text-sm font-medium text-accent-600">
              {M.overviewCta}
              <Icon name="chevronRight" className="h-4 w-4" />
            </span>
          </CardBody>
        </Link>
      </Card>
    </div>
  );
}
