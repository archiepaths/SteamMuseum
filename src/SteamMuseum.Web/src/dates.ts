export function localDate(date = new Date()) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}
export function datesBetween(start: string, end: string) {
  const days: string[] = [];
  const date = new Date(`${start}T12:00:00`);
  while (localDate(date) <= end && days.length < 370) {
    days.push(localDate(date));
    date.setDate(date.getDate() + 1);
  }
  return days;
}
export const prettyDate = (date: string) =>
  new Date(`${date}T12:00:00`).toLocaleDateString("en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
export const roleName = (role: string) =>
  role === "StationStaff" ? "Station staff" : role;
export const monthRange = () => {
  const now = new Date();
  return {
    from: localDate(new Date(now.getFullYear(), now.getMonth(), 1)),
    until: localDate(new Date(now.getFullYear(), now.getMonth() + 1, 0)),
  };
};
