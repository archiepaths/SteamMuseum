import { expect, it } from "vitest";
import { deadlineDate, deadlineUtc } from "./dates";
it.each(["2026-03-29", "2026-10-25", "2028-02-29", "2026-12-31"])("keeps deadline %s on the chosen day across calendar boundaries", date => {
  const instant = new Date(deadlineUtc(date));
  expect(instant.getHours()).toBe(0);
  expect(instant.getMinutes()).toBe(0);
  expect(deadlineDate(instant.toISOString())).toBe(date);
  const followingDay = new Date(`${date}T12:00:00`);
  followingDay.setDate(followingDay.getDate() + 1);
  expect(instant.toDateString()).toBe(followingDay.toDateString());
});
it("retains the date of an existing deadline with a time", () => {
  expect(deadlineDate(new Date(2026, 8, 20, 17, 30).toISOString())).toBe("2026-09-20");
});
