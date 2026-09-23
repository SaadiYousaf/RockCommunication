/**
 * Centralized user-facing copy for the Learning Academy.
 * Keep inline message strings out of the pages — reference these instead.
 */
export const ACADEMY_MSG = {
  /** Lowercase noun for the "Couldn't load <x>" error state. */
  resourceName: "your learning path",
  lessonResourceName: "this lesson",
  certificatesResourceName: "your certificates",

  title: "Academy",
  subtitle: "Learn the CRM one short lesson at a time. Your progress is saved as you go.",

  // Dashboard
  keepGoing: "Pick up where you left off",
  startLearning: "Start learning",
  resumeCta: (lesson: string) => `Continue: ${lesson}`,
  firstLessonCta: "Start your first lesson",

  statCourses: "Courses finished",
  statLessons: "Lessons read",
  statMinutes: "Time spent learning",
  statCertificates: "Certificates",

  // Courses
  yourPath: "Your path",
  pathHint: "These courses are chosen for what you do here, so nothing on this list is someone else's job.",
  lessonCount: (n: number) => (n === 1 ? "1 lesson" : `${n} lessons`),
  minutes: (n: number) => `${n} min`,
  progressOf: (done: number, total: number) => `${done} of ${total} done`,
  courseComplete: "Complete",
  notStarted: "Not started",
  inProgress: "In progress",
  startCourse: "Start",
  continueCourse: "Continue",
  reviewCourse: "Review",

  levelBeginner: "Beginner",
  levelIntermediate: "Intermediate",
  levelAdvanced: "Advanced",
  levelAdmin: "For admins",

  noCoursesTitle: "Nothing to learn just yet",
  noCoursesDesc: "Courses appear here as they're added for your role. Check back soon.",

  // Lesson
  backToCourse: "Back to course",
  openTheScreen: "Open the real screen",
  openTheScreenHint: "Opens the actual page in the CRM so you can follow along.",
  markRead: "Mark as read",
  markedRead: "Lesson complete",
  alreadyRead: "Read",
  nextLesson: "Next lesson",
  previousLesson: "Previous",
  finishCourse: "Finish course",
  completeFailed: "Couldn't save your progress",

  // Quiz
  quizTitle: "Check your understanding",
  quizHint: "Answer these to confirm it landed. You can retake it as many times as you like.",
  submitQuiz: "Check my answers",
  retakeQuiz: "Try again",
  quizIncomplete: "Answer every question first",
  quizFailed: "Couldn't mark your quiz",
  quizScore: (score: number, total: number) => `${score} of ${total} correct`,
  quizPassed: "Nicely done — that's a pass.",
  quizNotPassed: "Not quite yet. Read the notes below and give it another go.",
  answerCorrect: "Correct",
  answerWrong: "Not quite",

  // Certificates
  certificates: "Certificates",
  certificatesHint: "Earned by finishing every lesson in a course.",
  noCertificatesTitle: "No certificates yet",
  noCertificatesDesc: "Finish every lesson in a course and one is issued automatically.",
  certificateEarned: (course: string) => `Certificate earned: ${course}`,
  certificateAwardedTo: "This certifies that",
  certificateCompleted: "has completed",
  certificateIssued: "Issued",
  certificateSerial: "Certificate no.",
  printCertificate: "Print",
  viewCertificate: "View certificate",
  courseFinishedTitle: "Course complete",

  // The one-page overview that predates the Academy, kept and linked rather than duplicated.
  overviewTitle: "The whole pipeline on one page",
  overviewDesc: "A single-page map of how a lead travels from first click to paid commission, plus every role and a glossary.",
  overviewCta: "Open the overview",
} as const;
