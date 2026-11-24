using System;

namespace Znake
{
    internal static class Daylight
    {
        private const double Radians = Math.PI / 180;

        public static double NightAt(DateTimeOffset when, double? latitude, double? longitude)
        {
            if (!latitude.HasValue || !longitude.HasValue) return 0;
            DateTime utc = when.UtcDateTime;
            double hour = utc.TimeOfDay.TotalHours;
            int days = DateTime.IsLeapYear(utc.Year) ? 366 : 365;
            // NOAA's fractional-year approximation gives declination and solar time from UTC.
            double yearAngle = 2 * Math.PI / days * (utc.DayOfYear - 1 + (hour - 12) / 24);
            double equation = 229.18 * (0.000075 + 0.001868 * Math.Cos(yearAngle) - 0.032077 * Math.Sin(yearAngle)
                - 0.014615 * Math.Cos(2 * yearAngle) - 0.040849 * Math.Sin(2 * yearAngle));
            double declination = 0.006918 - 0.399912 * Math.Cos(yearAngle) + 0.070257 * Math.Sin(yearAngle)
                - 0.006758 * Math.Cos(2 * yearAngle) + 0.000907 * Math.Sin(2 * yearAngle)
                - 0.002697 * Math.Cos(3 * yearAngle) + 0.001480 * Math.Sin(3 * yearAngle);
            double minutes = (hour * 60 + equation + 4 * longitude.Value + 1440) % 1440;
            double angle = Math.Abs(minutes / 4 - 180) * Radians;
            double sinProduct = Math.Sin(latitude.Value * Radians) * Math.Sin(declination);
            double cosProduct = Math.Cos(latitude.Value * Radians) * Math.Cos(declination);
            double rising = (Math.Cos(90.833 * Radians) - sinProduct) / cosProduct;
            if (rising <= -1) return 0; // Midnight sun.
            if (rising >= 1)
            {
                // Polar night has no sunrise. Use solar altitude to measure its depth.
                double altitude = Math.Asin(Numbers.Signed(sinProduct + cosProduct * Math.Cos(angle))) / Radians;
                return Numbers.Unit((-altitude - 0.833) / 17.167);
            }
            double daylightArc = Math.Acos(rising);
            return Numbers.Unit((angle - daylightArc) / (Math.PI - daylightArc));
        }
    }
}
