namespace Restaurant.Common.Reservations;

public static class ReservationStatuses
{
    public const string Expired = "Expired";
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
    public const string CheckedIn = "CheckedIn";

    public static bool IsKnown(string status)
    {
        return status is Expired or Pending or Confirmed or Rejected or Cancelled or NoShow or CheckedIn;
    }
}
