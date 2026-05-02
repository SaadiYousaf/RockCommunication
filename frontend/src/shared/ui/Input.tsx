import { forwardRef, type InputHTMLAttributes, type ReactNode, type TextareaHTMLAttributes, type SelectHTMLAttributes } from "react";
import { cn } from "./cn";
import { useSecureEntry } from "./secureEntry";

interface FieldProps {
  label?: string;
  hint?: string;
  error?: string;
  /** Right-side helper text rendered next to the label (e.g. "Optional", "Max 50"). */
  labelHint?: string;
  leftIcon?: ReactNode;
  rightSlot?: ReactNode;
  containerClassName?: string;
  /**
   * When true, copy / paste / drop is blocked for typing-only roles
   * (Fronter / Verifier / Closer). No effect for other roles.
   */
  secure?: boolean;
}

/** Shared label row — keeps label + optional right-hint typography consistent. */
function Label({ htmlFor, children, required, hint }: {
  htmlFor: string | undefined;
  children: ReactNode;
  required?: boolean;
  hint?: string;
}) {
  return (
    <div className="flex items-baseline justify-between gap-2">
      <label htmlFor={htmlFor} className="text-[12px] font-medium text-ink-700 leading-none">
        {children}
        {required && <span className="text-rose-500 ml-0.5" aria-hidden>*</span>}
      </label>
      {hint && <span className="text-[10.5px] text-ink-400 font-medium">{hint}</span>}
    </div>
  );
}

/** Shared hint/error row — keeps spacing consistent. */
function HintRow({ hint, error }: { hint?: string; error?: string }) {
  if (!hint && !error) return null;
  return (
    <p className={cn(
      "text-[11.5px] leading-snug mt-0.5",
      error ? "text-rose-600 font-medium" : "text-ink-500",
    )}>
      {error ?? hint}
    </p>
  );
}

export interface InputProps extends InputHTMLAttributes<HTMLInputElement>, FieldProps {}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { label, hint, labelHint, error, leftIcon, rightSlot, className, containerClassName, id, secure, ...rest }, ref,
) {
  const inputId = id ?? rest.name;
  const { handlers: secureHandlers } = useSecureEntry(secure);
  return (
    <div className={cn("flex flex-col gap-1.5", containerClassName)}>
      {label && <Label htmlFor={inputId} required={rest.required} hint={labelHint}>{label}</Label>}
      <div className="relative">
        {leftIcon && (
          <div className="absolute inset-y-0 left-3 flex items-center text-ink-400 pointer-events-none">
            {leftIcon}
          </div>
        )}
        <input
          id={inputId}
          ref={ref}
          className={cn(
            "input-base",
            leftIcon ? "pl-10" : "",
            rightSlot ? "pr-10" : "",
            error ? "border-rose-400 focus:border-rose-500 focus:ring-rose-500/20" : "",
            className,
          )}
          {...rest}
          {...secureHandlers}
        />
        {rightSlot && <div className="absolute inset-y-0 right-2 flex items-center">{rightSlot}</div>}
      </div>
      <HintRow hint={hint} error={error} />
    </div>
  );
});

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement>, Omit<FieldProps, "leftIcon" | "rightSlot"> {}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea(
  { label, hint, labelHint, error, className, containerClassName, id, secure, ...rest }, ref,
) {
  const inputId = id ?? rest.name;
  const { handlers: secureHandlers } = useSecureEntry(secure);
  return (
    <div className={cn("flex flex-col gap-1.5", containerClassName)}>
      {label && <Label htmlFor={inputId} required={rest.required} hint={labelHint}>{label}</Label>}
      <textarea
        id={inputId} ref={ref}
        className={cn(
          "input-base min-h-[88px] resize-y leading-relaxed",
          error && "border-rose-400 focus:border-rose-500 focus:ring-rose-500/20",
          className,
        )}
        {...rest}
        {...secureHandlers}
      />
      <HintRow hint={hint} error={error} />
    </div>
  );
});

export interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement>, Omit<FieldProps, "leftIcon" | "rightSlot"> {}

export const Select = forwardRef<HTMLSelectElement, SelectProps>(function Select(
  { label, hint, labelHint, error, className, containerClassName, id, children, secure: _secure, ...rest }, ref,
) {
  const inputId = id ?? rest.name;
  return (
    <div className={cn("flex flex-col gap-1.5", containerClassName)}>
      {label && <Label htmlFor={inputId} required={rest.required} hint={labelHint}>{label}</Label>}
      <select
        id={inputId} ref={ref}
        className={cn(
          "input-base appearance-none pr-9 bg-no-repeat bg-[right_0.65rem_center] bg-[length:1rem]",
          // SVG chevron — color matches ink-500 so it sits well on light surfaces
          "bg-[url('data:image/svg+xml;utf8,<svg xmlns=%22http://www.w3.org/2000/svg%22 fill=%22none%22 viewBox=%220 0 20 20%22 stroke=%22%23646d7e%22 stroke-width=%221.6%22><path stroke-linecap=%22round%22 stroke-linejoin=%22round%22 d=%22M6 8l4 4 4-4%22/></svg>')]",
          // Slight cursor affordance
          "cursor-pointer",
          error && "border-rose-400 focus:border-rose-500 focus:ring-rose-500/20",
          className,
        )}
        {...rest}
      >
        {children}
      </select>
      <HintRow hint={hint} error={error} />
    </div>
  );
});
