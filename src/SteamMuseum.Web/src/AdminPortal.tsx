import { useState } from "react";
import {
  Link,
  NavLink,
  Navigate,
  Route,
  Routes,
  useLocation,
} from "react-router";
import {
  ArrowLeft,
  CalendarDays,
  ClipboardList,
  Database,
  History,
  Menu,
  ShieldCheck,
  Users,
  LogOut,
  LayoutDashboard,
} from "lucide-react";
import type { Account } from "./types";
import Administration from "./Administration";
import WindowManagement, { WindowAvailabilityGrid } from "./WindowManagement";
import RosterPage from "./Roster";
import RecordsPage from "./Records";
import RolesPage from "./RolesPage";
import "./admin.css";
export default function AdminPortal({
  user,
  onLogout,
}: {
  user: Account;
  onLogout: () => Promise<void>;
}) {
  const [collapsed, setCollapsed] = useState(false);
  const [error, setError] = useState("");
  const location = useLocation();
  const admin = user.roles.includes("Administrator");
  const planner = admin || user.roles.includes("Planner");
  const assessor = admin || user.roles.includes("Assessor");
  const pages = [
    {
      path: "",
      name: "Overview",
      icon: LayoutDashboard,
      description: "Manage museum operations and staff access.",
    },
    ...(planner
      ? [
          {
            path: "roster",
            name: "Roster planner",
            icon: ClipboardList,
            description: "Create duties, assign staff and publish shifts.",
          },
          {
            path: "windows",
            name: "Availability windows",
            icon: CalendarDays,
            description: "Open submission periods and update window notes.",
          },
        ]
      : []),
    {
      path: "records",
      name: "Staff records",
      icon: Users,
      description: "Review staff availability, assessments and training.",
    },
    {
      path: "roles",
      name: "Roles & elements",
      icon: ShieldCheck,
      description: "Browse role requirements and competence elements.",
    },
    ...(admin
      ? [
          {
            path: "accounts",
            name: "Staff accounts",
            icon: Users,
            description: "Create accounts and manage staff access.",
          },
          {
            path: "references",
            name: "Reference data",
            icon: Database,
            description: "Maintain railways and locomotives.",
          },
          {
            path: "activity",
            name: "Activity history",
            icon: History,
            description: "Review recorded changes and actions.",
          },
        ]
      : []),
  ];
  const current = pages.find(
    (p) => p.path === (location.pathname.split("/")[2] ?? ""),
  );
  const denied = <Navigate to="/manage" replace />;
  return (
    <div className={`admin-portal ${collapsed ? "admin-collapsed" : ""}`}>
      <header className="admin-header">
        <button
          aria-label="Toggle management navigation"
          aria-expanded={!collapsed}
          onClick={() => setCollapsed(!collapsed)}
        >
          <Menu size={18} />
        </button>
        <Link to="/manage" className="admin-brand">
          Steam Museum <span>Management</span>
        </Link>
        <div className="admin-identity">{user.displayName}</div>
        <button
          onClick={async () => {
            try {
              await onLogout();
            } catch (e) {
              setError((e as Error).message);
            }
          }}
        >
          <LogOut size={15} /> Sign out
        </button>
      </header>
      <aside className="admin-sidebar">
        <Link className="admin-return" to="/availability">
          <ArrowLeft size={16} />
          <span>Staff portal</span>
        </Link>
        <p>MANAGEMENT</p>
        <nav aria-label="Management navigation">
          {pages.map((p) => (
            <NavLink
              key={p.path}
              to={`/manage${p.path ? `/${p.path}` : ""}`}
              end={!p.path}
              title={p.name}
            >
              <p.icon size={17} />
              <span>{p.name}</span>
            </NavLink>
          ))}
        </nav>
      </aside>
      <main className="admin-main">
        <div className="admin-breadcrumb">
          <Link to="/manage">Management</Link>
          {current?.path && (
            <>
              <span>/</span>
              <span>{current.name}</span>
            </>
          )}
        </div>
        <div className="admin-page-title">
          <h1>{current?.name ?? "Management"}</h1>
          <span>Steam Museum</span>
        </div>
        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}
        <Routes>
          <Route
            path="/manage"
            element={
              <>
                <p className="intro">
                  Manage operations, people and access. Choose a workspace to
                  get started.
                </p>
                <div className="admin-services">
                  {pages
                    .filter((p) => p.path)
                    .map((p) => (
                      <Link key={p.path} to={`/manage/${p.path}`}>
                        <p.icon size={22} />
                        <div>
                          <h2>{p.name}</h2>
                          <p>{p.description}</p>
                        </div>
                      </Link>
                    ))}
                </div>
              </>
            }
          />
          <Route
            path="/manage/roster"
            element={planner ? <RosterPage planner /> : denied}
          />
          <Route
            path="/manage/windows"
            element={planner ? <WindowManagement /> : denied}
          />
          <Route
            path="/manage/windows/:windowId"
            element={planner ? <WindowAvailabilityGrid /> : denied}
          />
          <Route
            path="/manage/records"
            element={
              <RecordsPage staff planner={planner} assessor={assessor} />
            }
          />
          <Route
            path="/manage/roles"
            element={<RolesPage manage={assessor} />}
          />
          <Route
            path="/manage/roles/:roleId"
            element={<RolesPage manage={assessor} />}
          />
          {(["accounts", "references", "activity"] as const).map((section) => (
            <Route
              key={section}
              path={`/manage/${section}`}
              element={
                admin ? (
                  <Administration userId={user.id} section={section} />
                ) : (
                  denied
                )
              }
            />
          ))}
          <Route
            path="*"
            element={
              <p>
                Page not found. <Link to="/manage">Return to management</Link>
              </p>
            }
          />
        </Routes>
      </main>
    </div>
  );
}
