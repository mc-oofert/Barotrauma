﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    class OscillatorComponent : ItemComponent
    {
        public enum WaveType
        {
            Pulse,
            Sawtooth,
            Sine,
            Square,
            Triangle,
        }

        private float frequency;

        private float phase;

        [InGameEditable, Serialize(WaveType.Pulse, true, description: "What kind of a signal the item outputs." +
            " Pulse: periodically sends out a signal of 1." +
            " Sawtooth: sends out a periodic wave that increases linearly from 0 to 1." +
            " Sine: sends out a sine wave oscillating between -1 and 1." +
            " Square: sends out a signal that alternates between 0 and 1." +
            " Triangle: sends out a wave that alternates between increasing linearly from -1 to 1 and decreasing from 1 to -1.",
                                   alwaysUseInstanceValues: true)]
        public WaveType OutputType
        {
            get;
            set;
        }

        [InGameEditable(DecimalCount = 2), Serialize(1.0f, true, description: "How fast the signal oscillates, or how fast the pulses are sent (in Hz).", alwaysUseInstanceValues: true)]
        public float Frequency
        {
            get { return frequency; }
            set
            {
                //capped to 240 Hz (= 4 signals per frame) to prevent players 
                //from wrecking the performance by setting the value too high
                frequency = MathHelper.Clamp(value, 0.0f, 240.0f);
            }
        }

        public OscillatorComponent(Item item, XElement element) : 
            base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            switch (OutputType)
            {
                case WaveType.Pulse:
                    if (frequency <= 0.0f) return;

                    phase += deltaTime;
                    float pulseInterval = 1.0f / frequency;
                    while (phase >= pulseInterval)
                    {
                        item.SendSignal("1", "signal_out");
                        phase -= pulseInterval;
                    }
                    break;
                case WaveType.Sawtooth:
                    phase = (phase + deltaTime * frequency) % 1.0f;
                    item.SendSignal(phase.ToString(CultureInfo.InvariantCulture), "signal_out");
                    break;
                case WaveType.Square:
                    phase = (phase + deltaTime * frequency) % 1.0f;
                    item.SendSignal(phase < 0.5f ? "0" : "1", "signal_out");
                    break;
                case WaveType.Sine:
                    phase = (phase + deltaTime * frequency) % 1.0f;
                    item.SendSignal(Math.Sin(phase * MathHelper.TwoPi).ToString(CultureInfo.InvariantCulture), "signal_out");
                    break;
                case WaveType.Triangle:
                    phase = (phase + deltaTime * frequency) % 1.0f;
                    float output = 4.0f * MathF.Abs(MathUtils.PositiveModulo(phase - 0.25f, 1.0f) - 0.5f) - 1.0f;
                    item.SendSignal(output.ToString(CultureInfo.InvariantCulture), "signal_out");
                    break;
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            switch (connection.Name)
            {
                case "set_frequency":
                case "frequency_in":
                    float newFrequency;
                    if (float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out newFrequency))
                    {
                        Frequency = newFrequency;
                    }
                    IsActive = true;
                    break;
                case "set_outputtype":
                case "set_wavetype":
                    WaveType newOutputType;
                    if (Enum.TryParse(signal.value, out newOutputType))
                    {
                        OutputType = newOutputType;
                    }
                    break;
            }
        }
    }
}
