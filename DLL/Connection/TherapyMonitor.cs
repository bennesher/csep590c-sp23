using System.Diagnostics;
using CircularBuffer;

namespace DeviceInterface.Connection
{
    /// <summary>
    ///     Monitors the streaming data to determine when therapy needs to be applied,
    ///     and applies the therapy if it is enabled for the connection.
    ///     Notification of state change is delivered by the <see cref="TherapyStatus"/>
    ///     event.
    /// </summary>
    internal class TherapyMonitor
    {
        private const int EVALUATION_INCREMENT = DiagnosticClassifier.CLASSIFIER_WINDOW_DATAPOINTS / 4;
        private const int TIME_GAP_ALLOWED_MS = 10;
        private const double SEIZURE_START_CONFIDENCE_THRESHOLD = 1;
        private const double SEIZURE_OVER_CONFIDENCE_THRESHOLD = 3;
        private const int RETRY_DELAY_MS = 50;

        private readonly DeviceConnection _connection;
        private readonly CircularBuffer<double> _buffer = new(DiagnosticClassifier.CLASSIFIER_WINDOW_DATAPOINTS);
        private readonly DiagnosticClassifier _classifier = new();
        private int _counter = 0;
        private long _lastTimestamp = 0;
        private double _changeConfidence = 0;
        private bool _disposed = false;

        /// <summary>
        ///     Initialize the monitor
        /// </summary>
        /// <param name="connection">The connection we're monitoring therapy on</param>
        public TherapyMonitor(DeviceConnection connection)
        {
            _connection = connection;
            connection.StreamingData += OnData;
            connection.TherapyEnabledChanged += OnTherapyEnabledChanged;
        }

        /// <summary>
        ///     Process one streaming data point, adding it to the buffer. Once we
        ///     have a full window worth of data, then every <see cref="EVALUATION_INCREMENT"/>
        ///     of data points we evaluate the current window. And if a timestamp gap
        ///     (in device time) of more than <see cref="TIME_GAP_ALLOWED_MS"/> is
        ///     observed, a data discontinuity is assumed, the buffer is reset.
        /// </summary>
        /// <param name="sender">The source of the event</param>
        /// <param name="data">The data point</param>
        private void OnData(object? sender, StreamingData data)
        {
            if (Math.Abs(data.Timestamp - _lastTimestamp) > TIME_GAP_ALLOWED_MS)
            {
                _buffer.Clear();
                _counter = 0;
            }
            _lastTimestamp = data.Timestamp;
            _buffer.PushBack(data.Data);
            if (++_counter >= DiagnosticClassifier.CLASSIFIER_WINDOW_DATAPOINTS 
                && _counter % EVALUATION_INCREMENT == 0)
            {
                var window = _buffer.ToArray();
                Task.Run(() => EvaluateWindow(window));
            }
        }

        /// <summary>
        ///     Evaluate the current window of data, to determine required action
        /// </summary>
        /// <param name="window"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private void EvaluateWindow(double[] window)
        {
            var evaluation = _classifier.Classify(window);

            IsInSeizure = evaluation.Classification;

            var prevTherapy = IsTherapyNeeded;
            if (prevTherapy != evaluation.Classification)
            {
                // It appears that the patient's state is changing
                _changeConfidence += evaluation.Confidence;
                switch (prevTherapy)
                {
                    case true when _changeConfidence >= SEIZURE_OVER_CONFIDENCE_THRESHOLD:
                        IsTherapyNeeded = false;
                        if (IsTherapyActive) StopTherapy();
                        break;
                    case false when _changeConfidence >= SEIZURE_START_CONFIDENCE_THRESHOLD:
                        IsTherapyNeeded = true;
                        if (_connection.TherapyEnabled) StartTherapy();
                        break;
                }

                TherapyStatus?.Invoke(this, new TherapyEventArgs(IsTherapyNeeded));
            }
            else
            {
                // It does not look like a change is needed at the present time
                _changeConfidence -= evaluation.Confidence;
                if (_changeConfidence < 0)
                    _changeConfidence = 0;
            }

            // Share the classification (the connection takes care of async dispatch)
            _connection.RaiseSeizureStatusEvent(evaluation);
        }

        /// <summary>
        ///     Signals a change in the diagnostic status
        /// </summary>
        internal event EventHandler<TherapyEventArgs>? TherapyStatus;

        /// <summary>
        ///     Is the "patient" currently experiencing a seizure?
        /// </summary>
        public bool IsInSeizure { get; private set; } = false;

        /// <summary>
        ///     Has the system determined that therapy should be active?
        /// </summary>
        public bool IsTherapyNeeded { get; private set; } = false;

        /// <summary>
        ///     Is therapy actually being applied?
        /// </summary>
        internal bool IsTherapyActive { get; private set; } = false;

        /// <summary>
        ///     Handles updates to the therapy control
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="value">The new state</param>
        private void OnTherapyEnabledChanged(object? sender, bool value)
        {
            switch (value)
            {
                case true when IsTherapyNeeded:
                    StartTherapy();
                    break;
                case false when IsTherapyActive:
                    StopTherapy();
                    break;
            }
        }

        /// <summary>
        ///     Activate the therapy on the device
        /// </summary>
        private void StartTherapy()
        {
            Debug.WriteLine("Starting Therapy");

            Task.Run(() =>
            {
                // Make sure this is the right thing to do
                if (_disposed || IsTherapyActive || !IsTherapyNeeded || !_connection.TherapyEnabled) return;

                try
                {
                    Debug.WriteLine("Sending START command");
                    var result = _connection.SendCommand(OpCode.StartTherapy);
                    if (result is null or DeviceErrorCode.ERR_ALREADY_DOING_THERAPY)
                    {
                        IsTherapyActive = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception encountered starting therapy: " + ex);
                }

                Debug.WriteLine("Start Therapy failed");

                // If we made it here, something went wrong, so we retry
                // Wait a bit so we're not spamming the device
                Thread.Sleep(TimeSpan.FromMilliseconds(RETRY_DELAY_MS));

                // We still need to apply the therapy, so keep trying.
                StartTherapy();
            });
        }

        /// <summary>
        ///     Deactivate the therapy on the device
        /// </summary>
        private void StopTherapy()
        {
            Debug.WriteLine("Stopping Therapy");
            Task.Run(() =>
            {
                // Make sure this is the right thing to do
                if (_disposed || !IsTherapyActive || IsTherapyNeeded) return;

                try
                {
                    Debug.WriteLine("Sending STOP command");
                    var result = _connection.SendCommand(OpCode.StopTherapy);
                    if (result is null or DeviceErrorCode.ERR_ALREADY_STOP_THERAPY)
                    {
                        IsTherapyActive = false;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception encountered stopping therapy: " + ex);
                }

                Debug.WriteLine("Stop therapy failed");

                // If we made it here, something went wrong, so we'll retry
                // But we'll wait a bit so we're not spamming the device
                Thread.Sleep(TimeSpan.FromMilliseconds(RETRY_DELAY_MS));

                // We still need to apply the therapy, so keep trying.
                StopTherapy();
            });
        }

        /// <summary>
        ///     Clean up event handlers
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _connection.TherapyEnabledChanged -= OnTherapyEnabledChanged;
                _connection.StreamingData -= OnData;
            }
        }
    }

    /// <summary>
    ///     Used to report the currently needed therapy status
    /// </summary>
    internal class TherapyEventArgs: EventArgs
    {
        public readonly bool IsTherapyNeeded;

        public TherapyEventArgs(bool isTherapyNeeded)
        {
            IsTherapyNeeded = isTherapyNeeded;
        }
    }
}
