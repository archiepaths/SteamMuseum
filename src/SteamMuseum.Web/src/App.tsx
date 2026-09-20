import { useEffect, useState } from "react";
import {
  CalendarDays,
  ClipboardList,
  ShieldCheck,
  Users,
  TrainFront,
  LogOut,
  KeyRound,
  PanelLeftClose,
} from "lucide-react";
import { ApiError, request } from "./api";
import type { Account } from "./types";
import { ActionForm, Field, value } from "./ui";
import AvailabilityPage from "./Availability";
import RosterPage from "./Roster";
import RecordsPage from "./Records";
import Administration from "./Administration";

export default function App() {
  const [user, setUser] = useState<Account | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [page, setPage] = useState("availability");
  const [collapsed, setCollapsed] = useState(false);
  async function load() {
    setLoading(true);
    setError("");
    try {
      setUser(await request<Account>("/auth/me"));
    } catch (e) {
      if (!(e instanceof ApiError && e.status === 401))
        setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => {
    void load();
    const expired = () => {
      setUser(null);
      setPage("availability");
    };
    window.addEventListener("session-expired", expired);
    return () => window.removeEventListener("session-expired", expired);
  }, []);
  if (loading)
    return (
      <div className="auth">
        <p role="status">Opening the staff room…</p>
      </div>
    );
  if (!user)
    return (
      <div className="auth">
        <div className="auth-brand">
          <TrainFront size={44} />
          <p className="eyebrow">STEAM MUSEUM</p>
          <h1>
            Keeping our
            <br />
            railway moving.
          </h1>
          <p>A place for the people behind every journey.</p>
        </div>
        <section className="auth-form">
          <p className="eyebrow">THE STAFF ROOM</p>
          <h2>Welcome back</h2>
          <p>Sign in to manage your availability and duties.</p>
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
          <ActionForm
            submit="Sign in"
            onSubmit={async (f) => {
              await request("/auth/login", "POST", {
                email: value(f, "email"),
                password: value(f, "password"),
              });
              await load();
            }}
          >
            <Field label="Email address">
              <input
                name="email"
                type="email"
                autoComplete="username"
                required
              />
            </Field>
            <Field label="Password">
              <input
                name="password"
                type="password"
                autoComplete="current-password"
                required
                maxLength={256}
              />
            </Field>
          </ActionForm>
          <p className="muted">
            Need an account or a password reset? Contact your museum
            administrator.
          </p>
        </section>
      </div>
    );
  const planner = user.roles.some((r) =>
    ["Planner", "Administrator"].includes(r),
  );
  const staff = user.roles.some((r) =>
    ["Planner", "Assessor", "Administrator"].includes(r),
  );
  const admin = user.roles.includes("Administrator");
  const tabs = [
    { id: "availability", label: "My availability", icon: CalendarDays },
    { id: "duties", label: "My duties", icon: ClipboardList },
    { id: "competence", label: "My competence", icon: ShieldCheck },
    ...(planner
      ? [{ id: "roster", label: "Roster planner", icon: CalendarDays }]
      : []),
    ...(staff ? [{ id: "records", label: "Staff records", icon: Users }] : []),
    ...(admin
      ? [{ id: "admin", label: "Administration", icon: ShieldCheck }]
      : []),
    { id: "password", label: "Change password", icon: KeyRound },
  ];
  const currentPage = user.mustChangePassword ? "password" : page;
  return (
    <div className={`shell ${collapsed ? "collapsed" : ""}`}>
      <aside className="sidebar">
        <div className="brand">
          <TrainFront size={30} />
          <span>
            STEAM
            <br />
            <b>MUSEUM</b>
          </span>
        </div>
        <p className="nav-caption">STAFF ROOM</p>
        <nav aria-label="Main navigation">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              disabled={user.mustChangePassword && tab.id !== "password"}
              className={currentPage === tab.id ? "selected" : ""}
              onClick={() => setPage(tab.id)}
            >
              <tab.icon size={19} />
              <span>{tab.label}</span>
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <span className="avatar">{user.displayName.slice(0, 1)}</span>
          <div>
            <b>{user.displayName}</b>
            <small>{user.roles.join(" · ")}</small>
          </div>
        </div>
      </aside>
      <div className="workspace">
        <header className="topbar">
          <button
            className="icon-button"
            aria-label="Toggle navigation"
            onClick={() => setCollapsed(!collapsed)}
          >
            <PanelLeftClose size={20} />
          </button>
          <span>People & operations</span>
          <button
            onClick={async () => {
              try {
                await request("/auth/logout", "POST");
                setUser(null);
                setPage("availability");
              } catch (e) {
                setError((e as Error).message);
              }
            }}
          >
            <LogOut size={16} /> Sign out
          </button>
        </header>
        <main>
          <div className="page-heading">
            <p className="eyebrow">STEAM MUSEUM / STAFF</p>
            <h1>{tabs.find((t) => t.id === currentPage)?.label}</h1>
          </div>
          {error && (
            <div className="error" role="alert">
              {error}
              <button onClick={() => setError("")}>Dismiss</button>
            </div>
          )}
          {currentPage === "availability" && (
            <AvailabilityPage planner={planner} />
          )}
          {["duties", "roster"].includes(currentPage) && (
            <RosterPage key={currentPage} planner={currentPage === "roster"} />
          )}
          {["competence", "records"].includes(currentPage) && (
            <RecordsPage
              key={currentPage}
              staff={currentPage === "records"}
              planner={planner}
              assessor={user.roles.some((r) =>
                ["Assessor", "Administrator"].includes(r),
              )}
            />
          )}
          {currentPage === "admin" && admin && (
            <Administration userId={user.id} />
          )}
          {currentPage === "password" && (
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
                  await load();
                  setPage("availability");
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
          )}
        </main>
        <footer>
          STEAM MUSEUM <span>Made possible by our people.</span>
        </footer>
      </div>
    </div>
  );
}
