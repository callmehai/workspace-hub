export const DAY_START_HOUR = 0;
export const DAY_END_HOUR = 24;
export const WEEK_START_HOUR = 0;
export const WEEK_END_HOUR = 24;
export const HALF_HOUR_HEIGHT = 28;

export interface CalendarTimeGrid {
  startHour: number;
  endHour: number;
  halfHourHeight: number;
}

export const DAY_TIME_GRID: CalendarTimeGrid = {
  startHour: DAY_START_HOUR,
  endHour: DAY_END_HOUR,
  halfHourHeight: HALF_HOUR_HEIGHT,
};

export const WEEK_TIME_GRID: CalendarTimeGrid = {
  startHour: WEEK_START_HOUR,
  endHour: WEEK_END_HOUR,
  halfHourHeight: HALF_HOUR_HEIGHT,
};

export function buildTimeSlots(grid: CalendarTimeGrid) {
  return Array.from({ length: (grid.endHour - grid.startHour) * 2 }, (_, index) => index);
}

export function gridHeight(grid: CalendarTimeGrid) {
  return buildTimeSlots(grid).length * grid.halfHourHeight;
}

export function slotMinutes(grid: CalendarTimeGrid, slot: number) {
  return grid.startHour * 60 + slot * 30;
}

export function entryTop(grid: CalendarTimeGrid, startMinutes: number) {
  return ((startMinutes - grid.startHour * 60) / 30) * grid.halfHourHeight;
}
