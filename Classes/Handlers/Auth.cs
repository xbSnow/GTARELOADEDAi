using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using System;
using System.Threading.Tasks;

#nullable disable

namespace GTAServer
{
    public class Auth
    {
        private static readonly object _counterLock = new object();

        // One new account per IP per day
        private static readonly Dictionary<string, (int Count, DateTime WindowStart)> _newMembersPerIp
            = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase);
        private const int MaxNewMembersPerIpPerDay = 1;
        private const double NewMemberWindowHours = 24;

        /// <summary>Returns true if this IP may create a new account (max 1 per day).</summary>
        private static bool TryAllowNewMemberToday(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            lock (_counterLock)
            {
                if (!_newMembersPerIp.TryGetValue(ip, out var entry))
                {
                    _newMembersPerIp[ip] = (1, DateTime.UtcNow);
                    return true;
                }
                var now = DateTime.UtcNow;
                if ((now - entry.WindowStart).TotalHours >= NewMemberWindowHours)
                {
                    _newMembersPerIp[ip] = (1, now);
                    return true;
                }
                if (entry.Count >= MaxNewMembersPerIpPerDay)
                    return false;
                _newMembersPerIp[ip] = (entry.Count + 1, entry.WindowStart);
                return true;
            }
        }

        // CreateTicket spam: max requests per IP per window (no DB/decrypt until under limit)
        private static readonly Dictionary<string, (int Count, DateTime WindowStart)> _createTicketRateLimit
            = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase);
        private const int CreateTicketRateLimitMax = 15;
        private const double CreateTicketRateLimitWindowSeconds = 60;

        /// <summary>Returns true if this IP is under the CreateTicket rate limit. Call before decrypt/DB.</summary>
        private static bool TryConsumeCreateTicketRateLimit(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            lock (_counterLock)
            {
                var now = DateTime.UtcNow;
                if (!_createTicketRateLimit.TryGetValue(ip, out var entry))
                {
                    _createTicketRateLimit[ip] = (1, now);
                    return true;
                }
                if ((now - entry.WindowStart).TotalSeconds >= CreateTicketRateLimitWindowSeconds)
                {
                    _createTicketRateLimit[ip] = (1, now);
                    return true;
                }
                if (entry.Count >= CreateTicketRateLimitMax)
                    return false;
                _createTicketRateLimit[ip] = (entry.Count + 1, entry.WindowStart);
                return true;
            }
        }

        // Block list: IPs that sent too many suspicious (fake) auth attempts
        private static readonly HashSet<string> _blockedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, (int Count, DateTime WindowStart)> _suspiciousStrikes
            = new Dictionary<string, (int, DateTime)>(StringComparer.OrdinalIgnoreCase);
        private const int SuspiciousStrikeThreshold = 3; // after this many suspicious new-account attempts, block IP

        private static readonly string[] _suspiciousWords = new[]
        {
            "anal", "moist", "drippy", "soggy", "slippery", "wet", "tmp", "juicy", "damp", "cyprus", "gtav", "sex", "Skid_", "sync_", "Crash_", "LT5_", "gay", "she_", "kick_", "Snow_1", "Snow49310j-ss4", "Crash_1551R4PW2yVj", "Skid_1721V24P", "furry23799Zh5Zh", "furry", "snow_", "snow", "crash", "lt5", "skid", "kick_1941uN.yyH", "wolf", "wolf_", "a_", "a1", "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9", "a10", "a76901_VrE", "a4252W87Y", "a94304u5"

        };
        private static readonly Regex _botLikeSuffix = new Regex(@"\d{4,}[A-Za-z]{3,}", RegexOptions.Compiled);

        private static bool IsBlocked(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            lock (_counterLock)
                return _blockedIps.Contains(ip);
        }

        private static void BlockIp(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return;
            lock (_counterLock)
                _blockedIps.Add(ip);
        }

        /// <summary>Returns true if this strike caused the IP to be blocked (hit threshold).</summary>
        private static bool RecordSuspiciousStrike(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            lock (_counterLock)
            {
                if (!_suspiciousStrikes.TryGetValue(ip, out var entry))
                {
                    _suspiciousStrikes[ip] = (1, DateTime.UtcNow);
                    return 1 >= SuspiciousStrikeThreshold;
                }
                var now = DateTime.UtcNow;
                if ((now - entry.WindowStart).TotalHours >= 1)
                {
                    _suspiciousStrikes[ip] = (1, now);
                    return false;
                }
                int newCount = entry.Count + 1;
                _suspiciousStrikes[ip] = (newCount, entry.WindowStart);
                if (newCount >= SuspiciousStrikeThreshold)
                {
                    _blockedIps.Add(ip);
                    return true;
                }
                return false;
            }
        }
		// gamertag whitelist here the sus spam will not blok the gamertag anymore if it thinks its sus, fix by jamezvfx NO AI.
    private static readonly HashSet<string> _gamertagWhitelist =
    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Cool_dude_2013", "Eugenformula2", "CJCRA4CK18", "thomas_nana_04", "Nadia_Nk10", "JayManeFL", "CombatPuma__", "prensa_vecino99", "mustafa_1993l", "lI_Chiaretta_Il", "F4z0sOk", "Jibaro-123", "Ademlwyb16", "ROB-GON-STEP", "K518_KSA_518", "y130801yuri"
    };

        private static bool IsSuspiciousGamertag(string gamertag)
        {
			
            if (string.IsNullOrWhiteSpace(gamertag) || gamertag.Length > 32) return true;
			// >>> this is the whitelist bypass in full (only skips name heuristiks)
            if (_gamertagWhitelist.Contains(gamertag))
            return false;
            string lower = gamertag.ToLowerInvariant();
            foreach (string w in _suspiciousWords)
            {
                Console.WriteLine($"[AUTH BLOCK] Gamertag: {gamertag}");

                if (lower.Contains(w)) return true;
              

            }
            if (_botLikeSuffix.IsMatch(gamertag)) return true;
            int underscores = 0;
            foreach (char c in gamertag) if (c == '_') underscores++;
            if (underscores >= 2 && Regex.IsMatch(gamertag, @"_\d{4,}")) return true;
            return false;
        }
        

        /// <summary>Single place to detect fake vs real auth requests. Use for logging or decisions.</summary>
        public struct FakeDetectionResult
        {
            public bool IsLikelyFake;
            public string Reason;
        }

        private const int MinValidTicketLength = 32;  // real NP tickets have header + space + username (32 bytes)
        private const int MaxValidTicketLength = 2048;
        private const int MinGamertagLength = 1;       // PSN 3-16, we allow 1-20
        private const int MaxGamertagLength = 20;
        private static readonly string[] _allowedPlatformNames = new[] { "PS3", "ps3", "PSN", "psn" };

        /// <summary>Real NP tickets: contain a space (0x20) that delimits username; layout is header + space + 32-byte username. Space can be anywhere in ticket (matches Tools.GetUsername).</summary>
        private static bool IsValidNpTicketStructure(byte[] npTicket, out string reason)
        {
            reason = null;
            if (npTicket == null || npTicket.Length < MinValidTicketLength)
            {
                reason = "Ticket too short";
                return false;
            }
            if (npTicket.Length > MaxValidTicketLength)
            {
                reason = "Ticket too long (script?)";
                return false;
            }
            int spaceIndex = -1;
            for (int i = 0; i < npTicket.Length; i++)
            {
                if (npTicket[i] == 0x20)
                {
                    spaceIndex = i;
                    break;
                }
            }
            if (spaceIndex < 0)
            {
                reason = "Ticket missing username delimiter (no space)";
                return false;
            }
            if (spaceIndex + 33 > npTicket.Length)
            {
                reason = "Ticket too short for username region";
                return false;
            }
            return true;
        }

        /// <summary>Classify request as likely fake or real based on ticket structure, gamertag, platform, and whether member exists.</summary>
        public static FakeDetectionResult DetectFakeRequest(string gamertag, byte[] npTicket, bool isExistingMember, string ip, string platformName = null)
        {
            if (isExistingMember)
                return new FakeDetectionResult { IsLikelyFake = false, Reason = "Returning member" };

            if (npTicket == null)
                return new FakeDetectionResult { IsLikelyFake = true, Reason = "No ticket" };

            if (!IsValidNpTicketStructure(npTicket, out string ticketReason))
                return new FakeDetectionResult { IsLikelyFake = true, Reason = ticketReason };

            if (string.IsNullOrWhiteSpace(gamertag))
                return new FakeDetectionResult { IsLikelyFake = true, Reason = "Empty username from ticket" };

            int tagLen = (gamertag ?? "").Length;
            if (tagLen < MinGamertagLength || tagLen > MaxGamertagLength)
                return new FakeDetectionResult { IsLikelyFake = true, Reason = "Username length out of range (1–20)" };

            if (!string.IsNullOrWhiteSpace(platformName))
            {
                bool allowed = false;
                foreach (string p in _allowedPlatformNames)
                {
                    if (string.Equals(platformName, p, StringComparison.OrdinalIgnoreCase))
                    {
                        allowed = true;
                        break;
                    }
                }
                if (!allowed)
                    return new FakeDetectionResult { IsLikelyFake = true, Reason = "Unexpected platform: " + platformName };
            }

            if (IsSuspiciousGamertag(gamertag))
                return new FakeDetectionResult { IsLikelyFake = true, Reason = "Suspicious gamertag" };

            return new FakeDetectionResult { IsLikelyFake = false, Reason = "New member, clean" };
        }

        // =========================
        // =========================


        // =========================
        // PS3 / NP AUTH
        // =========================
        public static async Task<int> CreateTicketNp2(Globals.Client client)
        {
            string ip = client.endPoint?.Address?.ToString();
            string geo = await GeoIpLookup.GetGeoAsync(ip);
            if (IsBlocked(ip))
            {
                ServerLog.BlockedIpAttempt(ip, geo);
                client.response.StatusCode = (int)HttpStatusCode.Forbidden;
                return 1;
            }

            // Rate limit CreateTicket per IP — reject before decrypt/DB so spammers don't touch SQL or create users
            if (!TryConsumeCreateTicketRateLimit(ip))
            {
                Console.WriteLine($"[AUTH] CreateTicket rate limit exceeded from {ip}");
                client.response.StatusCode = 429; // TooManyRequests
                return 1;
            }

            ClientCrypto clientCrypto = new ClientCrypto();
            client.requestData = clientCrypto.Decrypt(client.requestData, "ps3");

            int userInfoLength = client.requestData.Length - 0x14;
            byte[] userInfoBytes = new byte[userInfoLength];
            Buffer.BlockCopy(client.requestData, 0, userInfoBytes, 0, userInfoLength);

            string userInfo = Encoding.ASCII.GetString(userInfoBytes);
            NameValueCollection collection = HttpUtility.ParseQueryString(userInfo);

            string platformName = collection["platformName"];
            string npTicketBase64 = collection["npTicket"];

            if (string.IsNullOrEmpty(platformName) || string.IsNullOrEmpty(npTicketBase64))
            {
                ServerLog.Error("Auth.CreateTicketNp2", "Missing platformName or npTicket", $"IP: {ip}");
                client.response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return 1;
            }

            byte[] npTicket = Convert.FromBase64String(npTicketBase64);
            string gamertag = Tools.GetUsername(npTicket);

            // Debug: when a real user gets empty username, log ticket structure (no secrets)
            if (string.IsNullOrWhiteSpace(gamertag) && npTicket != null && npTicket.Length >= 32)
            {
                int spaceIdx = -1;
                for (int i = 0; i < npTicket.Length && i < 256; i++)
                {
                    if (npTicket[i] == 0x20) { spaceIdx = i; break; }
                }
                string afterSpaceHex = spaceIdx >= 0 && spaceIdx + 32 <= npTicket.Length
                    ? BitConverter.ToString(npTicket, spaceIdx + 1, Math.Min(32, npTicket.Length - spaceIdx - 1)).Replace("-", " ")
                    : "(no space or too short)";
                Console.WriteLine($"[AUTH DEBUG] Empty username from {ip} | ticketLen={npTicket.Length} | spaceIndex={spaceIdx} | 32 bytes after space (hex): {afterSpaceHex}");
            }

            string xuid = Tools.GenerateXUID(gamertag);

            string sessionId = Tools.RandomSessionId();
            string sessionTicket = Tools.RandomBytesToBase64(60);

            Globals.Member member = new Globals.Member();
            bool exists = Database.GetMemberByXuid(ref member, xuid);

            FakeDetectionResult detection = DetectFakeRequest(gamertag, npTicket, exists, ip, platformName);
            Console.WriteLine(string.Format("[AUTH] {0} | {1} | {2} - {3}",
                ip ?? "?",
                gamertag ?? "?",
                detection.IsLikelyFake ? "FAKE" : "REAL",
                detection.Reason ?? ""));
            ServerLog.Auth(ip, gamertag, !detection.IsLikelyFake, detection.Reason ?? "", geo);

            if (!exists && detection.IsLikelyFake)
            {
                bool nowBlocked = false;
                if (IsSuspiciousGamertag(gamertag))
                {
                    nowBlocked = RecordSuspiciousStrike(ip);
                    Console.WriteLine($"[ANTI-SPAM] Suspicious gamertag \"{gamertag}\" from {ip}" + (nowBlocked ? " — IP blocked." : ""));
                }
                ServerLog.AntiSpam(ip, gamertag, detection.Reason ?? "Suspicious/fake", nowBlocked, geo);
                client.response.StatusCode = (int)HttpStatusCode.Forbidden;
                return 1;
            }

            if (!exists && !TryAllowNewMemberToday(ip))
            {
                Console.WriteLine($"[AUTH] One account per IP per day limit: {ip}");
                ServerLog.OneAccountPerDayLimit(ip, geo);
                client.response.StatusCode = 429; // TooManyRequests
                return 1;
            }

            if (exists)
            {
                member.last_online = DateTime.Now;
                member.session_ticket = sessionTicket;
                member.platform_name = platformName;
                Database.UpdateMember(ref member);
            }
            else
            {
                member.id = (int)(Database.GetMemberCount() + 1);
                member.xuid = xuid;
                member.crew_id = "7331";
                member.crew_tag = "RLO";
                member.gamertag = gamertag;
                member.expires = DateTime.Now.AddDays(7);
                member.last_online = DateTime.Now;
                member.session_ticket = sessionTicket;
                member.platform_name = platformName;
                Database.AddMember(ref member);
            }
            // === CREATE DEFAULT FILES ===
            string userDir = Path.Combine("bin", "members", xuid);
            if (!Directory.Exists(userDir))
            {
                Directory.CreateDirectory(userDir);

                // mpstats.json
                string defaultStats = "bin/mpstats.json";
                if (File.Exists(defaultStats))
                    File.Copy(defaultStats, Path.Combine(userDir, "mpstats.json"), true);

                // Default saves (game will overwrite)
                string[] defaultSaves = { "save_default0000.save", "save_char0001.save", "save_char0002.save" };
                foreach (string save in defaultSaves)
                {
                    string src = Path.Combine("bin", save);
                    string dst = Path.Combine(userDir, save);
                    if (File.Exists(src) && !File.Exists(dst))
                        File.Copy(src, dst);
                }

                Console.WriteLine($"[DEBUG] Created default files for {gamertag} ({xuid})");
                ServerLog.NewAccountCreated(ip, gamertag, xuid, geo);
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (member.banned == 1 && member.ban_expires > 0 && member.ban_expires <= now)
            {
                member.banned = 0;
                member.ban_expires = 0;
                Database.UpdateMember(ref member);
            }

            bool isBanned = member.banned == 1;
            if (isBanned)
{
    bool isPermanent = member.ban_expires == 0;
//1387887192, this is for en json i put it here, ignore thies numbers lmfao lmfao fr fr
    // fire & forget so auth does not wait
   _ = Tools.SendBanWebhookEmbedAsync(
    member.id,
    gamertag,
    isPermanent,
    member.ban_expires
 );
}
else
{
    // successful login
    _ = Tools.SendLoginWebhookEmbedAsync(gamertag);
}





            XmlDocument doc = new XmlDocument();
            doc.Load("bin/auth/CreateTicketResponse.xml");
            XmlNode secsNode = doc.GetElementsByTagName("SecsUntilExpiration")[0];
            XmlNode privilegesNode = doc.GetElementsByTagName("Privileges")[0];
            privilegesNode.InnerText = isBanned
                ? "7,20"
                : "1,2,3,4,5,6,8,9,10,11,14,15,16,17,18,19,21,22";


            if (isBanned && member.ban_expires > 0)
            {
                long remaining = member.ban_expires - now;
                if (remaining < 0) remaining = 0;
                doc.GetElementsByTagName("SecsUntilExpiration")[0].InnerText = remaining.ToString();
            } // by jamezvfx





            doc.GetElementsByTagName("PosixTime")[0].InnerText = now.ToString();
            doc.GetElementsByTagName("PlayerAccountId")[0].InnerText = member.id.ToString();
            doc.GetElementsByTagName("SessionId")[0].InnerText = sessionId;
            doc.GetElementsByTagName("SessionTicket")[0].InnerText = sessionTicket;
            doc.GetElementsByTagName("RockstarId")[0].InnerText = member.id.ToString();
            doc.GetElementsByTagName("Age")[0].InnerText = "21";
            doc.GetElementsByTagName("Nickname")[0].InnerText = gamertag;

            string file = doc.OuterXml;

            ServerCrypto serverCrypto = new ServerCrypto();
            client.responseData = serverCrypto.Encrypt(Encoding.UTF8.GetBytes(file), "ps3");

            client.response.StatusCode = (int)HttpStatusCode.OK;
            client.response.ContentType = "text/xml; charset=utf-8";
            client.response.ContentLength64 = client.responseData.Length;
            client.response.OutputStream.Write(client.responseData);

            return 0;
        }
    }
}

            

         