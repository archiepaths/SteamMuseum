// Isolated browser fixture. Serves the production build with synthetic API responses.
// Never used by npm run dev, npm run preview, or production deployments.
import http from "node:http";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = path.resolve(fileURLToPath(new URL("../dist/", import.meta.url)));
const window = {
  id: "fixture-window",
  name: "September operating days",
  kind: "Monthly",
  start: "2026-09-01",
  end: "2026-09-30",
  submissionDeadlineUtc: "2099-09-25T17:00:00Z",
  isOpen: true,
};
const member = {
  id: "fixture-member",
  displayName: "Preview staff",
  active: true,
};
let availability = {
  window,
  maximumAssignments: 4,
  assigned: 1,
  days: [5, 6, 12, 13, 19, 20, 26, 27].map((n) => ({
    date: `2026-09-${String(n).padStart(2, "0")}`,
    status: "Available",
    from: null,
    until: null,
    preferredRole: "Guard",
    note: null,
  })),
};
const rows = [
  {
    duty: {
      id: "fixture-duty",
      name: "Weekend passenger service",
      date: "2026-09-26",
      start: "09:00:00",
      end: "16:30:00",
      role: "Guard",
      railwayId: "railway",
      locomotiveId: "locomotive",
    },
    assignment: { id: "assignment", memberId: member.id, status: "Draft" },
    issues: [],
    preferredRole: "Guard",
  },
];
let signedIn = true;
const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, "http://localhost:5180");
    const json = (data, status = 200) => {
      res.writeHead(status, {
        "Content-Type": "application/json",
        "Cache-Control": "no-store",
      });
      res.end(JSON.stringify(data));
    };
    if (url.pathname.startsWith("/api/")) {
      let body = "";
      for await (const chunk of req) body += chunk;
      const data = body ? JSON.parse(body) : {};
      const route = url.pathname.slice(4);
      if (route === "/auth/csrf") return json({ token: "fixture-only" });
      if (route === "/auth/logout") {
        signedIn = false;
        return json({});
      }
      if (route === "/auth/login") {
        signedIn = true;
        return json({});
      }
      if (!signedIn) return json({ title: "Sign in required." }, 401);
      if (route === "/auth/me")
        return json({
          ...member,
          email: "preview@example.test",
          roles: ["Member", "Administrator"],
          mustChangePassword: false,
        });
      if (route === "/windows") return json([window]);
      if (route.includes("/availability/")) {
        if (req.method === "PUT") availability = { ...availability, ...data };
        return json(availability);
      }
      if (route === "/railways")
        return json([{ id: "railway", name: "Museum railway" }]);
      if (route === "/locomotives")
        return json([{ id: "locomotive", name: "No. 7 — Pioneer" }]);
      if (route === "/members") return json([member]);
      if (route === "/competence-elements")
        return json([
          {
            id: "safety",
            name: "Track safety",
            description: "Safe working",
            learningType: "TheoryAndPractical",
            reassessmentMonths: 12,
            active: true,
          },
        ]);
      if (route === "/competence-roles")
        return json([
          {
            id: "guard",
            name: "Museum guard",
            category: "Guard",
            railwayId: "railway",
            locomotiveId: null,
            baseRoleId: null,
            active: true,
            requirements: [{ elementId: "safety" }],
          },
        ]);
      if (route.endsWith("/element-assessments")) return json([]);
      if (route.endsWith("/role-eligibility"))
        return json([
          {
            roleId: "guard",
            name: "Museum guard",
            qualified: false,
            elements: [
              {
                elementId: "safety",
                name: "Track safety",
                status: "Not assessed",
                reassessmentDue: null,
              },
            ],
            issues: ["Track safety: Not assessed."],
          },
        ]);
      if (route.endsWith("/roster"))
        return json(
          rows.filter(
            (r) =>
              r.duty.date >= url.searchParams.get("from") &&
              r.duty.date <= url.searchParams.get("until") &&
              (route !== "/me/roster" || r.assignment.status === "Published"),
          ),
        );
      if (route === "/assignments/assignment/publish") {
        rows[0].assignment.status = "Published";
        return json(rows[0]);
      }
      if (route === "/assignments/assignment/cancel") {
        rows[0].assignment.status = "Cancelled";
        return json(rows[0]);
      }
      if (route.endsWith("/training") || route === "/audit") return json([]);
      return json(
        {
          title:
            "This operation is not implemented by the isolated browser fixture.",
        },
        400,
      );
    }
    const target = path.resolve(
      root,
      "." +
        decodeURIComponent(url.pathname === "/" ? "/index.html" : url.pathname),
    );
    if (
      !target.startsWith(root + path.sep) &&
      target !== path.join(root, "index.html")
    ) {
      res.writeHead(403);
      res.end();
      return;
    }
    const content = await readFile(target);
    res.writeHead(200, {
      "Content-Type":
        { ".html": "text/html", ".js": "text/javascript", ".css": "text/css" }[
          path.extname(target)
        ] ?? "application/octet-stream",
    });
    res.end(content);
  } catch {
    res.writeHead(404);
    res.end("Not found");
  }
});
server.listen(5180, "localhost", () =>
  console.log(
    "Synthetic browser fixture: http://localhost:5180 (no real accounts or database).",
  ),
);
