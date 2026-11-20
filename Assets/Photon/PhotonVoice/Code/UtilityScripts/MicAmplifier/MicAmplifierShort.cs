namespace Photon.Voice.Unity.UtilityScripts
{
    public class MicAmplifierShort : IProcessor<short>
    {
        public float AmplificationFactor { get; set; }

        public bool Disabled { get; set; }

        public MicAmplifierShort(float amplificationFactor)
        {
            this.AmplificationFactor = amplificationFactor;
        }

        public short[] Process(short[] buf)
        {
            if (this.Disabled)
            {
                return buf;
            }
            for (int i = 0; i < buf.Length; i++)
            {
                // Saturate before casting: boosted speech must not wrap around Int16.
                float sample = buf[i] * this.AmplificationFactor;
                buf[i] = (short)System.Math.Max(short.MinValue, System.Math.Min(short.MaxValue, sample));
            }
            return buf;
        }

        public void Dispose()
        {

        }
    }
}
