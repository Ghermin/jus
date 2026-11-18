using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Jus
{
    enum DeviceKind { Mouse, Keyboard, Headset, Earbuds, Gamepad, Laptop, Other }

    sealed class BatteryReading
    {
        public string Id, Name, Source;
        public DeviceKind Kind = DeviceKind.Other;
        public int Percent;
        public bool Charging, Approximate;
        /// <summary>Appareil bien présent, mais qui ne donne pas son niveau à Windows.</summary>
        public bool NoLevel;
        /// <summary>Reliée par un câble : une prise remplace le pourcentage.</summary>
        public bool PluggedIn;
        public int Missed;
        public bool IsLow => !NoLevel && Percent <= 20 && !Charging;
        public string Text => PluggedIn ? "branchée" : NoLevel ? "—" : (Approximate ? "~" : "") + Percent + "%";
    }

    interface IBatterySource
    {
        string Name { get; }
        void Poll(List<BatteryReading> output, List<HidInfo> hid, TextWriter log);
    }

    static class Naming
    {
        /// <summary>Devine le type d'appareil d'après son nom.</summary>
        public static DeviceKind Guess(string name, DeviceKind fallback = DeviceKind.Other)
        {
            string n = name.ToLowerInvariant();
            if (Regex.IsMatch(n, @"buds|earbud|airpods|écouteur|ecouteur|\bwf-|freebuds|galaxy buds")) return DeviceKind.Earbuds;
            if (Regex.IsMatch(n, @"headset|headphone|casque|arctis|\bwh-|\bqc\d|quietcomfort|hyperx cloud|\bg\d{3} ?(lightspeed|wireless)? ?headset|kraken|barracuda|blackshark")) return DeviceKind.Headset;
            if (Regex.IsMatch(n, @"controller|manette|gamepad|dualsense|dualshock|xbox|8bitdo|joy-con")) return DeviceKind.Gamepad;
            if (Regex.IsMatch(n, @"keyboard|clavier|\bkeys\b|\bmx keys\b")) return DeviceKind.Keyboard;
            if (Regex.IsMatch(n, @"mouse|souris|\bmx master|\bmx anywhere|trackball|viper|deathadder|basilisk|superlight|\bg\s?pro\b")) return DeviceKind.Mouse;
            return fallback;
        }

        /// <summary>Retire ce que Windows ajoute au nom (profil Bluetooth, nom du propriétaire).</summary>
        public static string Clean(string name)
        {
            string n = (name ?? "").Trim();
            n = Regex.Replace(n, @"^Transport Avrcp\s+", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s+(Hands-?Free( AG)?( Audio)?|Stereo|A2DP( SNK| Sink)?|Avrcp Transport|AVRCP)\s*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s+appartenant à .*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"^.{1,30}['’]s\s+", "");
            return n.Trim();
        }
    }

    // ---------------------------------------------------------------- Bluetooth (classique et LE)

    /// <summary>Tout ce que Windows connaît en Bluetooth : écouteurs, casques, souris, claviers…</summary>
    sealed class BluetoothSource : IBatterySource
    {
        public string Name => "Bluetooth";
        static Native.DEVPROPKEY batteryKey = new Native.DEVPROPKEY("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
        static Native.DEVPROPKEY connectedKey = new Native.DEVPROPKEY("83DA6326-97A6-4088-9453-A1923F573B29", 15);
        static Native.DEVPROPKEY nameKey = new Native.DEVPROPKEY("A45C254E-DF1C-4EFD-8020-67D146A850E0", 14); // FriendlyName
        static Native.DEVPROPKEY descKey = new Native.DEVPROPKEY("A45C254E-DF1C-4EFD-8020-67D146A850E0", 2);  // DeviceDesc

        public void Poll(List<BatteryReading> output, List<HidInfo> hid, TextWriter log)
        {
            var seen = new HashSet<string>();
            foreach (string enumerator in new[] { "BTHENUM", "BTHLE", "BTHLEDEVICE" })
            {
                IntPtr set = Native.SetupDiGetClassDevs(IntPtr.Zero, enumerator, IntPtr.Zero, Native.DIGCF_ALLCLASSES | Native.DIGCF_PRESENT);
                if (set == new IntPtr(-1)) continue;
                try
                {
                    var info = new Native.SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(Native.SP_DEVINFO_DATA)) };
                    for (int i = 0; Native.SetupDiEnumDeviceInfo(set, i, ref info); i++)
                    {
                        byte[] value = Property(set, ref info, batteryKey);
                        if (value == null || value.Length < 1 || value[0] > 100) continue;
                        string id = InstanceId(set, ref info);
                        string address = Address(id);
                        byte[] connected = Property(set, ref info, connectedKey);
                        bool isConnected = connected == null || connected.Length == 0 || connected[0] != 0;
                        log?.WriteLine($"  bluetooth {address} : {value[0]} %, connecté = {isConnected}");
                        if (!isConnected || !seen.Add(address)) continue;
                        string name = Naming.Clean(Text(set, ref info, nameKey) ?? Text(set, ref info, descKey) ?? "Appareil Bluetooth");
                        output.Add(new BatteryReading { Id = "bt:" + address, Name = name, Kind = Naming.Guess(name, DeviceKind.Headset), Percent = value[0], Source = Name });
                    }
                }
                finally { Native.SetupDiDestroyDeviceInfoList(set); }
            }
        }

        static string Address(string id)
        {
            Match m = Regex.Match(id ?? "", @"(?:DEV_|&)([0-9A-F]{12})(?:_C[0-9A-F]+|\\|$)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : id;
        }

        static byte[] Property(IntPtr set, ref Native.SP_DEVINFO_DATA info, Native.DEVPROPKEY key)
        {
            byte[] buffer = new byte[512];
            return Native.SetupDiGetDeviceProperty(set, ref info, ref key, out _, buffer, buffer.Length, out int n, 0)
                ? buffer.Take(n).ToArray() : null;
        }

        static string Text(IntPtr set, ref Native.SP_DEVINFO_DATA info, Native.DEVPROPKEY key)
        {
            byte[] b = Property(set, ref info, key);
            return b == null ? null : Encoding.Unicode.GetString(b).TrimEnd('\0');
        }

        static string InstanceId(IntPtr set, ref Native.SP_DEVINFO_DATA info)
        {
            var sb = new StringBuilder(512);
            return Native.SetupDiGetDeviceInstanceId(set, ref info, sb, sb.Capacity, out _) ? sb.ToString() : "";
        }
    }

    // ---------------------------------------------------------------- Manettes Xbox et compatibles

    sealed class XInputSource : IBatterySource
    {
        public string Name => "XInput";

        public void Poll(List<BatteryReading> output, List<HidInfo> hid, TextWriter log)
        {
            for (int user = 0; user < 4; user++)
            {
                Native.XINPUT_BATTERY_INFORMATION b;
                try { if (Native.XInputGetBatteryInformation(user, 0, out b) != 0) continue; }
                catch (DllNotFoundException) { return; }
                catch (EntryPointNotFoundException) { return; }
                log?.WriteLine($"  xinput {user} : type {b.BatteryType}, niveau {b.BatteryLevel}");
                if (b.BatteryType == 0) continue; // déconnectée
                if (b.BatteryType == 1) // filaire pour Windows : présente, mais sans niveau
                {
                    // Le récepteur 8BitDo (310A) se dit filaire lui aussi : seul le câble ajoute le périphérique 301C.
                    bool receiver = hid.Any(h => h.Vid == 0x2DC8 && h.Pid == 0x310A);
                    bool cable = hid.Any(h => h.Vid == 0x2DC8 && h.Pid == 0x301C);
                    output.Add(new BatteryReading { Id = "xinput:" + user, Name = PadName(hid, user), Kind = DeviceKind.Gamepad, NoLevel = true, PluggedIn = !receiver || cable, Source = Name });
                    continue;
                }
                int[] levels = { 10, 35, 65, 100 };
                output.Add(new BatteryReading
                {
                    Id = "xinput:" + user, Name = PadName(hid, user), Kind = DeviceKind.Gamepad,
                    Percent = levels[Math.Min((int)b.BatteryLevel, 3)], Approximate = true, Source = Name,
                });
            }
        }

        /// <summary>Le vrai nom si une seule manette XInput est branchée : XInput ne dit pas laquelle est laquelle.</summary>
        static string PadName(List<HidInfo> hid, int user)
        {
            List<HidInfo> pads = hid.Where(h => h.UsagePage == 1 && h.Usage == 5 && h.Path.IndexOf("ig_", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (pads.Count == 1 && pads[0].Product.Length > 0)
                return Regex.Replace(pads[0].Product, @"^Controller \((.*)\)$", "$1").Replace(" Wireless Controller", "");
            return user == 0 ? "Manette" : "Manette " + (user + 1);
        }
    }

    // ---------------------------------------------------------------- DualSense et DualShock 4

    sealed class PlayStationSource : IBatterySource
    {
        public string Name => "PlayStation";
        const ushort Sony = 0x054C;

        public void Poll(List<BatteryReading> output, List<HidInfo> hid, TextWriter log)
        {
            foreach (HidInfo info in hid.Where(h => h.Vid == Sony && h.UsagePage == 1 && h.Usage == 5))
            {
                bool dualSense = info.Pid == 0x0CE6 || info.Pid == 0x0DF2;
                bool ds4 = info.Pid == 0x05C4 || info.Pid == 0x09CC || info.Pid == 0x0BA0;
                if (!dualSense && !ds4) continue;
                using (HidDevice dev = HidDevice.Open(info))
                {
                    if (dev == null) continue;
                    bool bluetooth = info.IsBluetooth;
                    if (bluetooth)
                    {
                        // Lire ce rapport fait passer la manette en rapports complets, ceux qui portent la batterie.
                        byte[] feature = new byte[Math.Max((int)info.FeatureLen, 64)];
                        feature[0] = (byte)(dualSense ? 0x05 : 0x02);
                        dev.GetFeature(feature);
                    }
                    byte[] buffer = new byte[Math.Max((int)info.InputLen, 64)];
                    long end = Environment.TickCount + 1500;
                    while (Environment.TickCount < end)
                    {
                        int n = dev.Read(buffer, 400);
                        if (n <= 0) continue;
                        if (!Parse(buffer, n, dualSense, out int percent, out bool charging)) continue;
                        string name = dualSense ? (info.Pid == 0x0DF2 ? "DualSense Edge" : "DualSense") : "DualShock 4";
                        output.Add(new BatteryReading { Id = "ps:" + info.Path, Name = name, Kind = DeviceKind.Gamepad, Percent = percent, Charging = charging, Source = Name });
                        break;
                    }
                }
            }
        }

        static bool Parse(byte[] b, int n, bool dualSense, out int percent, out bool charging)
        {
            percent = 0; charging = false;
            if (dualSense)
            {
                int at = b[0] == 0x31 ? 54 : b[0] == 0x01 && n >= 64 ? 53 : -1;
                if (at < 0 || at >= n) return false;
                int level = b[at] & 0x0F, state = b[at] >> 4;
                if (state > 2) return false; // erreur de charge ou température : valeur non fiable
                charging = state == 1;
                percent = state == 2 ? 100 : Math.Min(level * 10 + 5, 100);
                return true;
            }
            int pos = b[0] == 0x11 ? 32 : b[0] == 0x01 && n >= 64 ? 30 : -1;
            if (pos < 0 || pos >= n) return false;
            int raw = b[pos] & 0x0F;
            charging = (b[pos] & 0x10) != 0 && raw < 11;
            percent = raw >= 10 ? 100 : raw * 10 + 5;
            return true;
        }
    }

    // ---------------------------------------------------------------- Logitech (HID++ 2.0 : Bolt, Unifying, Lightspeed)

    sealed class LogitechSource : IBatterySource
    {
        public string Name => "Logitech";
        const ushort Logitech = 0x046D;
        static byte softwareId;

        sealed class Slot { public string Name; public DeviceKind Kind; public byte Feature; public ushort FeatureId; }
        readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>();

        public void Poll(List<BatteryReading> output, List<HidInfo> hid, TextWriter log)
        {
            foreach (HidInfo info in hid.Where(h => h.Vid == Logitech && (h.UsagePage == 0xFF00 || h.UsagePage == 0xFF43) && h.Usage == 0x0002 && h.OutputLen >= 20))
            {
                using (HidDevice dev = HidDevice.Open(info))
                {
                    if (dev == null) continue;
                    bool receiver = info.Product.IndexOf("receiver", StringComparison.OrdinalIgnoreCase) >= 0
                                 || info.Product.IndexOf("USB Receiver", StringComparison.OrdinalIgnoreCase) >= 0
                                 || new ushort[] { 0xC52B, 0xC532, 0xC534, 0xC539, 0xC53A, 0xC53F, 0xC541, 0xC545, 0xC547, 0xC548, 0xC54D }.Contains(info.Pid);
                    foreach (byte index in receiver ? new byte[] { 1, 2, 3, 4, 5, 6 } : new byte[] { 0xFF })
                        PollSlot(dev, info, index, output, log);
                }
            }
        }

        void PollSlot(HidDevice dev, HidInfo info, byte index, List<BatteryReading> output, TextWriter log)
        {
            string key = info.Path + "#" + index;
            if (!slots.TryGetValue(key, out Slot slot))
            {
                slot = Discover(dev, index);
                if (slot == null) return;
                slots[key] = slot;
            }
            if (!ReadBattery(dev, index, slot, out int percent, out bool charging, out bool approx))
            {
                slots.Remove(key); // éteint ou déconnecté : on redécouvrira au prochain passage
                return;
            }
            log?.WriteLine($"  logitech {index} : {slot.Name}, fonction {slot.FeatureId:X4}, {percent} %");
            output.Add(new BatteryReading { Id = "logi:" + key, Name = slot.Name, Kind = slot.Kind, Percent = percent, Charging = charging, Approximate = approx, Source = Name });
        }

        Slot Discover(HidDevice dev, byte index)
        {
            if (Send(dev, index, 0, 1, new byte[0]) == null) return null; // emplacement vide ou appareil éteint
            foreach (ushort id in new ushort[] { 0x1004, 0x1000, 0x1001 })
            {
                byte feature = FindFeature(dev, index, id);
                if (feature == 0) continue;
                var slot = new Slot { Feature = feature, FeatureId = id, Name = "Appareil Logitech" };
                byte nameFeature = FindFeature(dev, index, 0x0005);
                if (nameFeature != 0)
                {
                    byte[] count = Call(dev, index, nameFeature, 0);
                    var sb = new StringBuilder();
                    if (count != null)
                        for (int pos = 0; pos < count[4]; pos += 16)
                        {
                            byte[] part = Call(dev, index, nameFeature, 1, (byte)pos);
                            if (part == null) break;
                            sb.Append(Encoding.ASCII.GetString(part, 4, Math.Min(16, count[4] - pos)));
                        }
                    if (sb.Length > 0) slot.Name = sb.ToString().TrimEnd('\0').Trim();
                    byte[] type = Call(dev, index, nameFeature, 2);
                    slot.Kind = type == null ? DeviceKind.Other : type[4] == 0 || type[4] == 2 ? DeviceKind.Keyboard
                              : type[4] == 3 || type[4] == 4 || type[4] == 5 ? DeviceKind.Mouse
                              : type[4] == 8 ? DeviceKind.Headset : DeviceKind.Other;
                }
                if (slot.Kind == DeviceKind.Other) slot.Kind = Naming.Guess(slot.Name);
                return slot;
            }
            return null;
        }

        static bool ReadBattery(HidDevice dev, byte index, Slot slot, out int percent, out bool charging, out bool approx)
        {
            percent = 0; charging = false; approx = false;
            byte[] r = Call(dev, index, slot.Feature, (byte)(slot.FeatureId == 0x1004 ? 1 : 0));
            if (r == null) return false;
            if (slot.FeatureId == 0x1004)
            {
                percent = r[4];
                if (percent == 0) // certains appareils ne donnent qu'un niveau
                {
                    approx = true;
                    percent = (r[5] & 8) != 0 ? 100 : (r[5] & 4) != 0 ? 60 : (r[5] & 2) != 0 ? 20 : 5;
                }
                charging = r[6] >= 1 && r[6] <= 3;
            }
            else if (slot.FeatureId == 0x1000)
            {
                percent = r[4];
                charging = r[6] >= 1 && r[6] <= 4;
            }
            else
            {
                approx = true;
                percent = FromVoltage(r[4] << 8 | r[5]);
                charging = (r[6] & 0x80) != 0;
            }
            percent = Math.Max(0, Math.Min(100, percent));
            return true;
        }

        static int FromVoltage(int mv)
        {
            int[,] curve = { { 4186, 100 }, { 4067, 90 }, { 3989, 80 }, { 3922, 70 }, { 3859, 60 }, { 3811, 50 }, { 3778, 40 }, { 3751, 30 }, { 3717, 20 }, { 3671, 10 }, { 3646, 5 }, { 3579, 2 }, { 3500, 0 } };
            for (int i = 0; i < curve.GetLength(0); i++) if (mv >= curve[i, 0]) return curve[i, 1];
            return 0;
        }

        static byte FindFeature(HidDevice dev, byte index, ushort id)
        {
            byte[] r = Call(dev, index, 0, 0, (byte)(id >> 8), (byte)id);
            return r == null ? (byte)0 : r[4];
        }

        /// <summary>Une souris en veille met du temps à répondre : on réessaie une fois.</summary>
        static byte[] Call(HidDevice dev, byte index, byte feature, byte function, params byte[] args)
            => Send(dev, index, feature, function, args) ?? Send(dev, index, feature, function, args);

        static byte[] Send(HidDevice dev, byte index, byte feature, byte function, byte[] args)
        {
            // Un numéro différent à chaque requête : une réponse en retard ne passe pas pour la suivante.
            softwareId = (byte)(softwareId % 15 + 1);
            byte[] request = new byte[dev.Info.OutputLen];
            request[0] = 0x11; request[1] = index; request[2] = feature; request[3] = (byte)(function << 4 | softwareId);
            Array.Copy(args, 0, request, 4, Math.Min(args.Length, 16));
            dev.Flush();
            if (!dev.Write(request)) return null;
            byte[] buffer = new byte[Math.Max((int)dev.Info.InputLen, 20)];
            int end = Environment.TickCount + 700;
            while (Environment.TickCount - end < 0)
            {
                int n = dev.Read(buffer, Math.Max(1, end - Environment.TickCount));
                if (n < 7 || buffer[0] != 0x11 || buffer[1] != index) continue;
                if (buffer[2] == feature && buffer[3] == request[3]) return (byte[])buffer.Clone();
                if (buffer[2] == 0xFF && buffer[3] == feature && buffer[4] == request[3]) return null; // erreur HID++
            }
            return null;
        }
    }

    // ---------------------------------------------------------------- SteelSeries Arctis

    sealed class SteelSeriesSource : IBatterySource
    {
        public string Name => "SteelSeries";
        const ushort SteelSeries = 0x1038;

        public void Poll(List<BatteryReading> output, List<HidInfo> hid, TextWriter log)
        {
            foreach (HidInfo info in hid.Where(h => h.Vid == SteelSeries && h.UsagePage == 0xFFC0 && h.OutputLen > 1))
            {
                using (HidDevice dev = HidDevice.Open(info))
                {
                    if (dev == null) continue;
                    byte[] request = new byte[info.OutputLen];
                    request[1] = 0xB0;
                    dev.Flush();
                    if (!dev.Write(request)) continue;
                    byte[] b = new byte[Math.Max((int)info.InputLen, 8)];
                    int end = Environment.TickCount + 500;
                    while (Environment.TickCount - end < 0)
                    {
                        int n = dev.Read(b, Math.Max(1, end - Environment.TickCount));
                        if (n < 5 || b[1] != 0xB0) continue;
                        log?.WriteLine($"  steelseries {info.Pid:X4} : niveau {b[3]}, état {b[4]}");
                        if (b[4] == 0) break; // casque éteint
                        // Les premiers Arctis Nova 7 comptent de 0 à 4, les suivants donnent directement le pourcentage.
                        bool fiveSteps = new ushort[] { 0x2202, 0x2206, 0x220A, 0x223A, 0x227A }.Contains(info.Pid);
                        int percent = fiveSteps ? Math.Min((int)b[3], 4) * 25 : Math.Min((int)b[3], 100);
                        string name = string.IsNullOrEmpty(info.Product) ? "Casque SteelSeries" : info.Product;
                        output.Add(new BatteryReading { Id = "steelseries:" + info.Pid.ToString("X4"), Name = name, Kind = DeviceKind.Headset, Percent = percent, Charging = b[4] == 1, Approximate = fiveSteps, Source = Name });
                        break;
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- Razer

    /// <summary>Protocole Razer (rapports de 90 octets). Non testé sur du vrai matériel pour l'instant.</summary>
    sealed class RazerSource : IBatterySource
    {
        public string Name => "Razer";

        public void Poll(List<BatteryReading> output, List<HidInfo> hid, TextWriter log)
        {
            var done = new HashSet<ushort>();
            foreach (HidInfo info in hid.Where(h => h.Vid == 0x1532 && h.FeatureLen >= 91))
            {
                if (done.Contains(info.Pid)) continue;
                using (HidDevice dev = HidDevice.Open(info, false))
                {
                    if (dev == null) continue;
                    int level = Ask(dev, 0x80);
                    if (level < 0) continue;
                    done.Add(info.Pid);
                    string name = string.IsNullOrEmpty(info.Product) ? "Appareil Razer" : info.Product;
                    output.Add(new BatteryReading
                    {
                        Id = "razer:" + info.Pid.ToString("X4"), Name = name, Kind = Naming.Guess(name, DeviceKind.Mouse),
                        Percent = level * 100 / 255, Charging = Ask(dev, 0x84) == 1, Source = Name,
                    });
                }
            }
        }

        static int Ask(HidDevice dev, byte command)
        {
            foreach (byte transaction in new byte[] { 0x1F, 0x3F, 0xFF })
            {
                byte[] r = new byte[91];
                r[2] = transaction; r[6] = 2; r[7] = 0x07; r[8] = command;
                byte crc = 0;
                for (int i = 3; i < 89; i++) crc ^= r[i];
                r[89] = crc;
                if (!dev.SetFeature(r)) continue;
                Thread.Sleep(60);
                byte[] a = new byte[91];
                if (dev.GetFeature(a) && a[1] == 2 && a[7] == 0x07 && a[8] == command) return a[10];
            }
            return -1;
        }
    }

    // ---------------------------------------------------------------- HID standard (usages « batterie »)

    /// <summary>Tout appareil HID qui déclare sa batterie de façon standard, onduleurs compris.</summary>
    sealed class StandardHidSource : IBatterySource
    {
        public string Name => "HID standard";

        public void Poll(List<BatteryReading> output, List<HidInfo> hid, TextWriter log)
        {
            foreach (HidInfo info in hid)
            {
                if (info.Vid == 0x046D || info.Vid == 0x054C || info.Vid == 0x1532) continue; // sources dédiées
                using (HidDevice dev = HidDevice.Open(info, false))
                {
                    if (dev == null || !Native.HidD_GetPreparsedData(dev.Handle, out IntPtr pre)) continue;
                    try
                    {
                        int percent = Read(dev, pre, Native.HidP_Feature);
                        if (percent < 0) percent = Read(dev, pre, Native.HidP_Input);
                        if (percent < 0) continue;
                        string name = string.IsNullOrEmpty(info.Product) ? "Appareil sans fil" : info.Product;
                        DeviceKind fallback = info.UsagePage == 1 && info.Usage == 2 ? DeviceKind.Mouse
                                            : info.UsagePage == 1 && info.Usage == 6 ? DeviceKind.Keyboard
                                            : info.UsagePage == 1 && (info.Usage == 4 || info.Usage == 5) ? DeviceKind.Gamepad : DeviceKind.Other;
                        output.Add(new BatteryReading { Id = $"hid:{info.Vid:X4}:{info.Pid:X4}", Name = name, Kind = Naming.Guess(name, fallback), Percent = percent, Source = Name });
                    }
                    finally { Native.HidD_FreePreparsedData(pre); }
                }
            }
        }

        static int Read(HidDevice dev, IntPtr pre, int type)
        {
            if (Native.HidP_GetCaps(pre, out Native.HIDP_CAPS caps) != Native.HIDP_STATUS_SUCCESS) return -1;
            ushort count = type == Native.HidP_Feature ? caps.NumberFeatureValueCaps : caps.NumberInputValueCaps;
            int length = type == Native.HidP_Feature ? caps.FeatureReportByteLength : caps.InputReportByteLength;
            if (count == 0 || length == 0) return -1;
            const int capSize = 72;
            byte[] raw = new byte[count * capSize];
            if (Native.HidP_GetValueCaps(type, raw, ref count, pre) != Native.HIDP_STATUS_SUCCESS) return -1;
            for (int i = 0; i < count; i++)
            {
                int o = i * capSize;
                ushort page = BitConverter.ToUInt16(raw, o);
                ushort usage = BitConverter.ToUInt16(raw, o + 56);
                bool battery = (page == 0x06 && usage == 0x20) || (page == 0x85 && (usage == 0x64 || usage == 0x65 || usage == 0x66));
                if (!battery) continue;
                byte[] report = new byte[length];
                report[0] = raw[o + 2];
                bool ok = type == Native.HidP_Feature ? Native.HidD_GetFeature(dev.Handle, report, length) : Native.HidD_GetInputReport(dev.Handle, report, length);
                if (!ok) continue;
                ushort link = BitConverter.ToUInt16(raw, o + 6);
                if (Native.HidP_GetUsageValue(type, page, link, usage, out uint value, pre, report, length) != Native.HIDP_STATUS_SUCCESS) continue;
                int min = BitConverter.ToInt32(raw, o + 40), max = BitConverter.ToInt32(raw, o + 44);
                int percent = max > min ? (int)(((long)value - min) * 100 / (max - min)) : (int)value;
                if (percent >= 0 && percent <= 100) return percent;
            }
            return -1;
        }
    }

    // ---------------------------------------------------------------- Batterie du PC portable

    sealed class SystemSource : IBatterySource
    {
        public string Name => "Windows";

        public void Poll(List<BatteryReading> output, List<HidInfo> hid, TextWriter log)
        {
            if (!Native.GetSystemPowerStatus(out Native.SYSTEM_POWER_STATUS s)) return;
            if ((s.BatteryFlag & 128) != 0 || s.BatteryFlag == 255 || s.BatteryLifePercent > 100) return;
            output.Add(new BatteryReading { Id = "pc", Name = "Ce PC", Kind = DeviceKind.Laptop, Percent = s.BatteryLifePercent, Charging = (s.BatteryFlag & 8) != 0, Source = Name });
        }
    }
}
