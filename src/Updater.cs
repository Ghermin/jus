using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace Jus
{
    /// <summary>
    /// Mises à jour automatiques depuis les releases GitHub, comme le Gestionnaire RGB : vérifiées 2 minutes après le
    /// lancement puis toutes les 6 heures. Le nouvel exe est téléchargé dans le dossier d'installation, vérifié
    /// (taille et SHA-256 annoncés par GitHub), puis lancé avec --updated : il ferme cette version, la remplace et se relance.
    /// </summary>
    static class Updater
    {
        const string Repo = "Ghermin/jus";
        const string AssetName = "Jus.exe";

        sealed class Asset
        {
            public Version Version;
            public string Url, Sha256;
            public long Size;
        }

        public static Version Current => Normalize(typeof(Updater).Assembly.GetName().Version);

        static string DownloadPath => Path.Combine(Installer.Dir, "Jus-update.exe");

        /// <summary>
        /// Télécharge la nouvelle version s'il y en a une. Renvoie l'exe prêt à lancer, sinon null.
        /// <paramref name="latest"/> = dernière version publiée, null si GitHub n'a pas répondu.
        /// </summary>
        public static string Fetch(out Version latest)
        {
            latest = null;
            try
            {
                Asset asset = Latest();
                if (asset == null) return null;
                latest = asset.Version;
                if (asset.Version <= Current) return null;
                Download(asset, DownloadPath);
                return DownloadPath;
            }
            catch (Exception) { return null; } // hors ligne, GitHub indisponible… on réessaiera plus tard
        }

        public static void Apply(string exe) => Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false });

        /// <summary>L'exe de mise à jour ne peut pas s'effacer lui-même : la version installée s'en charge.</summary>
        public static void CleanUp()
        {
            foreach (string path in new[] { DownloadPath, DownloadPath + ".part" })
            {
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        static Asset Latest()
        {
            string json;
            using (WebClient web = Client(true)) json = web.DownloadString($"https://api.github.com/repos/{Repo}/releases/latest");
            var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            if (!Version.TryParse(((root["tag_name"] as string) ?? "").TrimStart('v'), out Version version)) return null;
            foreach (object item in (object[])root["assets"])
            {
                var a = (Dictionary<string, object>)item;
                if (a["name"] as string != AssetName) continue;
                string digest = a.TryGetValue("digest", out object d) ? d as string : null;
                return new Asset
                {
                    Version = Normalize(version),
                    Url = (string)a["browser_download_url"],
                    Size = Convert.ToInt64(a["size"]),
                    Sha256 = digest != null && digest.StartsWith("sha256:") ? digest.Substring(7).ToLowerInvariant() : null,
                };
            }
            return null;
        }

        static void Download(Asset asset, string path)
        {
            if (!asset.Url.StartsWith("https://github.com/" + Repo + "/", StringComparison.OrdinalIgnoreCase))
                throw new IOException("adresse de téléchargement inattendue");
            string partial = path + ".part";
            File.Delete(partial);
            using (WebClient web = Client(false)) web.DownloadFile(asset.Url, partial);
            if (new FileInfo(partial).Length != asset.Size) { File.Delete(partial); throw new IOException("téléchargement incomplet"); }
            if (asset.Sha256 != null)
            {
                string hash;
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = File.OpenRead(partial))
                    hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                if (hash != asset.Sha256) { File.Delete(partial); throw new IOException("empreinte SHA-256 différente de celle annoncée par GitHub"); }
            }
            File.Delete(path);
            File.Move(partial, path);
        }

        static Version Normalize(Version v) => new Version(v.Major, v.Minor, Math.Max(v.Build, 0));

        static WebClient Client(bool api)
        {
            // Sans attribut TargetFramework dans l'exe, .NET 4.8 n'active pas TLS 1.2 de lui-même : GitHub l'exige.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var web = new WebClient();
            web.Headers[HttpRequestHeader.UserAgent] = "Jus";
            if (api) web.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return web;
        }
    }
}
