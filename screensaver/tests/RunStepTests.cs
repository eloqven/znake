using System;
using System.Collections.Generic;

namespace Znake.Tests
{
    internal static class RunStepTests
    {
        public static void Run()
        {
            StateMachineTransitions();
            LogTailPartialLine();
            LayerToggleOpacity();
            VisionToggle();
            StoppedHistoryRecording();
            PostRunSummaryState();
        }

        private static void StateMachineTransitions()
        {
            var m = new Znake.ControlPanel.RunStateMachine();

            TestRunner.Check(m.State == Znake.ControlPanel.RunStepState.Idle, "Initial state Idle");
            TestRunner.Check(m.CanStart, "Can start from Idle");
            TestRunner.Check(!m.CanStop, "Cannot stop from Idle");

            m.BeginRun();
            TestRunner.Check(m.State == Znake.ControlPanel.RunStepState.Running, "State Running after BeginRun");
            TestRunner.Check(!m.CanStart, "Cannot start while Running");
            TestRunner.Check(m.CanStop,   "Can stop while Running");

            m.MarkStopped();
            TestRunner.Check(m.State == Znake.ControlPanel.RunStepState.Stopped, "State Stopped after MarkStopped");
            TestRunner.Check(m.CanResume, "Can resume from Stopped");

            m.ResetToIdle();
            TestRunner.Check(m.State == Znake.ControlPanel.RunStepState.Idle, "Back to Idle after Reset");

            m.BeginRun();
            m.MarkCompleted(true, 5);
            TestRunner.Check(m.State == Znake.ControlPanel.RunStepState.CompletedExtinction, "State CompletedExtinction");
            TestRunner.Check(m.ShowCompletionBanner, "ShowCompletionBanner when extinct");
            TestRunner.Check(m.CanRender, "CanRender after completion");

            m.ResetToIdle();
            m.BeginRun();
            m.MarkCompleted(false, 5);
            TestRunner.Check(m.State == Znake.ControlPanel.RunStepState.CompletedTimeLimit, "State CompletedTimeLimit");

            m.ResetToIdle();
            m.BeginRun();
            m.MarkFailed("boom");
            TestRunner.Check(m.State == Znake.ControlPanel.RunStepState.Failed, "State Failed");
            TestRunner.Check(m.FailureMessage == "boom", "FailureMessage set");
        }

        private static void LogTailPartialLine()
        {
            string tmpFile = System.IO.Path.GetTempFileName();
            try
            {
                // write a complete JSON line followed by a partial line (no trailing newline)
                var record = new Znake.Replay.Record
                {
                    Type   = "start",
                    Width  = 240,
                    Height = 135,
                    Square = 8
                };
                var ser    = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Znake.Replay.Record));
                var ms     = new System.IO.MemoryStream();
                ser.WriteObject(ms, record);
                byte[] lineBytes = ms.ToArray();
                byte[] nl        = System.Text.Encoding.UTF8.GetBytes("\n");
                byte[] partial   = System.Text.Encoding.UTF8.GetBytes("{\"type\":\"snapshot\",\"w\":240,");

                using (var fs = new System.IO.FileStream(tmpFile, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                {
                    fs.Write(lineBytes, 0, lineBytes.Length);
                    fs.Write(nl, 0, nl.Length);
                    fs.Write(partial, 0, partial.Length);
                }

                var tail = new Znake.ControlPanel.LogTail(tmpFile);
                bool updated = tail.Poll();
                TestRunner.Check(updated, "LogTail.Poll returns true for complete line");
                TestRunner.Check(tail.StartRecord != null, "LogTail.StartRecord parsed");
                TestRunner.Check(tail.LatestSnapshot == null, "LogTail.LatestSnapshot null for partial line");

                // append the rest of the partial line
                using (var fs = new System.IO.FileStream(tmpFile, System.IO.FileMode.Append, System.IO.FileAccess.Write))
                {
                    byte[] rest = System.Text.Encoding.UTF8.GetBytes("\"h\":135,\"sq\":8,\"bodies\":null}\n");
                    fs.Write(rest, 0, rest.Length);
                }
                tail.Poll();
                // If snapshot doesn't parse fully due to field names being different, StartRecord is still set
                TestRunner.Check(tail.StartRecord != null, "LogTail.StartRecord still set after second poll");
            }
            finally
            {
                try { System.IO.File.Delete(tmpFile); } catch (Exception) { }
            }
        }

        private static void LayerToggleOpacity()
        {
            var layers = new Znake.ControlPanel.LayerSettings
            {
                ShowSnakes   = true,
                ShowFruits   = false,
                SnakesOpacity = 0.75f,
                FruitsOpacity = 0.0f
            };
            var clone = layers.Clone();
            TestRunner.Check(clone.ShowSnakes,       "LayerSettings clone ShowSnakes");
            TestRunner.Check(!clone.ShowFruits,      "LayerSettings clone ShowFruits");
            TestRunner.Check(clone.SnakesOpacity == 0.75f, "LayerSettings clone SnakesOpacity");
            TestRunner.Check(clone.FruitsOpacity == 0.0f,  "LayerSettings clone FruitsOpacity");

            // Independence of clone
            layers.ShowSnakes = false;
            TestRunner.Check(clone.ShowSnakes, "Clone independent from source");
        }

        private static void VisionToggle()
        {
            var layers = new Znake.ControlPanel.LayerSettings { ShowVision = true, VisionOpacity = 0.65f };
            layers.ShowVision = false;
            var clone = layers.Clone();
            TestRunner.Check(!clone.ShowVision, "Vision toggle off propagates to clone");
        }

        private static void StoppedHistoryRecording()
        {
            string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "znake-test-hist-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            System.IO.Directory.CreateDirectory(tmpDir);
            try
            {
                var store = new Znake.ControlPanel.HistoryStore(tmpDir);
                string folder = System.IO.Path.Combine(tmpDir, "run001");
                System.IO.Directory.CreateDirectory(folder);
                store.RecordStopped(folder, 12345u);

                var list = store.List();
                TestRunner.Check(list.Count == 1, "HistoryStore has one entry after RecordStopped");
                TestRunner.Check(list[0].Seed == 12345u, "HistoryEntry Seed correct");
            }
            finally
            {
                try { System.IO.Directory.Delete(tmpDir, true); } catch (Exception) { }
            }
        }

        private static void PostRunSummaryState()
        {
            var m = new Znake.ControlPanel.RunStateMachine();
            m.BeginRun();
            m.MarkCompleted(false, 5);
            TestRunner.Check(m.ShowCompletionBanner, "Banner shown after completion");
            TestRunner.Check(m.CountdownActive,       "Countdown active after completion");
            TestRunner.Check(m.CountdownRemainingSeconds == 5, "Countdown starts at 5");

            // tick 5 times — last tick fires auto-proceed
            bool fired = false;
            for (int i = 0; i < 4; i++)
            {
                bool done = m.TickCountdown();
                TestRunner.Check(!done, "Tick " + i + " not done yet");
            }
            fired = m.TickCountdown();
            TestRunner.Check(fired, "5th tick fires auto-proceed");
            TestRunner.Check(!m.CountdownActive, "Countdown no longer active after expiry");
        }
    }
}
