import type { Account } from "./types";
import { request } from "./api";
import { ActionForm, Field, value } from "./ui";
export default function PasswordPage({
  user,
  onSaved,
}: {
  user: Account;
  onSaved: () => Promise<void>;
}) {
  return (
    <section className="card narrow">
      <h2>
        {user.mustChangePassword
          ? "Choose your own password"
          : "Update your password"}
      </h2>
      <p>
        {user.mustChangePassword
          ? "Change your temporary password before accessing staff records."
          : "Use at least 12 characters, including uppercase, lowercase, a number and a symbol."}
      </p>
      <ActionForm
        submit="Change password"
        onSubmit={async (f) => {
          if (value(f, "newPassword") !== value(f, "confirm"))
            throw new Error("The new passwords do not match.");
          await request("/auth/password", "POST", {
            currentPassword: value(f, "currentPassword"),
            newPassword: value(f, "newPassword"),
          });
          await onSaved();
        }}
      >
        <Field label="Current password">
          <input
            name="currentPassword"
            type="password"
            autoComplete="current-password"
            required
          />
        </Field>
        <Field label="New password">
          <input
            name="newPassword"
            type="password"
            autoComplete="new-password"
            minLength={12}
            maxLength={256}
            required
          />
        </Field>
        <Field label="Confirm new password">
          <input
            name="confirm"
            type="password"
            autoComplete="new-password"
            required
          />
        </Field>
      </ActionForm>
    </section>
  );
}
