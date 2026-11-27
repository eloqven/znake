namespace Znake
{
    internal sealed class WindowsTapSequence
    {
        private const int MaximumTapMilliseconds = 700;
        private int held, count;
        private bool chord;
        private long pressedAt, lastTap;

        public bool Key(bool right, bool down, long milliseconds)
        {
            int flag = right ? 2 : 1;
            if (down)
            {
                if ((held & flag) != 0) return false;
                if (held == 0)
                {
                    pressedAt = milliseconds;
                    chord = false;
                }
                else CancelTaps();
                held |= flag;
                return false;
            }

            if ((held & flag) == 0) return false;
            held &= ~flag;
            if (held != 0) return false;
            if (chord || milliseconds - pressedAt > MaximumTapMilliseconds)
            {
                CancelTaps();
                return false;
            }

            if (pressedAt - lastTap > MaximumTapMilliseconds) count = 0;
            lastTap = milliseconds;
            if (++count < 3) return false;
            Reset();
            return true;
        }

        public void CancelTaps()
        {
            count = 0;
            if (held != 0) chord = true;
        }

        public void Reset()
        {
            held = count = 0;
            chord = false;
            lastTap = 0;
        }
    }
}
