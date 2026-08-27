export interface AuthResponse {
  token: string;
  refreshToken: string;
}

export interface HabitDto {
  id: number;
  name: string;
  category: string;
  customCategoryName: string | null;
  unit: "Count" | "Minutes" | "Hours";
  dailyGoal: number;
  createdAt: string;
  xpPerUnit: number;
  xpBonusForGoal: number;
  period: "Daily" | "Weekly" | "Monthly";
  targetTime: string | null;
  reminderTime: string | null;
  isArchived: boolean;
  archivedAt: string | null;
  notes: string | null;
}

export interface HabitProgressDto {
  habitId: number;
  dailyGoal: number;
  totalToday: number;
  totalInPeriod: number;
  isCompleted: boolean;
  percentageCompleted: number;
  currentStreak: number;
  period: "Daily" | "Weekly" | "Monthly";
  periodStart: string;
  periodEnd: string;
}

export interface BookDto {
  id: number;
  title: string;
  author: string | null;
  goalType: "Pages" | "Minutes";
  period: "Daily" | "Weekly" | "Monthly";
  totalPages: number | null;
  dailyGoalAmount: number;
  currentPage: number;
  totalMinutesRead: number;
  isCompleted: boolean;
  percentageCompleted: number | null;
  createdAt: string;
  completedAt: string | null;
  isArchived: boolean;
  archivedAt: string | null;
  notes: string | null;
  coverImageUrl: string | null;
}

export interface PetDto {
  id: number;
  type: string;
  level: number;
  xp: number;
  mood: string;
  createdAt: string;
  nickname: string | null;
  stage: "Egg" | "Hatched";
  hatchedAt: string | null;
  isEgg: boolean;
  equippedAccessory: string | null;
}

export interface FlowerDto {
  id: number;
  waterAmount: number;
  level: number;
  stage: string;
  createdAt: string;
  updatedAt: string;
}

export interface DashboardDto {
  totalXp: number;
  focusXpPool: number;
  habits: HabitProgressDto[];
  books: BookDto[];
  pets: PetDto[];
  flower: FlowerDto | null;
  unreadNotificationCount: number;
}

export interface PagedResultDto<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
