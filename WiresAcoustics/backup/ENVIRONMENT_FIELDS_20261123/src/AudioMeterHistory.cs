using System.Threading;

namespace WiresAcoustics
{
    // Per-source audio diagnostic packets, published by the audio callback.
    // This is NOT a sound recording. It stores only pre/post DSP energies
    // for COMPLETE callbacks. The main thread can read a consistent packet
    // without locking or blocking the audio thread. Allocation is performed
    // on the game thread only when debug metering is enabled.
    internal sealed class AudioMeterHistory
    {
        private const int Capacity = 512;
        private const int Mask = Capacity - 1;

        private struct Packet
        {
            internal long Version;
            internal double InputEnergy, OutputEnergy;
            internal int Samples;
        }

        private readonly Packet[] m_packets = new Packet[Capacity];
        private long m_next, m_published, m_consumed;

        // Called only from the corresponding AudioSource's audio callback.
        internal void Publish(double inputEnergy, double outputEnergy, int samples)
        {
            long seq = Interlocked.Increment(ref m_next);
            int i = (int)(seq & Mask);
            // Mark the slot as being written BEFORE the payload. A reader
            // never accepts a partially overwritten older packet.
            Volatile.Write(ref m_packets[i].Version, -seq);
            m_packets[i].InputEnergy = inputEnergy;
            m_packets[i].OutputEnergy = outputEnergy;
            m_packets[i].Samples = samples;
            Volatile.Write(ref m_packets[i].Version, seq);
            Volatile.Write(ref m_published, seq);
        }

        // Called at the existing three-second main-thread debug report.
        internal void Read(out double input, out double output,
                           out long samples, out long dropped)
        {
            input = output = 0.0;
            samples = dropped = 0;
            long latest = Volatile.Read(ref m_published);
            if (latest <= m_consumed) return;
            long from = m_consumed + 1;
            if (latest - from >= Capacity)
            {
                long newFrom = latest - Capacity + 1;
                dropped += newFrom - from;
                from = newFrom;
            }
            for (long seq = from; seq <= latest; seq++)
            {
                int i = (int)(seq & Mask);
                if (Volatile.Read(ref m_packets[i].Version) != seq)
                {
                    dropped++;
                    continue;
                }
                double inValue = m_packets[i].InputEnergy;
                double outValue = m_packets[i].OutputEnergy;
                int count = m_packets[i].Samples;
                if (Volatile.Read(ref m_packets[i].Version) != seq)
                {
                    dropped++;
                    continue;
                }
                input += inValue;
                output += outValue;
                samples += count;
            }
            m_consumed = latest;
        }
    }
}
