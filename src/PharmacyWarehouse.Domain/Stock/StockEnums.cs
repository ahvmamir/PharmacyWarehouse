namespace PharmacyWarehouse.Domain.Stock;

// دقت تاریخ انقضا: روز مشخص، یا فقط ماه (آخر ماه در نظر گرفته می‌شود).
public enum ExpiryPrecision { Day, Month }

// تقویمی که کاربر تاریخ را در آن وارد کرده است.
public enum CalendarKind { Gregorian, Jalali }

public enum StockMovementKind { Import, Receipt, Withdrawal, Reversal, Adjustment, Disposal }
