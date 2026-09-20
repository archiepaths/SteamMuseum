import { useState } from "react";
import { request } from "./api";
import type { Audit, Member, Reference } from "./types";
import {
  ActionForm,
  Badge,
  Empty,
  Field,
  Status,
  useResource,
  value,
} from "./ui";

function RoleFields() {
  return (
    <div className="role-options">
      <b>Access roles</b>
      <label>
        <input type="checkbox" checked disabled />
        Member (always included)
      </label>
      {["Planner", "Assessor", "Administrator"].map((role) => (
        <label key={role}>
          <input type="checkbox" name="roles" value={role} />
          {role}
        </label>
      ))}
    </div>
  );
}
export default function Administration({ userId }: { userId: string }) {
  const members = useResource<Member[]>("/members");
  const [memberId, setMemberId] = useState("");
  const [since, setSince] = useState(() =>
    new Date(Date.now() - 7 * 86400000).toISOString(),
  );
  const audit = useResource<Audit[]>(
    `/audit?sinceUtc=${encodeURIComponent(since)}`,
  );
  const selected = members.data?.find((m) => m.id === memberId);
  return (
    <>
      <p className="intro">
        Manage staff access, reference data and the museum’s activity history.
      </p>
      <div className="split">
        <section className="card">
          <h2>Create staff account</h2>
          <ActionForm
            submit="Create account"
            onSubmit={async (f) => {
              await request("/accounts", "POST", {
                displayName: value(f, "displayName"),
                email: value(f, "email"),
                temporaryPassword: value(f, "password"),
                roles: ["Member", ...f.getAll("roles")],
              });
              members.reload();
            }}
          >
            <Field label="Full name">
              <input name="displayName" required maxLength={150} />
            </Field>
            <Field label="Email">
              <input name="email" type="email" required maxLength={256} />
            </Field>
            <Field label="Temporary password">
              <input
                name="password"
                type="password"
                autoComplete="new-password"
                required
                minLength={12}
                maxLength={256}
              />
            </Field>
            <p className="muted">
              Use 12+ characters with uppercase, lowercase, a number and a
              symbol. The member must change it on first sign-in.
            </p>
            <RoleFields />
          </ActionForm>
        </section>
        <section className="card">
          <h2>Manage an account</h2>
          <Status resource={members} />
          <Field label="Staff member">
            <select
              value={memberId}
              onChange={(e) => setMemberId(e.target.value)}
            >
              <option value="">Choose a member</option>
              {members.data?.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.displayName}
                </option>
              ))}
            </select>
          </Field>
          {selected && (
            <div key={selected.id}>
              <p>
                <Badge tone={selected.active ? "green" : ""}>
                  {selected.active ? "Active" : "Inactive"}
                </Badge>
              </p>
              {selected.id === userId ? (
                <p className="notice">
                  Use Change password for your own account. Ask another
                  administrator to manage your access.
                </p>
              ) : (
                <>
                  <ActionForm
                    submit={
                      selected.active
                        ? "Deactivate account"
                        : "Reactivate account"
                    }
                    onSubmit={async () => {
                      await request(`/accounts/${selected.id}/active`, "PUT", {
                        active: !selected.active,
                      });
                      members.reload();
                    }}
                  >
                    <p className="muted">
                      Deactivation prevents sign-in and revokes existing
                      sessions.
                    </p>
                  </ActionForm>
                  <details>
                    <summary>Replace access roles</summary>
                    <ActionForm
                      submit="Replace roles"
                      onSubmit={async (f) => {
                        await request(`/accounts/${selected.id}/roles`, "PUT", {
                          roles: ["Member", ...f.getAll("roles")],
                        });
                      }}
                    >
                      <p className="notice">
                        The API does not expose current account roles. Select
                        the complete replacement set below. Saving revokes this
                        member’s existing sessions.
                      </p>
                      <RoleFields />
                      <label className="check">
                        <input type="checkbox" required />I have checked the
                        replacement roles.
                      </label>
                    </ActionForm>
                  </details>
                  <details>
                    <summary>Reset password</summary>
                    <ActionForm
                      submit="Reset password"
                      onSubmit={async (f) => {
                        await request(
                          `/accounts/${selected.id}/reset-password`,
                          "POST",
                          { temporaryPassword: value(f, "password") },
                        );
                      }}
                    >
                      <Field label="New temporary password">
                        <input
                          name="password"
                          type="password"
                          required
                          minLength={12}
                          maxLength={256}
                          autoComplete="new-password"
                        />
                      </Field>
                      <p className="muted">
                        This revokes existing sessions and requires a password
                        change on next sign-in.
                      </p>
                    </ActionForm>
                  </details>
                </>
              )}
            </div>
          )}
        </section>
      </div>
      <div className="split">
        <ReferenceEditor title="Railways" path="/railways" />
        <ReferenceEditor title="Locomotives" path="/locomotives" />
      </div>
      <section className="card">
        <div className="section-heading">
          <h2>Activity history</h2>
          <Field label="Period">
            <select
              defaultValue="7"
              onChange={(e) =>
                setSince(
                  new Date(
                    Date.now() - Number(e.target.value) * 86400000,
                  ).toISOString(),
                )
              }
            >
              <option value="1">Last 24 hours</option>
              <option value="7">Last 7 days</option>
              <option value="30">Last 30 days</option>
            </select>
          </Field>
          <button onClick={audit.reload}>Refresh</button>
        </div>
        <Status resource={audit} />
        {audit.data?.length === 0 && (
          <Empty>No activity during this period.</Empty>
        )}
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Time</th>
                <th>Action</th>
                <th>Staff member</th>
                <th>Details</th>
              </tr>
            </thead>
            <tbody>
              {audit.data?.map((a) => (
                <tr key={a.id}>
                  <td>{new Date(a.atUtc).toLocaleString("en-GB")}</td>
                  <td>{a.action}</td>
                  <td>
                    {members.data?.find((m) => m.id === a.actorId)
                      ?.displayName ?? a.actorId}
                  </td>
                  <td>{a.details}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </>
  );
}
function ReferenceEditor({ title, path }: { title: string; path: string }) {
  const records = useResource<Reference[]>(path);
  return (
    <section className="card">
      <h2>{title}</h2>
      <Status resource={records} />
      {records.data?.length === 0 && (
        <Empty>No {title.toLowerCase()} recorded.</Empty>
      )}
      <ul className="reference-list">
        {records.data?.map((r) => (
          <li key={r.id}>{r.name}</li>
        ))}
      </ul>
      <ActionForm
        submit="Add reference"
        onSubmit={async (f) => {
          await request(path, "POST", { name: value(f, "name") });
          records.reload();
        }}
      >
        <Field label="Name">
          <input name="name" required maxLength={150} />
        </Field>
      </ActionForm>
    </section>
  );
}
