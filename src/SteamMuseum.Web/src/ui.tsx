import { useEffect, useState, type ReactNode, type FormEvent } from "react";
import { request } from "./api";

export function useResource<T>(path: string | null, version = 0) {
  const [data, setData] = useState<T>();
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true;
    setData(undefined);
    setError("");
    setLoading(!!path);
    if (path)
      request<T>(path)
        .then((value) => {
          if (active) setData(value);
        })
        .catch((e) => {
          if (active) setError(e.message);
        })
        .finally(() => {
          if (active) setLoading(false);
        });
    return () => {
      active = false;
    };
  }, [path, version, retry]);
  return { data, loading, error, reload: () => setRetry((x) => x + 1) };
}
export function Status({
  resource,
}: {
  resource: { loading: boolean; error: string; reload: () => void };
}) {
  return resource.loading ? (
    <p role="status" className="empty">
      Loading records…
    </p>
  ) : resource.error ? (
    <div role="alert" className="error">
      {resource.error} <button onClick={resource.reload}>Try again</button>
    </div>
  ) : null;
}
export function Field({
  label,
  children,
}: {
  label: string;
  children: ReactNode;
}) {
  return (
    <label className="field">
      <span>{label}</span>
      {children}
    </label>
  );
}
export function Empty({ children }: { children: ReactNode }) {
  return <p className="empty">{children}</p>;
}
export function Badge({
  children,
  tone = "",
}: {
  children: ReactNode;
  tone?: string;
}) {
  return <span className={`badge ${tone}`}>{children}</span>;
}
export function ActionForm({
  onSubmit,
  children,
  submit = "Save changes",
  className = "",
  disabled = false,
  submitDisabled = false,
}: {
  onSubmit: (data: FormData) => Promise<unknown>;
  children: ReactNode;
  submit?: string;
  className?: string;
  disabled?: boolean;
  submitDisabled?: boolean;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [saved, setSaved] = useState(false);
  async function handle(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || disabled || submitDisabled) return;
    const data = new FormData(event.currentTarget);
    setBusy(true);
    setError("");
    setSaved(false);
    try {
      await onSubmit(data);
      setSaved(true);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <form
      className={className}
      onSubmit={handle}
      onChange={() => setSaved(false)}
    >
      <fieldset disabled={busy || disabled}>
        {children}
        <div className="form-footer">
          <button className="primary" type="submit" disabled={submitDisabled}>
            {busy ? "Saving…" : submit}
          </button>
          {saved && <span role="status">Saved successfully.</span>}
        </div>
      </fieldset>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
    </form>
  );
}
export const value = (form: FormData, key: string) =>
  String(form.get(key) ?? "");
export const nullable = (form: FormData, key: string) =>
  value(form, key) || null;
