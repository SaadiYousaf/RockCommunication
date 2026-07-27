import { Icon } from "./Icon";
import { Input } from "./Input";
import { cn } from "./cn";

export interface SearchInputProps {
  value: string;
  /** Receives the string value directly (not the event) for terse call sites. */
  onChange: (value: string) => void;
  placeholder?: string;
  className?: string;
  containerClassName?: string;
  autoFocus?: boolean;
  "aria-label"?: string;
}

/**
 * Canonical search field for every list / table page: one consistent look, a leading search
 * icon, and a clear (×) button that appears once there's text. Controlled — `onChange` gets the
 * string value. Replaces the ~18 hand-rolled `<Input leftIcon={search} …>` search boxes so search
 * looks and behaves identically everywhere, like an enterprise CRM.
 */
export function SearchInput({
  value, onChange, placeholder = "Search…", className, containerClassName, autoFocus, ...rest
}: SearchInputProps) {
  return (
    <Input
      type="search"
      autoComplete="off"
      leftIcon={<Icon name="search" size={14} />}
      rightSlot={value ? (
        <button
          type="button"
          aria-label="Clear search"
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => onChange("")}
          className="text-ink-400 hover:text-ink-700 rounded p-0.5 transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400"
        >
          <Icon name="x" size={14} />
        </button>
      ) : undefined}
      placeholder={placeholder}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className={cn(className)}
      containerClassName={containerClassName}
      autoFocus={autoFocus}
      {...rest}
    />
  );
}
