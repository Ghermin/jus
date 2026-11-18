using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Jus
{
    /// <summary>Interroge toutes les sources en arrière-plan, jamais sur le fil de l'interface.</summary>
    sealed class BatteryMonitor : IDisposable
    {
        const int PollMs = 60000;
        const int MaxMissed = 2; // une lecture ratée ne fait pas disparaître l'appareil

        readonly IBatterySource[] sources =
        {
            new SystemSource(), new LogitechSource(), new SteelSeriesSource(), new RazerSource(),
            new PlayStationSource(), new XInputSource(), new BluetoothSource(), new StandardHidSource(),
        };
        readonly Dictionary<string, BatteryReading> known = new Dictionary<string, BatteryReading>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        volatile bool stopping;
        Thread thread;

        public event Action<List<BatteryReading>> Updated;

        public void Start()
        {
            thread = new Thread(Loop) { IsBackground = true, Name = "Jus.Monitor" };
            thread.Start();
        }

        public void Refresh() => wake.Set();

        void Loop()
        {
            while (!stopping)
            {
                List<BatteryReading> list = Poll(null);
                if (!stopping) Updated?.Invoke(list);
                wake.WaitOne(PollMs);
            }
        }

        /// <summary>Un passage complet. Avec un journal, détaille ce que dit chaque source.</summary>
        public List<BatteryReading> Poll(TextWriter log)
        {
            List<HidInfo> hid;
            try { hid = Hid.Enumerate(); }
            catch (Exception e) { log?.WriteLine("HID : " + e.Message); hid = new List<HidInfo>(); }

            var fresh = new List<BatteryReading>();
            foreach (IBatterySource source in sources)
            {
                log?.WriteLine("[" + source.Name + "]");
                try { source.Poll(fresh, hid, log); }
                catch (Exception e) { log?.WriteLine("  erreur : " + e.Message); }
            }

            // Le même appareil peut remonter par deux chemins : on garde la première source.
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>();
            foreach (BatteryReading r in fresh)
            {
                if (!names.Add(r.Name)) continue;
                ids.Add(r.Id);
                known[r.Id] = r;
            }
            foreach (string id in known.Keys.ToList())
            {
                if (ids.Contains(id)) continue;
                if (++known[id].Missed > MaxMissed) known.Remove(id);
            }
            return known.Values.OrderBy(r => r.Kind).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public void Dispose()
        {
            stopping = true;
            wake.Set();
        }
    }
}
