﻿using SharpDX.DirectInput;
using SharpDX.XInput;
using System;
using System.Threading;

namespace x360ce.App.DInput
{
    public partial class DInputHelper : IDisposable
    {

        public DInputHelper()
        {
            _timer = new JocysCom.ClassLibrary.HiResTimer();
            _timer.Elapsed += Timer_Elapsed;
            CombinedXiConencted = new bool[4];
            CombinedXiStates = new State[4];
            LiveXiControllers = new Controller[4];
            LiveXiConnected = new bool[4];
            LiveXiStates = new State[4];
            for (int i = 0; i < 4; i++)
            {
                CombinedXiStates[i] = new State();
                LiveXiControllers[i] = new Controller((UserIndex)i);
                LiveXiStates[i] = new State();
            }
            watch = new System.Diagnostics.Stopwatch();
        }

        public bool Suspended;

        // Where current DInput device state is stored:
        //
        //    UserDevice.Device - DirectInput Device (Joystick)
        //    UserDevice.State - DirectInput Device (JoystickState)
        //
        // Process 1
        // limited to [125, 250, 500, 1000Hz]
        // Lock
        // {
        //    Acquire:
        //    DiDevices - when device is detected.
        //	  DiCapabilities - when device is detected.
        //	  JoStates - from mapped devices.
        //	  DiStates - from converted JoStates.
        //	  XiStates - from converted DiStates
        // }
        //
        // Process 2
        // limited to [30Hz] (only when visible).
        // Lock
        // {
        //	  DiDevices, DiCapabilities, DiStates, XiStates
        //	  Update DInput and XInput forms.
        // }

        public event EventHandler<DInputEventArgs> FrequencyUpdated;
        public event EventHandler<DInputEventArgs> DevicesUpdated;
        public event EventHandler<DInputEventArgs> StatesUpdated;
        public event EventHandler<DInputEventArgs> StatesRetrieved;
        public event EventHandler<DInputEventArgs> UpdateCompleted;
        DirectInput Manager;

        JocysCom.ClassLibrary.HiResTimer _timer;

        //ThreadStart _ThreadStart;
        //Thread _Thread;

        public void Start()
        {
            watch.Restart();
            _timer.Interval = (int)Frequency;
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }


        public Exception LastException = null;

        public bool SkipRefreshAll = false;

        private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (Suspended)
                return;
            try
            {
                RefreshAll();
            }
            catch (Exception ex)
            {
                JocysCom.ClassLibrary.Runtime.LogHelper.Current.WriteException(ex);
                LastException = ex;
            }
        }

        object DiUpdatesLock = new object();

        int? RefreshAllThreadId;

        void RefreshAll()
        {
            lock (DiUpdatesLock)
            {
                // If thread changed then...
                if (RefreshAllThreadId.HasValue && RefreshAllThreadId.Value != Thread.CurrentThread.ManagedThreadId)
                {
                    UnInitDeviceDetector();
                    Manager.Dispose();
                    RefreshAllThreadId = null;
                }
                if (!RefreshAllThreadId.HasValue)
                {
                    Thread.CurrentThread.Name = "RefreshAllThread";
                    RefreshAllThreadId = Thread.CurrentThread.ManagedThreadId;
                    // DIrect input device querying and force feedback updated will run on a separate thread from MainForm therefore
                    // separate windows form must be created on the same thread as the process which will access and update device.
                    // detector.DetectorForm will be used to acquire devices.
                    InitDeviceDetector();
                    Manager = new DirectInput();
                }
                var game = MainForm.Current.CurrentGame;
                // Update information about connected devices.
                UpdateDiDevices();
                // Update JoystickStates from devices.
                UpdateDiStates(game);
                // Update XInput states from Custom DirectInput states.
                UpdateXiStates();
                // Combine XInput states of controllers.
                CombineXiStates();
                // Update virtual devices from combined states.
                UpdateVirtualDevices(game);
                // Retrieve XInput states from XInput controllers.
                RetrieveXiStates();
                // Update pool frequency value and sleep if necessary.
                UpdateDelayFrequency();
                // Fire event.
                var ev = UpdateCompleted;
                if (ev != null)
                    ev(this, new DInputEventArgs());
            }
        }

        /// <summary>
        /// Watch to monitor update frequency.
        /// </summary>
        System.Diagnostics.Stopwatch watch;
        long lastTime;
        long currentTick;
        public long CurrentUpdateFrequency;

        UpdateFrequency Frequency
        {
            get { return _Frequency; }
            set
            {
                _Frequency = value;
                var t = _timer;
                if (t != null && t.Interval != (int)value)
                    t.Interval = (int)value;
            }
        }
        UpdateFrequency _Frequency = UpdateFrequency.ms1_1000Hz;

        void UpdateDelayFrequency()
        {
            // Calculate update frequency.
            currentTick++;
            var currentTime = watch.ElapsedMilliseconds;
            // If one second elapsed then...
            if ((currentTime - lastTime) > 1000)
            {
                CurrentUpdateFrequency = currentTick;
                currentTick = 0;
                lastTime = currentTime;
                var ev = FrequencyUpdated;
                if (ev != null)
                    ev(this, new DInputEventArgs());
            }
        }

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // The bulk of the clean-up code is implemented in Dispose(bool)
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();
                UnInitDeviceDetector();
                if (Manager != null)
                {
                    Manager.Dispose();
                    Manager = null;
                }
                Nefarius.ViGEm.Client.ViGEmClient.DisposeCurrent();
            }
        }

        #endregion

    }
}
