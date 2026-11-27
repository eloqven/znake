using System;

namespace Znake.ControlPanel
{
    internal enum RunStepState
    {
        Idle,
        Running,
        Stopped,
        CompletedExtinction,
        CompletedTimeLimit,
        Failed
    }

    internal sealed class RunStateMachine
    {
        public RunStepState State { get; private set; }
        public bool CountdownActive { get; private set; }
        public int CountdownRemainingSeconds { get; private set; }
        public string FailureMessage { get; private set; }

        public bool CanStart { get { return State == RunStepState.Idle || State == RunStepState.Stopped; } }
        public bool CanStop { get { return State == RunStepState.Running; } }
        public bool CanNewRun { get { return State != RunStepState.Running; } }
        public bool CanRender { get { return State == RunStepState.Stopped || State == RunStepState.CompletedExtinction || State == RunStepState.CompletedTimeLimit; } }
        public bool CanResume { get { return State == RunStepState.Stopped || State == RunStepState.CompletedTimeLimit; } }
        public bool ShowCompletionBanner { get { return State == RunStepState.CompletedExtinction || State == RunStepState.CompletedTimeLimit; } }

        public void BeginRun()
        {
            if (State == RunStepState.Running)
                throw new InvalidOperationException("A run is already active.");
            State = RunStepState.Running;
            CountdownActive = false;
            CountdownRemainingSeconds = 0;
            FailureMessage = null;
        }

        public void MarkStopped()
        {
            State = RunStepState.Stopped;
            CountdownActive = false;
            CountdownRemainingSeconds = 0;
        }

        public void MarkCompleted(bool extinct, int countdownSeconds = 5)
        {
            State = extinct ? RunStepState.CompletedExtinction : RunStepState.CompletedTimeLimit;
            CountdownActive = countdownSeconds > 0;
            CountdownRemainingSeconds = Math.Max(0, countdownSeconds);
        }

        public void MarkFailed(string message)
        {
            State = RunStepState.Failed;
            CountdownActive = false;
            CountdownRemainingSeconds = 0;
            FailureMessage = message;
        }

        public bool TickCountdown()
        {
            if (!CountdownActive) return false;
            if (CountdownRemainingSeconds > 0) CountdownRemainingSeconds--;
            if (CountdownRemainingSeconds == 0)
            {
                CountdownActive = false;
                return true;
            }
            return false;
        }

        public void StayOnRun()
        {
            CountdownActive = false;
            CountdownRemainingSeconds = 0;
        }

        public void ResetToIdle()
        {
            if (State == RunStepState.Running)
                throw new InvalidOperationException("Cannot reset while running.");
            State = RunStepState.Idle;
            CountdownActive = false;
            CountdownRemainingSeconds = 0;
            FailureMessage = null;
        }
    }
}
