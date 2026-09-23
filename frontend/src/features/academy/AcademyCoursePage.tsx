import { Link, useNavigate, useParams } from "react-router-dom";
import {
  Badge, Button, Card, CardBody, EmptyState, Icon, PageHeader, QueryState, Skeleton,
} from "../../shared/ui";
import { useAcademyCourseQuery, useAcademyCoursesQuery } from "../../shared/api/baseApi";
import type { AcademyLessonSummary } from "../../shared/api/types";
import { ACADEMY_MSG as M } from "./messages";

/**
 * One course: its lessons in order, each showing whether it's been read.
 *
 * Lessons are never locked behind one another. Someone who already knows how fronting works
 * shouldn't have to click through three lessons to reach the one they came for — the order is a
 * recommendation, not a gate.
 */

function LessonRow({ lesson, courseKey, index }: { lesson: AcademyLessonSummary; courseKey: string; index: number }) {
  const done = lesson.status === "Completed";

  return (
    <Link
      to={`/academy/${courseKey}/${lesson.key}`}
      className="group flex items-start gap-4 rounded-xl px-4 py-3.5 transition-colors hover:bg-ink-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400"
    >
      {/* Step marker: the curriculum really is a sequence, so the number carries meaning. */}
      <span
        aria-hidden
        className={
          done
            ? "grid h-7 w-7 shrink-0 place-items-center rounded-full bg-emerald-100 text-emerald-700 ring-1 ring-emerald-200"
            : "grid h-7 w-7 shrink-0 place-items-center rounded-full bg-ink-100 text-xs font-semibold text-ink-600 ring-1 ring-ink-200"
        }
      >
        {done ? <Icon name="check" className="h-4 w-4" /> : index + 1}
      </span>

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-medium text-ink-900">{lesson.title}</span>
          {done && <Badge tone="success">{M.alreadyRead}</Badge>}
          {lesson.hasQuiz && <Badge tone="neutral">{M.quizTitle}</Badge>}
        </div>
        <p className="mt-0.5 text-sm leading-6 text-ink-600">{lesson.summary}</p>
      </div>

      <span className="mt-0.5 inline-flex shrink-0 items-center gap-1 text-xs text-ink-500">
        <Icon name="clock" className="h-3.5 w-3.5" />
        {M.minutes(lesson.estimatedMinutes)}
        <Icon name="chevronRight" className="h-4 w-4 text-ink-400 transition-transform group-hover:translate-x-0.5" />
      </span>
    </Link>
  );
}

export function AcademyCoursePage() {
  const { courseKey = "" } = useParams();
  const navigate = useNavigate();

  const lessons = useAcademyCourseQuery(courseKey, { skip: !courseKey });
  // The course's own title/summary comes from the path listing, which is already cached by the
  // Academy home — so this costs nothing in the normal navigation.
  const courses = useAcademyCoursesQuery();
  const course = courses.data?.find((c) => c.key === courseKey);

  const firstUnread = lessons.data?.find((l) => l.status !== "Completed") ?? lessons.data?.[0];

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={course?.title ?? M.title}
        description={course?.summary}
        breadcrumbs={[{ label: M.title, to: "/academy" }, { label: course?.title ?? "" }]}
        badge={course?.isComplete ? <Badge tone="success">{M.courseComplete}</Badge> : undefined}
        actions={
          firstUnread ? (
            <Button onClick={() => navigate(`/academy/${courseKey}/${firstUnread.key}`)}>
              {course?.isComplete ? M.reviewCourse : course && course.completedLessons > 0 ? M.continueCourse : M.startCourse}
              <Icon name="arrowRight" className="h-4 w-4" />
            </Button>
          ) : undefined
        }
      />

      {course?.isComplete && course.certificateSerial && (
        <Card accent="success">
          <CardBody className="flex flex-wrap items-center justify-between gap-4">
            <div className="flex items-center gap-3">
              <span className="grid h-10 w-10 place-items-center rounded-xl bg-gradient-to-br from-emerald-50 to-emerald-100 text-emerald-700 ring-1 ring-emerald-200/60">
                <Icon name="star" className="h-5 w-5" />
              </span>
              <div>
                <p className="font-semibold text-ink-900">{M.courseFinishedTitle}</p>
                <p className="text-sm text-ink-600">
                  {M.certificateSerial} {course.certificateSerial}
                </p>
              </div>
            </div>
            <Button variant="secondary" onClick={() => navigate("/academy/certificates")}>
              {M.viewCertificate}
            </Button>
          </CardBody>
        </Card>
      )}

      <Card>
        <QueryState
          isLoading={lessons.isLoading}
          isError={lessons.isError}
          error={lessons.error}
          isEmpty={(lessons.data?.length ?? 0) === 0}
          resource={M.resourceName}
          onRetry={lessons.refetch}
          loading={
            <CardBody className="flex flex-col gap-3">
              {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-16 rounded-xl" />)}
            </CardBody>
          }
          empty={
            <EmptyState
              icon={<Icon name="book" className="h-6 w-6" />}
              title={M.noCoursesTitle}
              description={M.noCoursesDesc}
            />
          }
        >
          <CardBody className="flex flex-col gap-1 p-2 sm:p-3">
            {lessons.data?.map((l, i) => (
              <LessonRow key={l.id} lesson={l} courseKey={courseKey} index={i} />
            ))}
          </CardBody>
        </QueryState>
      </Card>

      <Link to="/academy" className="inline-flex items-center gap-1 text-sm text-ink-600 hover:text-ink-900">
        <Icon name="chevronLeft" className="h-4 w-4" />
        {M.title}
      </Link>
    </div>
  );
}
