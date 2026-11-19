using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Jus
{
    /// <summary>Interroge les sources en arrière-plan, jamais sur le fil de l'interface.</summary>
    sealed class BatteryMonitor : IDisposable
    {
        const int TickMs = 4000;   // un appareil éteint disparaît en quelques secondes
        const int SettleMs = 700;  // Windows finit d'installer ou de retirer l'appareil

        sealed class Scheduled
        {
            public IBatterySource Source;
            public int PeriodMs, LastPoll;
            public bool Polled;
            public List<BatteryReading> Readings = new List<BatteryReading>();
            public Scheduled(IBatterySource source, int periodMs) { Source = source; PeriodMs = periodMs; }
        }

        // Les sources qui réveillent un appareil sans fil sont interrogées moins souvent.
        readonly Scheduled[] sources =
        {
            new Scheduled(new LogitechSource(), 10000),
            new Scheduled(new SteelSeriesSource(), 4000), new Scheduled(new RazerSource(), 30000),
            new Scheduled(new PlayStationSource(), 12000), new Scheduled(new XInputSource(), 0),
            new Scheduled(new BluetoothSource(), 0), new Scheduled(new StandardHidSource(), 30000),
        };
        readonly Dictionary<string, BatteryReading> known = new Dictionary<string, BatteryReading>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        volatile bool stopping, refreshAll, devicesChanged;
        Thread thread;

        public event Action<List<BatteryReading>> Updated;

        public void Start()
        {
            thread = new Thread(Loop) { IsBackground = true, Name = "Jus.Monitor" };
            thread.Start();
        }

        public void Refresh() { refreshAll = true; wake.Set(); }

        /// <summary>Un appareil vient d'être branché ou retiré.</summary>
        public void DevicesChanged() { devicesChanged = true; wake.Set(); }

        void Loop()
        {
            string last = null;
            while (!stopping)
            {
                bool changed = devicesChanged, all = refreshAll;
                devicesChanged = refreshAll = false;
                if (changed) Thread.Sleep(SettleMs);
                List<BatteryReading> list = Poll(null, changed, all || changed);
                string state = string.Join("|", list.Select(r => $"{r.Id},{r.Name},{r.Text},{r.Charging},{r.Patchable}"));
                if (!stopping && state != last)
                {
                    last = state;
                    Updated?.Invoke(list);
                }
                wake.WaitOne(TickMs);
            }
        }

        /// <summary>Un passage. Avec un journal, interroge tout et détaille ce que dit chaque source.</summary>
        public List<BatteryReading> Poll(TextWriter log, bool deviceChanged = false, bool everything = true)
        {
            List<HidInfo> hid;
            try { hid = Hid.Enumerate(); }
            catch (Exception e) { log?.WriteLine("HID : " + e.Message); hid = new List<HidInfo>(); }

            foreach (Scheduled s in sources)
            {
                if (!everything && s.Polled && Environment.TickCount - s.LastPoll < s.PeriodMs) continue;
                log?.WriteLine("[" + s.Source.Name + "]");
                var fresh = new List<BatteryReading>();
                try { s.Source.Poll(fresh, hid, log); }
                catch (Exception e) { log?.WriteLine("  erreur : " + e.Message); }
                s.Readings = fresh;
                s.Polled = true;
                s.LastPoll = Environment.TickCount;
            }

            // Le même appareil peut remonter par deux chemins : on garde la première source.
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>();
            foreach (BatteryReading r in sources.SelectMany(s => s.Readings))
            {
                if (!names.Add(r.Name)) continue;
                ids.Add(r.Id);
                known[r.Id] = r;
            }

            // Une souris Logitech en veille ne répond plus sans être partie : on lui laisse un peu de temps.
            bool logitechReceiver = hid.Any(h => h.Vid == 0x046D && h.UsagePage == 0xFF00);
            foreach (string id in known.Keys.ToList())
            {
                if (ids.Contains(id)) continue;
                BatteryReading r = known[id];
                int tolerance = r.Source == "Logitech" && logitechReceiver ? 2 : deviceChanged ? 0 : 1;
                if (++r.Missed > tolerance) known.Remove(id);
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
