import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  Badge, Button, Card, CardBody, Icon, PageHeader, QueryState, Skeleton, useToast,
} from "../../shared/ui";
import {
  useAcademyLessonQuery, useCompleteAcademyLessonMutation, useSubmitAcademyQuizMutation,
} from "../../shared/api/baseApi";
import type { QuizResult } from "../../shared/api/types";
import { LessonBody } from "./LessonBody";
import { ACADEMY_MSG as M } from "./messages";

/**
 * A lesson: the reading, a link to the real screen it describes, and a quiz.
 *
 * Time spent is measured here rather than guessed on the server — the page knows when it was opened.
 * It is clamped so a tab left open overnight doesn't claim eight hours of study.
 */

/** Hard cap on a single sitting, in seconds. Beyond this the tab was open, not being read. */
const MAX_SITTING_SECONDS = 60 * 60;

function Quiz({ lessonId, courseTitle, questions }: {
  lessonId: string;
  courseTitle: string;
  questions: { id: string; prompt: string; options: string[] }[];
}) {
  const [answers, setAnswers] = useState<Record<string, number>>({});
  const [result, setResult] = useState<QuizResult | null>(null);
  const [submit, { isLoading }] = useSubmitAcademyQuizMutation();
  const toast = useToast();

  const allAnswered = questions.every((q) => answers[q.id] !== undefined);
  const byQuestion = useMemo(
    () => new Map(result?.items.map((i) => [i.questionId, i])),
    [result],
  );

  const onSubmit = async () => {
    try {
      const ordered = questions.map((q) => answers[q.id] ?? -1);
      const res = await submit({ lessonId, answers: ordered }).unwrap();
      setResult(res);
      if (res.courseCompleted && res.certificateSerial) {
        toast.success(M.certificateEarned(courseTitle), M.certificateSerial + " " + res.certificateSerial);
      }
    } catch {
      toast.error(M.quizFailed);
    }
  };

  const retake = () => { setResult(null); setAnswers({}); };

  return (
    <Card accent="brand">
      <CardBody className="flex flex-col gap-5">
        <div>
          <h2 className="text-base font-semibold text-ink-900">{M.quizTitle}</h2>
          <p className="text-sm text-ink-600">{M.quizHint}</p>
        </div>

        {questions.map((q, qi) => {
          const graded = byQuestion.get(q.id);
          return (
            <fieldset key={q.id} className="flex flex-col gap-2">
              <legend className="mb-1 text-sm font-medium text-ink-900">
                {qi + 1}. {q.prompt}
              </legend>

              {q.options.map((opt, oi) => {
                const picked = answers[q.id] === oi;
                // After grading, the right answer is always shown — that is the teaching moment.
                const isKey = graded ? graded.correctIndex === oi : false;
                const isWrongPick = graded ? picked && !graded.correct : false;

                return (
                  <label
                    key={oi}
                    className={[
                      "flex cursor-pointer items-start gap-3 rounded-xl border px-3.5 py-2.5 text-sm transition-colors",
                      isKey
                        ? "border-emerald-300 bg-emerald-50 text-emerald-900"
                        : isWrongPick
                          ? "border-rose-300 bg-rose-50 text-rose-900"
                          : picked
                            ? "border-brand-400 bg-brand-50 text-ink-900"
                            : "border-ink-200 hover:border-ink-300 hover:bg-ink-50 text-ink-700",
                      result ? "cursor-default" : "",
                    ].join(" ")}
                  >
                    <input
                      type="radio"
                      name={q.id}
                      className="mt-0.5 accent-brand-600"
                      checked={picked}
                      disabled={!!result}
                      onChange={() => setAnswers((a) => ({ ...a, [q.id]: oi }))}
                    />
                    <span className="flex-1">{opt}</span>
                    {isKey && <Icon name="check" className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" />}
                    {isWrongPick && <Icon name="x" className="mt-0.5 h-4 w-4 shrink-0 text-rose-600" />}
                  </label>
                );
              })}

              {graded && (
                <p className="mt-1 flex gap-2 rounded-lg bg-ink-50 px-3 py-2 text-sm leading-6 text-ink-700">
                  <Icon
                    name={graded.correct ? "success" : "info"}
                    className={graded.correct ? "mt-1 h-4 w-4 shrink-0 text-emerald-600" : "mt-1 h-4 w-4 shrink-0 text-amber-600"}
                  />
                  <span>
                    <strong className="font-semibold">
                      {graded.correct ? M.answerCorrect : M.answerWrong}.
                    </strong>{" "}
                    {graded.explanation}
                  </span>
                </p>
              )}
            </fieldset>
          );
        })}

        <div className="flex flex-wrap items-center gap-3">
          {result ? (
            <>
              <Badge tone={result.passed ? "success" : "warning"}>
                {M.quizScore(result.score, result.total)}
              </Badge>
              <span className="text-sm text-ink-700">
                {result.passed ? M.quizPassed : M.quizNotPassed}
              </span>
              <Button variant="secondary" onClick={retake} className="ml-auto">
                {M.retakeQuiz}
              </Button>
            </>
          ) : (
            <>
              <Button onClick={onSubmit} disabled={!allAnswered || isLoading} loading={isLoading}>
                {M.submitQuiz}
              </Button>
              {!allAnswered && <span className="text-sm text-ink-500">{M.quizIncomplete}</span>}
            </>
          )}
        </div>
      </CardBody>
    </Card>
  );
}

export function AcademyLessonPage() {
  const { courseKey = "", lessonKey = "" } = useParams();
  const navigate = useNavigate();
  const toast = useToast();

  const lesson = useAcademyLessonQuery({ courseKey, lessonKey }, { skip: !courseKey || !lessonKey });
  const [complete, { isLoading: completing }] = useCompleteAcademyLessonMutation();

  // Restarted per lesson, not per mount, so navigating on within the course times each one.
  const openedAt = useRef(Date.now());
  useEffect(() => { openedAt.current = Date.now(); }, [lessonKey]);

  const data = lesson.data;
  const done = data?.status === "Completed";

  const markRead = async () => {
    if (!data) return;
    const seconds = Math.min(Math.round((Date.now() - openedAt.current) / 1000), MAX_SITTING_SECONDS);
    try {
      const res = await complete({ lessonId: data.id, secondsSpent: seconds }).unwrap();
      if (res.courseCompleted && res.certificateSerial) {
        toast.success(M.certificateEarned(data.courseTitle), M.certificateSerial + " " + res.certificateSerial);
      } else {
        toast.success(M.markedRead, data.title);
      }
      if (data.nextLessonKey) navigate(`/academy/${courseKey}/${data.nextLessonKey}`);
      else navigate(`/academy/${courseKey}`);
    } catch {
      toast.error(M.completeFailed);
    }
  };

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={data?.title ?? M.lessonResourceName}
        description={data?.summary}
        breadcrumbs={[
          { label: M.title, to: "/academy" },
          { label: data?.courseTitle ?? "", to: `/academy/${courseKey}` },
          { label: data?.title ?? "" },
        ]}
        badge={done ? <Badge tone="success">{M.alreadyRead}</Badge> : undefined}
        actions={
          data?.route ? (
            <Button variant="secondary" onClick={() => navigate(data.route!)} title={M.openTheScreenHint}>
              <Icon name="externalLink" className="h-4 w-4" />
              {M.openTheScreen}
            </Button>
          ) : undefined
        }
      />

      <QueryState
        isLoading={lesson.isLoading}
        isError={lesson.isError}
        error={lesson.error}
        isEmpty={false}
        resource={M.lessonResourceName}
        onRetry={lesson.refetch}
        loading={
          <Card><CardBody className="flex flex-col gap-3">
            {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-5" />)}
          </CardBody></Card>
        }
        empty={null}
      >
        {data && (
          <>
            <Card>
              <CardBody className="max-w-3xl">
                <LessonBody markdown={data.bodyMarkdown} />
              </CardBody>
            </Card>

            {data.questions.length > 0 && (
              <Quiz lessonId={data.id} courseTitle={data.courseTitle} questions={data.questions} />
            )}

            {/* Move on. The primary action is always the next step, whether or not it was read before. */}
            <div className="flex flex-wrap items-center justify-between gap-3">
              {data.previousLessonKey ? (
                <Button
                  variant="ghost"
                  onClick={() => navigate(`/academy/${courseKey}/${data.previousLessonKey}`)}
                >
                  <Icon name="chevronLeft" className="h-4 w-4" />
                  {M.previousLesson}
                </Button>
              ) : <span />}

              <Button onClick={markRead} loading={completing} disabled={completing}>
                {done
                  ? (data.nextLessonKey ? M.nextLesson : M.backToCourse)
                  : (data.nextLessonKey ? M.markRead : M.finishCourse)}
                <Icon name="arrowRight" className="h-4 w-4" />
              </Button>
            </div>
          </>
        )}
      </QueryState>

      <Link
        to={`/academy/${courseKey}`}
        className="inline-flex items-center gap-1 text-sm text-ink-600 hover:text-ink-900"
      >
        <Icon name="chevronLeft" className="h-4 w-4" />
        {M.backToCourse}
      </Link>
    </div>
  );
}
