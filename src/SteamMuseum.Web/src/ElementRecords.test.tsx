// @vitest-environment jsdom
import { afterEach, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { MemoryRouter } from "react-router";
import ElementRecords from "./ElementRecords";
import { DutyFields } from "./Roster";
import { request } from "./api";
vi.mock("./api", () => ({ request: vi.fn() }));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});
const element = {
  id: "safety",
  name: "Personal track safety",
  description: "Safe working",
  learningType: "Theory",
  reassessmentMonths: 12,
  active: true,
};
function setup() {
  vi.mocked(request).mockImplementation(async (path) => {
    if (path === "/competence-elements") return [element];
    if (path.includes("role-eligibility"))
      return [
        {
          roleId: "driver",
          name: "Railway A driver",
          qualified: false,
          issues: ["Personal track safety: Not competent."],
          elements: [
            {
              elementId: "safety",
              name: element.name,
              status: "Not competent",
              reassessmentDue: "2030-09-23",
            },
          ],
        },
      ];
    return [];
  });
}
it("shows failed required elements and prevents members from accessing assessment forms", async () => {
  setup();
  render(
    <MemoryRouter>
      <ElementRecords prefix="/me" memberId="" manage={false} />
    </MemoryRouter>,
  );
  await screen.findByText("Not qualified");
  expect(screen.getByText("Not competent")).toBeTruthy();
  expect(screen.queryByText("Record assessment or reassessment")).toBeNull();
  expect(screen.queryByText("Add competence element")).toBeNull();
});
it("records a not competent assessment for the selected member", async () => {
  setup();
  render(
    <MemoryRouter>
      <ElementRecords prefix="/members/member" memberId="member" manage />
    </MemoryRouter>,
  );
  await screen.findByText("Not qualified");
  fireEvent.click(screen.getByText("Record assessment or reassessment"));
  fireEvent.change(screen.getByLabelText("Competence element"), {
    target: { value: "safety" },
  });
  fireEvent.change(screen.getByLabelText("Assessment outcome"), {
    target: { value: "NotCompetent" },
  });
  fireEvent.change(screen.getByLabelText("Assessment date"), {
    target: { value: "2026-09-01" },
  });
  fireEvent.change(screen.getByLabelText("Assessment evidence"), {
    target: { value: "Further practical learning required" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Record assessment" }));
  await waitFor(() =>
    expect(request).toHaveBeenCalledWith("/element-assessments", "POST", {
      memberId: "member",
      elementId: "safety",
      outcome: "NotCompetent",
      assessedOn: "2026-09-01",
      evidence: "Further practical learning required",
    }),
  );
});
it("retains inherited operating scope when selecting a variant for a duty", () => {
  render(
    <form aria-label="Duty">
      <DutyFields
        railways={[{ id: "railway", name: "Railway A" }]}
        locomotives={[{ id: "loco", name: "Locomotive B" }]}
        competenceRoles={[
          {
            id: "variant",
            name: "A driver with B",
            baseRoleId: null,
            category: "Driver",
            railwayId: "railway",
            locomotiveId: "loco",
            active: true,
            requirements: [],
          },
        ]}
      />
    </form>,
  );
  fireEvent.change(screen.getByLabelText("Competence role or variant"), {
    target: { value: "variant" },
  });
  const form = screen.getByRole("form", { name: "Duty" }) as HTMLFormElement;
  const data = new FormData(form);
  expect(data.get("role")).toBe("Driver");
  expect(data.get("railwayId")).toBe("railway");
  expect(data.get("locomotiveId")).toBe("loco");
  expect(data.get("competenceRoleId")).toBe("variant");
  expect(
    (within(form).getByLabelText("Railway") as HTMLSelectElement).disabled,
  ).toBe(true);
});
