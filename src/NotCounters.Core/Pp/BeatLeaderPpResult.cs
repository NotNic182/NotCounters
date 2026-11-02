namespace NotCounters.Core.Pp
{
    public enum BeatLeaderZeroReason
    {
        None,
        InvalidAccuracy,
        NoFail,
        NoData,
        NonFinite,
    }

    public readonly struct BeatLeaderPpResult
    {
        public BeatLeaderPpResult(double pp, double passPp, double accPp, double techPp, double stars,
            bool isApproximate, bool isLegacy, BeatLeaderZeroReason zeroReason)
        {
            Pp = pp;
            PassPp = passPp;
            AccPp = accPp;
            TechPp = techPp;
            Stars = stars;
            IsApproximate = isApproximate;
            IsLegacy = isLegacy;
            ZeroReason = zeroReason;
        }

        public double Pp { get; }

        public double PassPp { get; }

        public double AccPp { get; }

        public double TechPp { get; }

        public double Stars { get; }

        public bool IsApproximate { get; }

        public bool IsLegacy { get; }

        public BeatLeaderZeroReason ZeroReason { get; }

        internal static BeatLeaderPpResult Zero(double stars, bool approximate, bool legacy, BeatLeaderZeroReason reason) =>
            new BeatLeaderPpResult(0, 0, 0, 0, stars, approximate, legacy, reason);
    }
}
