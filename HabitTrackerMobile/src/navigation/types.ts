export type AuthStackParamList = {
  Login: undefined;
  Register: undefined;
  ConfirmEmail: { email: string };
};

export type AppStackParamList = {
  Dashboard: undefined;
  Habits: undefined;
  HabitDetail: { habitId: number };
  Books: undefined;
  BookDetail: { bookId: number };
  Pets: undefined;
  PetDetail: { petId: number };
  Profile: undefined;
};
