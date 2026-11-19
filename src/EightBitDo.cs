using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;

namespace Jus
{
    /// <summary>
    /// Patch « batterie en 2,4 GHz » de la 8BitDo Ultimate 2C et de son dongle, proposé par Jus et lancé seulement après
    /// l'accord de l'utilisateur. Même code que l'outil public github.com/Ghermin/8bitdo-2c-battery-patch (garder les deux
    /// en phase) : sauvegarde complète lue deux fois, firmware officiel téléchargé chez 8BitDo et vérifié par SHA-256,
    /// patch vérifié octet par octet, écriture limitée à la zone du programme, relecture, retour au mode normal.
    /// </summary>
    static class EightBitDo
    {
        public const ushort Vid = 0x2DC8, PadPid = 0x310A, IdlePid = 0x301C, BootPid = 0x3208;
        public const ushort PadPage = 0xFF7A, IdlePage = 0xFFA0, PadProduct = 0x301B, DongleProduct = 0x301C;
        public const byte Magic = 0xBA;
        const int Chunk = 46;

        /// <summary>Pendant un patch, Jus n'interroge plus la 8BitDo.</summary>
        public static volatile bool Busy;

        // ================================================================ firmware et patch

        const int PadBase = 0x6000, PadPayloadSize = 0x12400, PadEnd = 0x19000, PadFlash = 0x80000;
        const string PadPayloadSha = "dfd443c77f831008aa453ee2c1c9cb6871c36f8947c548e61d4387fba0a9da70";   // 1.09 officielle (.dat sans en-tête)
        const string PadPatchedSha = "01b2f36c92799c381b6b1c7ddb7d3f4f48813d43500f8fa993bfd4586c19bfe2";   // zone 0x6000-0x19000 patchée
        const int DongleBase = 0x6000, DongleSize = 0xA000, DongleEnd = 0x10000, DongleFlash = 0x20000;
        const string DongleFactorySha = "311768ba6c29453bf7ecc8bfb9b9f3d6810d65cd9f0992ff1d201566b441a792";
        const string DonglePatchedSha = "e106d8b50c5541bca0e9f00d76b2db00f520a99b3ef56af15af318381772deb4";

        sealed class Edit
        {
            public readonly int Addr; public readonly byte[] Old, New;
            public Edit(int addr, byte[] old, byte[] @new) { Addr = addr; Old = old; New = @new; }
        }

        const int Nop = 0x46C0;
        static readonly int[] ThumbToTc32 = { 0xF, 0xE, 0xA, 0xB, 0x0, 0x1, 0x5, 0x4, 0x2, 0x3, 0x7, 0x6, 0xD, 0xC, 0x8, 0x9 };
        static byte[] Tc32(params int[] thumb) => thumb.SelectMany(h => BitConverter.GetBytes((ushort)(ThumbToTc32[h >> 12 & 0xF] << 12 | h & 0xFFF))).ToArray();
        static byte[] Bl(int at, int target) { int off = target - (at + 4); return Tc32(0xF000 | (off >> 12) & 0x7FF, 0xF800 | (off & 0xFFF) >> 1); }
        static byte[] Hex(string s) => s.Split(' ').Select(x => Convert.ToByte(x, 16)).ToArray();
        static byte[] U32(uint v) => BitConverter.GetBytes(v);

        static readonly Edit[] PadEdits =
        {
            new Edit(0xDD90, Hex("03 c0"), Tc32(Nop)),                       // toujours 22 octets
            new Edit(0xDDC8, Hex("06 c0"), Tc32(Nop)),                       // octets 18-19 toujours remplis
            new Edit(0xDE14, U32(0x843487), U32(0x843415)),                  // octet 18 : batterie au lieu de la qualité du lien
            new Edit(0xDDD2, Hex("fd 97 e9 9b"), Tc32(0x2000 | Magic, Nop)),  // octet 19 : 0xBA
        };

        static readonly Edit[] DongleEdits = DongleEditList();

        static Edit[] DongleEditList()
        {
            byte[] helper = Tc32(0x2364, 0x9310, 0xAA00, 0x4B03, 0x781B, 0x71D3, 0x4B02, 0x781B, 0x7213, 0x4770)
                .Concat(U32(0x842E47)).Concat(U32(0x842E1C)).ToArray();
            return new[]
            {
                new Edit(0x6C28, Hex("05 c1"), Tc32(Nop)),                    // garde les octets 18-19 de tout paquet
                new Edit(0xAC0E, Hex("64 a3 10 33"), Bl(0xAC0E, 0xC100)),      // identification : appelle la fonction ajoutée
                new Edit(0xC100, Enumerable.Repeat((byte)0xFF, helper.Length).ToArray(), helper),
            };
        }

        static byte[] Apply(byte[] image, int baseAddr, Edit[] edits, bool undo)
        {
            byte[] img = (byte[])image.Clone();
            foreach (Edit e in edits)
            {
                byte[] from = undo ? e.New : e.Old, to = undo ? e.Old : e.New;
                int o = e.Addr - baseAddr;
                if (!img.Skip(o).Take(from.Length).SequenceEqual(from)) return null;
                Array.Copy(to, 0, img, o, to.Length);
            }
            return img;
        }

        static string Sha(byte[] b) { using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
        static byte[] Slice(byte[] b, int start, int end) => b.Skip(start).Take(end - start).ToArray();

        static byte[] PadOfficialZone(byte[] dat)
        {
            if (dat == null || dat.Length < 0x1C) return null;
            byte[] payload = dat.Skip(0x1C).ToArray();
            if (payload.Length != PadPayloadSize || Sha(payload) != PadPayloadSha) return null;
            byte[] zone = Enumerable.Repeat((byte)0xFF, PadEnd - PadBase).ToArray();
            Array.Copy(payload, zone, PadPayloadSize);
            return zone;
        }

        static byte[] PadPatchedZone(byte[] official)
        {
            byte[] z = official == null ? null : Apply(official, PadBase, PadEdits, false);
            return z != null && Sha(z) == PadPatchedSha ? z : null;
        }

        static byte[] DongleFactory(byte[] bank)
        {
            if (Sha(bank) == DongleFactorySha) return bank;
            byte[] b = Sha(bank) == DonglePatchedSha ? Apply(bank, DongleBase, DongleEdits, true) : null;
            return b != null && Sha(b) == DongleFactorySha ? b : null;
        }

        static byte[] DonglePatched(byte[] factory)
        {
            byte[] b = factory == null ? null : Apply(factory, DongleBase, DongleEdits, false);
            return b != null && Sha(b) == DonglePatchedSha ? b : null;
        }

        // ================================================================ lecture (requêtes autorisées : 81 05 00 21 01 et 81 05 05)

        public static byte[] Identify(HidDevice dev)
        {
            var request = new byte[dev.Info.OutputLen];
            request[0] = 0x81; request[1] = 0x05; request[3] = 0x21; request[4] = 0x01;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                dev.Flush();
                if (!dev.Write(request)) continue;
                var b = new byte[dev.Info.InputLen];
                int end = Environment.TickCount + 600;
                while (Environment.TickCount - end < 0)
                {
                    int n = dev.Read(b, Math.Max(1, end - Environment.TickCount));
                    if (n >= 10 && b[0] == 0x02 && b[1] == 0x22) return b;
                }
            }
            return null;
        }

        public static int Product(byte[] ident) => ident == null ? 0 : ident[6] | ident[7] << 8;

        /// <summary>Lecture de la flash (jamais au-delà de 512 Ko : la zone des registres s'efface parfois à la lecture).</summary>
        public static byte[] Read(HidDevice dev, int addr, int n)
        {
            if (n < 1 || n > Chunk || addr < 0 || addr + n > PadFlash) throw new ArgumentOutOfRangeException(nameof(addr));
            var request = new byte[dev.Info.OutputLen];
            request[0] = 0x81; request[1] = 0x05; request[2] = 0x05;
            request[6] = (byte)n; request[7] = (byte)(n >> 8);
            request[14] = (byte)addr; request[15] = (byte)(addr >> 8); request[16] = (byte)(addr >> 16); request[17] = (byte)(addr >> 24);
            var b = new byte[dev.Info.InputLen];
            for (int attempt = 0; attempt < 3; attempt++)
            {
                dev.Flush();
                if (!dev.Write(request)) continue;
                int end = Environment.TickCount + 300;
                while (Environment.TickCount - end < 0)
                {
                    int got = dev.Read(b, Math.Max(1, end - Environment.TickCount));
                    if (got < 18 + n || b[0] != 0x02 || b[1] != 0x05 || b[2] != 0x00 || b[4] != 0x05) continue;
                    if ((b[6] | b[7] << 8) != n || (b[14] | b[15] << 8 | b[16] << 16 | b[17] << 24) != addr) continue;
                    return b.Skip(18).Take(n).ToArray();
                }
            }
            return null;
        }

        static byte[] ReadRange(HidDevice dev, int addr, int length, Action<int> progress)
        {
            var result = new byte[length];
            for (int o = 0; o < length; o += Chunk)
            {
                byte[] part = Read(dev, addr + o, Math.Min(Chunk, length - o));
                for (int retry = 0; part == null && retry < 2; retry++) // un raté passager : on réveille le canal et on redemande
                {
                    Thread.Sleep(200);
                    Identify(dev);
                    part = Read(dev, addr + o, Math.Min(Chunk, length - o));
                }
                if (part == null) return null;
                Array.Copy(part, 0, result, o, part.Length);
                progress?.Invoke((int)(100L * (o + part.Length) / length));
            }
            return result;
        }

        /// <summary>Vrai si la manette (par câble) a le patch : lecture de 4 octets.</summary>
        public static bool? PadPatched(HidDevice dev) => Matches(Read(dev, PadEdits[3].Addr, 4), PadEdits[3].New);

        /// <summary>Vrai si le dongle (seul, ou relié à la manette) a le patch : lecture de 4 octets de sa flash.</summary>
        public static bool? DongleIsPatched(HidDevice dev) => Matches(Read(dev, DongleEdits[1].Addr, 4), DongleEdits[1].New);

        static bool? Matches(byte[] read, byte[] expected) => read == null ? (bool?)null : read.SequenceEqual(expected);

        // ================================================================ installation guidée

        public enum Part { Dongle, Pad }
        public enum State { Waiting, Working, Done, Failed }

        /// <summary>
        /// Patche le dongle et la manette dès qu'ils sont dans le bon état (dongle seul ; manette par câble), dans n'importe
        /// quel ordre, jusqu'à ce que les deux soient faits ou que l'utilisateur annule. Sur un fil à part.
        /// </summary>
        public sealed class Installer
        {
            public event Action<Part, State, string, int> Report; // partie, état, texte, progression (-1 : aucune)
            public volatile bool Cancel;
            readonly string folder;
            byte[] dat;

            public Installer()
            {
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jus", "8bitdo");
            }

            string lastLogged;

            void Say(Part part, State state, string text, int progress = -1)
            {
                string line = $"{part} {state} {text}";
                if (line != lastLogged) Log(line); // la progression seule ne va pas au journal
                lastLogged = line;
                Report?.Invoke(part, state, text, progress);
            }

            void Log(string s)
            {
                try { Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, "journal.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {s}\r\n"); } catch { }
            }

            public void Run()
            {
                bool dongleDone = false, padDone = false;
                string shownDongle = null, shownPad = null;
                Busy = true;
                try
                {
                    while (!Cancel && !(dongleDone && padDone))
                    {
                        List<HidInfo> hid = Hid.Enumerate();
                        HidInfo boot = hid.FirstOrDefault(h => h.Vid == Vid && h.Pid == BootPid);
                        if (boot != null) // une écriture interrompue : on la termine
                        {
                            Resume(boot, ref dongleDone, ref padDone);
                            continue;
                        }

                        var pads = Find(hid, PadPid, PadPage);
                        var idle = Find(hid, IdlePid, IdlePage).Where(d => Product(d.ident) == DongleProduct).ToList();
                        var cablePad = pads.Where(d => Product(d.ident) == PadProduct).ToList();
                        var linked = pads.Where(d => Product(d.ident) == DongleProduct).ToList();

                        // Une écriture à la fois : après chacune, on regarde de nouveau ce qui est branché.
                        if (!dongleDone)
                        {
                            if (idle.Count == 1) { dongleDone = PatchDongle(idle[0].info, idle[0].ident); shownDongle = null; continue; }
                            if (linked.Count == 1 && Ask(linked[0].info, DongleIsPatched) == true) { dongleDone = true; Say(Part.Dongle, State.Done, "Déjà fait"); }
                            else Wait(Part.Dongle, ref shownDongle, idle.Count > 1 ? "Ne branche qu'un seul dongle 8BitDo" : "Éteins la manette, en laissant le dongle branché");
                        }
                        if (!padDone)
                        {
                            if (cablePad.Count == 1) { padDone = PatchPad(cablePad[0].info, cablePad[0].ident); shownPad = null; continue; }
                            Wait(Part.Pad, ref shownPad, cablePad.Count > 1 ? "Ne branche qu'une seule manette par câble" : "Branche la manette au PC avec un câble USB");
                        }
                        if (!Cancel && !(dongleDone && padDone)) Thread.Sleep(700);
                    }
                }
                catch (Exception e)
                {
                    Log("erreur : " + e);
                    Say(dongleDone ? Part.Pad : Part.Dongle, State.Failed, "Erreur : " + e.Message);
                }
                finally { Busy = false; }
            }

            void Wait(Part part, ref string shown, string text)
            {
                if (shown == text) return;
                shown = text;
                Say(part, State.Waiting, text);
            }

            static List<(HidInfo info, byte[] ident)> Find(List<HidInfo> hid, ushort pid, ushort page)
            {
                var found = new List<(HidInfo, byte[])>();
                foreach (HidInfo h in hid.Where(h => h.Vid == Vid && h.Pid == pid && h.UsagePage == page && h.InputLen >= 64 && h.OutputLen >= 64))
                    using (HidDevice dev = HidDevice.Open(h))
                        if (dev != null) found.Add((h, Identify(dev)));
                return found;
            }

            static bool? Ask(HidInfo info, Func<HidDevice, bool?> question)
            {
                using (HidDevice dev = HidDevice.Open(info)) return dev == null ? null : question(dev);
            }

            // ------------------------------------------------------------ dongle

            bool PatchDongle(HidInfo info, byte[] ident)
            {
                if (ident[2] != 100) { Say(Part.Dongle, State.Failed, $"Firmware {ident[2] / 100.0:0.00} : seul le 1.00 d'usine est pris en charge"); Cancel = true; return false; }
                byte[] backup;
                using (HidDevice dev = HidDevice.Open(info))
                {
                    if (dev == null) return false;
                    if (DongleIsPatched(dev) == true) { Say(Part.Dongle, State.Done, "Déjà fait"); return true; }
                    backup = Backup(dev, Part.Dongle, "dongle", DongleFlash, ident[2]);
                    if (backup == null) return false;
                }
                byte[] target = DonglePatched(DongleFactory(Slice(backup, DongleBase, DongleEnd)));
                if (target == null || Sha(Slice(backup, DongleBase, DongleEnd)) != DongleFactorySha)
                {
                    Log("banque du dongle " + Sha(Slice(backup, DongleBase, DongleEnd)));
                    Say(Part.Dongle, State.Failed, "Ce dongle n'a pas le firmware prévu : rien n'est modifié");
                    Cancel = true;
                    return false;
                }
                if (Staged(Slice(backup, 0x12000, 0x12024))) { Say(Part.Dongle, State.Failed, "Une mise à jour est en attente sur ce dongle : rien n'est modifié"); Cancel = true; return false; }
                if (!EnterUpdateMode(info)) return false;
                HidInfo boot = WaitFor(h => h.Vid == Vid && h.Pid == BootPid, 12000);
                if (boot == null) { Say(Part.Dongle, State.Failed, "Le dongle n'est pas passé en mode mise à jour : rien n'a été écrit. Débranche-le et rebranche-le."); return false; }
                return DongleBoot(boot, target, backup);
            }

            bool DongleBoot(HidInfo bootInfo, byte[] bank, byte[] backup)
            {
                using (HidDevice dev = HidDevice.Open(bootInfo))
                {
                    if (dev == null || bootInfo.OutputLen != 65 || bootInfo.InputLen != 65) { Say(Part.Dongle, State.Failed, "Mode mise à jour inattendu : rien n'est écrit"); return false; }
                    var bl = new Boot(dev, DongleBase, DongleEnd);
                    if (bl.Identify() != DongleProduct) return Restart(bl, IdlePid, IdlePage, Part.Dongle, false, "Ce n'est pas le dongle : rien n'est écrit");
                    if (backup != null)
                    {
                        byte[] rec = bl.ReadRange(0x12000, 0x24);
                        bool same = Same(bl.ReadRange(0, DongleBase), backup, 0) && Same(bl.ReadRange(0x1E000, 0x40), backup, 0x1E000);
                        if (!same || rec == null || Staged(rec)) return Restart(bl, IdlePid, IdlePage, Part.Dongle, false, "Le dongle ne correspond pas à sa sauvegarde : rien n'est écrit");
                    }
                    Say(Part.Dongle, State.Working, "Écriture… ne débranche rien", 0);
                    if (!bl.Erase(DongleBase, DongleSize)) return Stuck(Part.Dongle);
                    // L'en-tête (46 premiers octets) en dernier : interrompu avant, le dongle reste en mode mise à jour.
                    for (int a = DongleBase + Chunk; a < DongleEnd; a += Chunk)
                    {
                        byte[] blk = bank.Skip(a - DongleBase).Take(Math.Min(Chunk, DongleEnd - a)).ToArray();
                        if (blk.All(x => x == 0xFF)) continue;
                        if (!bl.Write(a, DongleSize, blk)) return Stuck(Part.Dongle);
                        if ((a - DongleBase) % 0x800 < Chunk) Say(Part.Dongle, State.Working, "Écriture… ne débranche rien", 100 * (a - DongleBase) / DongleSize);
                    }
                    if (!Same(bl.ReadRange(DongleBase + Chunk, DongleSize - Chunk), bank, Chunk)) return Stuck(Part.Dongle);
                    if (!bl.Write(DongleBase, DongleSize, bank.Take(Chunk).ToArray())) return Stuck(Part.Dongle);
                    if (!Same(bl.ReadRange(DongleBase, DongleSize), bank, 0)) return Stuck(Part.Dongle);
                    return Restart(bl, IdlePid, IdlePage, Part.Dongle, true, "Fait");
                }
            }

            // ------------------------------------------------------------ manette

            bool PatchPad(HidInfo info, byte[] ident)
            {
                if (ident[2] > 109) { Say(Part.Pad, State.Failed, $"Firmware {ident[2] / 100.0:0.00} : plus récent que la 1.09, pas encore pris en charge"); Cancel = true; return false; }
                using (HidDevice dev = HidDevice.Open(info))
                {
                    if (dev == null) return false;
                    if (ident[2] == 109 && PadPatched(dev) == true) { Say(Part.Pad, State.Done, "Déjà fait"); return true; }
                }
                byte[] target = PadPatchedZone(PadOfficialZone(OfficialDat()));
                if (target == null) { Say(Part.Pad, State.Failed, "Impossible de récupérer le firmware officiel chez 8BitDo (connexion ?)"); return false; }
                byte[] backup;
                using (HidDevice dev = HidDevice.Open(info))
                {
                    if (dev == null) return false;
                    backup = Backup(dev, Part.Pad, "manette", PadFlash, ident[2]);
                    if (backup == null) return false;
                }
                if (Pending(Slice(backup, 0x3F000, 0x3F024))) { Say(Part.Pad, State.Failed, "Une mise à jour est en attente sur la manette : rien n'est modifié"); Cancel = true; return false; }
                if (!EnterUpdateMode(info)) return false;
                HidInfo boot = WaitFor(h => h.Vid == Vid && h.Pid == BootPid, 12000);
                if (boot == null) { Say(Part.Pad, State.Failed, "La manette n'est pas passée en mode mise à jour : rien n'a été écrit. Débranche-la et rebranche-la."); return false; }
                return PadBoot(boot, target, backup);
            }

            bool PadBoot(HidInfo bootInfo, byte[] target, byte[] backup)
            {
                using (HidDevice dev = HidDevice.Open(bootInfo))
                {
                    if (dev == null || bootInfo.OutputLen != 65 || bootInfo.InputLen != 65) { Say(Part.Pad, State.Failed, "Mode mise à jour inattendu : rien n'est écrit"); return false; }
                    var bl = new Boot(dev, PadBase, PadEnd);
                    if (bl.Identify() != PadProduct) return Restart(bl, PadPid, PadPage, Part.Pad, false, "Ce n'est pas la manette : rien n'est écrit");
                    byte[] hi = bl.Read(0x20000, 16);
                    if (hi == null || !hi.All(x => x == 0xFF)) return Restart(bl, PadPid, PadPage, Part.Pad, false, "Contenu inattendu : rien n'est écrit");
                    if (backup != null)
                    {
                        bool same = Same(bl.ReadRange(0, PadBase), backup, 0) && Same(bl.ReadRange(0x76000, 0x1000), backup, 0x76000)
                                 && Same(bl.ReadRange(PadBase, PadEnd - PadBase), backup, PadBase);
                        byte[] rec = bl.ReadRange(0x3F000, 0x24);
                        if (!same || rec == null || Pending(rec)) return Restart(bl, PadPid, PadPage, Part.Pad, false, "La manette ne correspond pas à sa sauvegarde : rien n'est écrit");
                    }
                    Say(Part.Pad, State.Working, "Écriture… ne débranche rien", 0);
                    if (!bl.Erase(PadBase, PadEnd - PadBase)) return Stuck(Part.Pad);
                    int end = PadBase + PadPayloadSize;
                    for (int a = PadBase; a < end; a += Chunk)
                    {
                        if (!bl.Write(a, PadPayloadSize, target.Skip(a - PadBase).Take(Math.Min(Chunk, end - a)).ToArray())) return Stuck(Part.Pad);
                        if ((a - PadBase) % 0x1000 < Chunk) Say(Part.Pad, State.Working, "Écriture… ne débranche rien", 100 * (a - PadBase) / PadPayloadSize);
                    }
                    for (int a = end; a < PadEnd; a += Chunk) // après le programme : seulement les blocs non vides
                    {
                        byte[] blk = target.Skip(a - PadBase).Take(Math.Min(Chunk, PadEnd - a)).ToArray();
                        if (!blk.All(x => x == 0xFF) && !bl.Write(a, PadPayloadSize, blk)) return Stuck(Part.Pad);
                    }
                    if (!Same(bl.ReadRange(PadBase, PadEnd - PadBase), target, 0)) return Stuck(Part.Pad);
                    return Restart(bl, PadPid, PadPage, Part.Pad, true, "Fait");
                }
            }

            // ------------------------------------------------------------ reprise d'une écriture interrompue

            void Resume(HidInfo bootInfo, ref bool dongleDone, ref bool padDone)
            {
                int? who;
                using (HidDevice dev = HidDevice.Open(bootInfo)) who = dev == null ? null : new Boot(dev, 0, 0).Identify();
                if (who == PadProduct)
                {
                    Say(Part.Pad, State.Working, "Reprise de l'écriture interrompue");
                    byte[] target = PadPatchedZone(PadOfficialZone(OfficialDat()));
                    if (target == null) { Say(Part.Pad, State.Failed, "Impossible de récupérer le firmware officiel chez 8BitDo (connexion ?)"); Thread.Sleep(5000); return; }
                    padDone = PadBoot(bootInfo, target, null);
                }
                else if (who == DongleProduct)
                {
                    Say(Part.Dongle, State.Working, "Reprise de l'écriture interrompue");
                    byte[] last = LatestBackup("dongle", DongleFlash);
                    byte[] target = last == null ? null : DonglePatched(DongleFactory(Slice(last, DongleBase, DongleEnd)));
                    if (target == null) { Say(Part.Dongle, State.Failed, "Pas de sauvegarde du dongle pour terminer"); Cancel = true; return; }
                    dongleDone = DongleBoot(bootInfo, target, null);
                }
                else Thread.Sleep(1000);
            }

            // ------------------------------------------------------------ commun

            byte[] Backup(HidDevice dev, Part part, string kind, int size, int version)
            {
                byte[] a = ReadRange(dev, 0, size, p => Say(part, State.Working, "Sauvegarde (1/2)…", p / 2));
                byte[] b = a == null ? null : ReadRange(dev, 0, size, p => Say(part, State.Working, "Sauvegarde (2/2)…", 50 + p / 2));
                if (a == null || b == null) { Say(part, State.Waiting, "Lecture interrompue : rien n'est modifié"); return null; }
                if (!a.SequenceEqual(b)) { Say(part, State.Waiting, "Les deux lectures diffèrent : rien n'est modifié"); return null; }
                string dir = Path.Combine(folder, "sauvegardes");
                Directory.CreateDirectory(dir);
                string name = $"{kind}-{(version / 100.0).ToString("0.00", CultureInfo.InvariantCulture)}-{DateTime.Now:yyyyMMdd-HHmmss}.bin";
                File.WriteAllBytes(Path.Combine(dir, name), a);
                File.AppendAllText(Path.Combine(dir, "SHA256SUMS"), $"{Sha(a)}  {name}\n");
                Log("sauvegarde " + name);
                return a;
            }

            byte[] LatestBackup(string kind, int size)
            {
                string dir = Path.Combine(folder, "sauvegardes");
                if (!Directory.Exists(dir)) return null;
                return Directory.GetFiles(dir, kind + "-*.bin").OrderByDescending(File.GetLastWriteTimeUtc).Select(File.ReadAllBytes).FirstOrDefault(b => b.Length == size);
            }

            /// <summary>Le .dat officiel 1.09 : gardé dans le dossier de Jus après un premier téléchargement chez 8BitDo.</summary>
            byte[] OfficialDat()
            {
                if (dat != null) return dat;
                string cache = Path.Combine(folder, "ultimate-2c-1.09.dat");
                try { if (File.Exists(cache) && PadOfficialZone(File.ReadAllBytes(cache)) != null) return dat = File.ReadAllBytes(cache); } catch { }
                Say(Part.Pad, State.Working, "Téléchargement du firmware officiel chez 8BitDo…");
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var req = (HttpWebRequest)WebRequest.Create("http://dl.8bitdo.com:8080/firmware/select");
                    req.Method = "POST"; req.Timeout = 15000; req.ContentLength = 0;
                    req.Headers.Add("Beta", "1"); req.Headers.Add("Type", "75");
                    string json;
                    using (var r = req.GetResponse()) using (var s = new StreamReader(r.GetResponseStream())) json = s.ReadToEnd();
                    var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                    string path = null;
                    if (root != null && root.TryGetValue("list", out object list) && list is IEnumerable items)
                        foreach (object o in items)
                            if (o is Dictionary<string, object> e && e.TryGetValue("version", out object v) && e.TryGetValue("filePathName", out object p)
                                && Math.Abs(Convert.ToDouble(v, CultureInfo.InvariantCulture) - 1.09) < 0.001)
                                path = p as string;
                    if (path == null) return null;
                    using (var wc = new WebClient())
                    {
                        byte[] d = wc.DownloadData("http://dl.8bitdo.com:8080" + path);
                        if (PadOfficialZone(d) == null) { Log("fichier téléchargé refusé (SHA-256)"); return null; }
                        Directory.CreateDirectory(folder);
                        File.WriteAllBytes(cache, d);
                        return dat = d;
                    }
                }
                catch (Exception e) { Log("téléchargement : " + e.Message); return null; }
            }

            bool EnterUpdateMode(HidInfo info)
            {
                using (HidDevice dev = HidDevice.Open(info))
                {
                    if (dev == null) return false;
                    Log("passage en mode mise à jour");
                    var cmd = new byte[info.OutputLen];
                    cmd[0] = 0x81; cmd[1] = 0x05; cmd[3] = 0x51; cmd[4] = 0x00;
                    dev.Write(cmd);
                    return true;
                }
            }

            bool Restart(Boot bl, ushort pid, ushort page, Part part, bool ok, string text)
            {
                bl.Restart();
                HidInfo back = WaitFor(h => h.Vid == Vid && h.Pid == pid && h.UsagePage == page, 15000);
                if (back == null && ok) text += " (débranche et rebranche si besoin)";
                Say(part, ok ? State.Done : State.Failed, text);
                if (!ok) Cancel = true;
                return ok;
            }

            int failures;

            /// <summary>L'appareil reste en mode mise à jour, sans danger : la boucle reprend l'écriture, trois fois au plus.</summary>
            bool Stuck(Part part)
            {
                if (++failures >= 3)
                {
                    Say(part, State.Failed, "L'écriture n'aboutit pas. L'appareil reste en mode mise à jour, sans danger : rebranche-le et réessaie plus tard.");
                    Cancel = true;
                    return false;
                }
                Say(part, State.Working, "L'écriture n'a pas abouti, nouvel essai…");
                Thread.Sleep(2000);
                return false;
            }

            static HidInfo WaitFor(Func<HidInfo, bool> match, int ms)
            {
                int end = Environment.TickCount + ms;
                while (Environment.TickCount - end < 0)
                {
                    HidInfo found = Hid.Enumerate().FirstOrDefault(match);
                    if (found != null) { Thread.Sleep(500); return Hid.Enumerate().FirstOrDefault(match) ?? found; }
                    Thread.Sleep(300);
                }
                return null;
            }

            static bool Same(byte[] read, byte[] reference, int offset) => read != null && read.SequenceEqual(reference.Skip(offset).Take(read.Length));

            static bool Pending(byte[] record)
            {
                uint magic = BitConverter.ToUInt32(record, 0x18), length = BitConverter.ToUInt32(record, 0x20);
                return !(magic == 0 || magic == 0xFFFFFFFF) || !(length == 0 || length == 0xFFFFFFFF);
            }

            static bool Staged(byte[] r) => BitConverter.ToUInt32(r, 0x18) == 0xAEB14D6C && BitConverter.ToUInt32(r, 0x20) >= 1 && BitConverter.ToUInt32(r, 0x20) <= 0x3EFFF;
        }

        /// <summary>
        /// Mode mise à jour (2DC8:3208), rapports de 64 octets sans ID : [0]=05 [1:3]=commande [5:7]=longueur
        /// [7:9]=CRC-16/MODBUS [9:13]=paramètre [13:17]=adresse [17:]=données ; 5 lecture, 4 effacement, 3 écriture,
        /// 8 identité, 7 redémarrage. Effacement et écriture refusés hors de la zone autorisée.
        /// </summary>
        sealed class Boot
        {
            readonly HidDevice dev;
            readonly int start, end;

            public Boot(HidDevice dev, int start, int end) { this.dev = dev; this.start = start; this.end = end; }

            static byte[] Request(int cmd, int addr = 0, int param = 0, byte[] data = null, int? datalen = null)
            {
                var r = new byte[64];
                data = data ?? new byte[0];
                r[0] = 0x05;
                r[1] = (byte)cmd; r[2] = (byte)(cmd >> 8);
                int len = datalen ?? data.Length;
                r[5] = (byte)len; r[6] = (byte)(len >> 8);
                if (data.Length > 0) { ushort crc = Crc16(data); r[7] = (byte)crc; r[8] = (byte)(crc >> 8); }
                r[9] = (byte)param; r[10] = (byte)(param >> 8); r[11] = (byte)(param >> 16); r[12] = (byte)(param >> 24);
                r[13] = (byte)addr; r[14] = (byte)(addr >> 8); r[15] = (byte)(addr >> 16); r[16] = (byte)(addr >> 24);
                Array.Copy(data, 0, r, 17, data.Length);
                return r;
            }

            static ushort Crc16(byte[] data)
            {
                int c = 0xFFFF;
                foreach (byte x in data)
                {
                    c ^= x;
                    for (int i = 0; i < 8; i++) c = (c & 1) != 0 ? c >> 1 ^ 0xA001 : c >> 1;
                }
                return (ushort)c;
            }

            bool Send(byte[] request)
            {
                var buf = new byte[65];
                Array.Copy(request, 0, buf, 1, 64);
                dev.Flush();
                return dev.Write(buf, 1000);
            }

            byte[] Call(byte[] r, int timeoutMs = 1000, int tries = 3)
            {
                var buf = new byte[65];
                for (int t = 0; t < tries; t++)
                {
                    if (!Send(r)) continue;
                    int stop = Environment.TickCount + timeoutMs;
                    while (Environment.TickCount - stop < 0)
                    {
                        int n = dev.Read(buf, Math.Max(1, stop - Environment.TickCount));
                        if (n < 19) continue;
                        byte[] a = buf.Skip(1).Take(64).ToArray();
                        if (a[0] == 0 && a[1] == 5 && a[2] == 0 && a[4] == r[1] && a[14] == r[13] && a[15] == r[14] && a[16] == r[15] && a[17] == r[16]) return a;
                    }
                }
                return null;
            }

            public int? Identify()
            {
                byte[] a = Call(Request(8, param: 4, datalen: 4), 1000, 1);
                if (a == null || a[6] != 2 || a[7] != 0) return null;
                return a[18] | a[19] << 8;
            }

            public byte[] Read(int addr, int n) => Call(Request(5, addr, n, datalen: n))?.Skip(18).Take(n).ToArray();

            public byte[] ReadRange(int addr, int length)
            {
                var result = new byte[length];
                for (int o = 0; o < length; o += Chunk)
                {
                    byte[] part = Read(addr + o, Math.Min(Chunk, length - o));
                    if (part == null) return null;
                    Array.Copy(part, 0, result, o, part.Length);
                }
                return result;
            }

            void Guard(int addr, int length)
            {
                if (addr < start || length <= 0 || addr + length > end) throw new InvalidOperationException($"refusé : 0x{addr:x}+0x{length:x} hors de 0x{start:x}-0x{end:x}");
            }

            public bool Erase(int addr, int length) { Guard(addr, length); return Call(Request(4, addr, length), 15000, 1) != null; }

            public bool Write(int addr, int param, byte[] data)
            {
                Guard(addr, data.Length);
                if (data.Length > Chunk) throw new InvalidOperationException("bloc trop long");
                return Call(Request(3, addr, param, data)) != null;
            }

            public void Restart() { Send(Request(7)); Thread.Sleep(300); }
        }
    }
}
