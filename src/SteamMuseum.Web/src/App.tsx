import { useEffect, useState } from "react";
import {
  CalendarDays,
  ClipboardList,
  ShieldCheck,
  Users,
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
import {
  BrowserRouter,
  Routes,
  Route,
  NavLink,
  Navigate,
  Link,
  useLocation,
  useNavigate,
} from "react-router";
import PasswordPage from "./PasswordPage";
import RolesPage from "./RolesPage";
import museumLogo from "./img/logo.svg";

export default function App() {
  return (
    <BrowserRouter>
      <StaffApp />
    </BrowserRouter>
  );
}
export function StaffApp() {
  const navigate = useNavigate();
  const location = useLocation();
  const [user, setUser] = useState<Account | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
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
          <img
            className="museum-logo auth-logo"
            src={museumLogo}
            alt="Bressingham Steam Museum"
          />
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
    { id: "roles", label: "Roles", icon: ShieldCheck },
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
  const currentPage = user.mustChangePassword
    ? "password"
    : location.pathname.split("/")[1];
  const assessor = user.roles.some((r) =>
    ["Assessor", "Administrator"].includes(r),
  );
  return (
    <div className={`shell ${collapsed ? "collapsed" : ""}`}>
      <aside className="sidebar">
        <div className="brand">
          <img
            className="museum-logo"
            src={museumLogo}
            alt="Bressingham Steam Museum"
          />
        </div>
        <p className="nav-caption">STAFF ROOM</p>
        <nav aria-label="Main navigation">
          {tabs.map((tab) => (
            <NavLink
              key={tab.id}
              to={`/${tab.id}`}
              aria-disabled={user.mustChangePassword && tab.id !== "password"}
              onClick={(e) => {
                if (user.mustChangePassword && tab.id !== "password")
                  e.preventDefault();
              }}
              className={({ isActive }) => (isActive ? "selected" : "")}
            >
              <tab.icon size={19} />
              <span>{tab.label}</span>
            </NavLink>
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
                navigate("/availability", { replace: true });
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
            <h1>
              {tabs.find((t) => t.id === currentPage)?.label ?? "Staff room"}
            </h1>
          </div>
          {error && (
            <div className="error" role="alert">
              {error}
              <button onClick={() => setError("")}>Dismiss</button>
            </div>
          )}
          <Routes>
            <Route
              path="/password"
              element={
                <PasswordPage
                  user={user}
                  onSaved={async () => {
                    await load();
                    navigate("/availability", { replace: true });
                  }}
                />
              }
            />
            {user.mustChangePassword ? (
              <Route path="*" element={<Navigate to="/password" replace />} />
            ) : (
              <>
                <Route
                  path="/"
                  element={<Navigate to="/availability" replace />}
                />
                <Route
                  path="/availability"
                  element={<AvailabilityPage planner={planner} />}
                />
                <Route
                  path="/availability/manage"
                  element={
                    planner ? (
                      <AvailabilityPage planner managing />
                    ) : (
                      <Navigate to="/availability" replace />
                    )
                  }
                />
                <Route
                  path="/availability/:windowId"
                  element={<AvailabilityPage planner={planner} />}
                />
                <Route
                  path="/availability/:windowId/edit"
                  element={<AvailabilityPage planner={planner} editing />}
                />
                <Route
                  path="/duties"
                  element={<RosterPage planner={false} />}
                />
                <Route
                  path="/roster"
                  element={
                    planner ? (
                      <RosterPage planner />
                    ) : (
                      <Navigate to="/availability" replace />
                    )
                  }
                />
                <Route
                  path="/competence"
                  element={
                    <RecordsPage
                      staff={false}
                      planner={planner}
                      assessor={assessor}
                    />
                  }
                />
                <Route
                  path="/records"
                  element={
                    staff ? (
                      <RecordsPage
                        staff
                        planner={planner}
                        assessor={assessor}
                      />
                    ) : (
                      <Navigate to="/availability" replace />
                    )
                  }
                />
                <Route
                  path="/roles"
                  element={<RolesPage manage={assessor} />}
                />
                <Route
                  path="/roles/:roleId"
                  element={<RolesPage manage={assessor} />}
                />
                <Route
                  path="/admin"
                  element={
                    admin ? (
                      <Administration userId={user.id} />
                    ) : (
                      <Navigate to="/availability" replace />
                    )
                  }
                />
                <Route
                  path="*"
                  element={
                    <section className="card">
                      <h2>Page not found</h2>
                      <Link to="/availability">Go to my availability</Link>
                    </section>
                  }
                />
              </>
            )}
          </Routes>
        </main>
        <footer>
          STEAM MUSEUM <span>Made possible by our people.</span>
        </footer>
      </div>
    </div>
  );
}
