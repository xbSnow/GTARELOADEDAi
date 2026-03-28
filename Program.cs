using System.Net;
using System.Text;

#nullable disable

namespace GTAServer
{
    class Program
    {
        private static HttpListener listener;

        private static byte[] GetRequestData(HttpListenerRequest request)
        {
            try
            {
                if (!request.HasEntityBody)
                    return Array.Empty<byte>();

                using (MemoryStream ms = new MemoryStream())
                {
                    request.InputStream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] [GetRequestData] Exception: {ex.Message}");
                return null;
            }
        }

        private static async Task HandlePostRequest(Globals.Client client)
        {
            if (client.path == null)
            {
                Console.WriteLine("[DEBUG] client.path is null");
                client.response?.Close();
                return;
            }

            if (client.response == null)
            {
                Console.WriteLine("[DEBUG] client.response is null");
                return;
            }

            try
            {
                int functionNameOffset = client.path.LastIndexOf('/') + 1;
                string functionName = string.Empty;

                if (functionNameOffset != -1)
                {
                    functionName = client.path.Substring(functionNameOffset);
                }

                if (Globals.PostList.TryGetValue(functionName, out Func<Globals.Client, Task<int>> function))
                {
                    await function(client);
                }
                else
                {
                    client.response.StatusCode = (int)HttpStatusCode.NotFound;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] [HandlePostRequest] Exception: {ex}");
                ServerLog.Error("HandlePostRequest", ex.Message, ex.StackTrace);
                client.response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            finally
            {
                client.response.Close();
            }
        }


        private static void HandleGetRequest(Globals.Client client)
        {
            try
            {
                int fileNameOffset = client.path.LastIndexOf('/') + 1;
                string fileName = fileNameOffset != -1 ? client.path.Substring(fileNameOffset) : "";
                int fileExtOffset = client.path.LastIndexOf('.') + 1;
                string fileExt = fileExtOffset != -1 ? client.path.Substring(fileExtOffset) : "";

                if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(fileExt))
                {
                    client.response.StatusCode = (int)HttpStatusCode.BadRequest;
                    client.response.Close();
                    return;
                }

                // === 1. CLOUD MEMBER FILES (mpstats, saves) ===
                if (client.path.Contains("members"))
                {
                    string session_ticket = client.request.Headers.Get("ros-SessionTicket");
                    Globals.Member member = new Globals.Member();
                    if (!Database.GetMemberFromSessionTicket(ref member, session_ticket))
                    {
                        client.response.StatusCode = (int)HttpStatusCode.Unauthorized;
                        client.response.Close();
                        return;
                    }

                    string filePath = Members.GetPath(client.path, member.platform_name);

                    // ← CRITICAL: RETURN 404 IF FILE DOES NOT EXIST
                    if (filePath == "DoesNotExist" || string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    {
                        client.response.StatusCode = (int)HttpStatusCode.NotFound;
                        client.response.ContentType = "text/plain";
                        client.responseData = Encoding.UTF8.GetBytes("File not found");
                        client.response.ContentLength64 = client.responseData.Length;
                        client.response.OutputStream.Write(client.responseData);
                        client.response.Close();
                        return;
                    }

                    // ← SERVE FILE
                    byte[] fileData = File.ReadAllBytes(filePath);
                    ServerCrypto serverCrypto = new ServerCrypto(true);
                    client.responseData = serverCrypto.Encrypt(fileData, member.platform_name);

                    client.response.StatusCode = (int)HttpStatusCode.OK;
                    client.response.ContentType = Tools.GetContentType(fileExt);
                    client.response.ContentLength64 = client.responseData.Length;
                    client.response.OutputStream.Write(client.responseData);
                    client.response.Close();
                    return;
                }

                // === 2. MISSIONS ===
                else if (client.path.Contains("gta5mission"))
                {
                    string session_ticket = client.request.Headers.Get("ros-SessionTicket");
                    Globals.Member member = new Globals.Member();
                    Database.GetMemberFromSessionTicket(ref member, session_ticket);

                    // Extract everything after "gta5mission/"
                    int missionIndex = client.path.IndexOf("gta5mission/") + "gta5mission/".Length;
                    string relativePath = client.path.Substring(missionIndex);

                    // Normalize slashes
                    relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

                    // Final mission file path
                    string filePath = Path.Combine("bin", "ugc", "gta5missions", relativePath);

                    Console.WriteLine(filePath);

                    if (!File.Exists(filePath))
                    {
                        client.response.StatusCode = (int)HttpStatusCode.NotFound;
                        client.response.Close();
                        return;
                    }

                    byte[] fileData = File.ReadAllBytes(filePath);
                    ServerCrypto serverCrypto = new ServerCrypto(true);
                    client.responseData = serverCrypto.Encrypt(fileData, member.platform_name);

                    client.response.StatusCode = (int)HttpStatusCode.OK;
                    client.response.ContentType = Tools.GetContentType(fileExt);
                    client.response.ContentLength64 = client.responseData.Length;
                    client.response.OutputStream.Write(client.responseData);
                    client.response.Close();
                    return;
                }

                // === GTA5 PHOTO  ===
                else if (client.path.Contains("/ugc/gta5photo/"))
                {
                    string session_ticket = client.request.Headers.Get("ros-SessionTicket");
                    Globals.Member member = new Globals.Member();
                    Database.GetMemberFromSessionTicket(ref member, session_ticket);

                    string[] parts = client.path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    int idx = Array.IndexOf(parts, "gta5photo");

                    if (idx == -1 || parts.Length < idx + 3)
                    {
                        client.response.StatusCode = (int)HttpStatusCode.BadRequest;
                        client.response.Close();
                        return;
                    }

                    string contentId = parts[idx + 1];
                    string imageName = parts[idx + 2];

                    string photoPath = Path.Combine(
                        "bin",
                        "members",
                        member.xuid,
                        "gta5photo",
                        contentId,
                        imageName
                    );

                    if (!File.Exists(photoPath))
                    {
                        client.response.StatusCode = (int)HttpStatusCode.NotFound;
                        client.response.Close();
                        return;
                    }

                    byte[] fileData = File.ReadAllBytes(photoPath);
                    ServerCrypto serverCrypto = new ServerCrypto(true);
                    client.responseData = serverCrypto.Encrypt(fileData, member.platform_name);

                    client.response.StatusCode = (int)HttpStatusCode.OK;
                    client.response.ContentType = Tools.GetContentType(Path.GetExtension(imageName).TrimStart('.'));
                    client.response.ContentLength64 = client.responseData.Length;
                    client.response.OutputStream.Write(client.responseData);
                    client.response.Close();

                    Console.WriteLine($"[UGC-CDN] Served gta5photo {contentId}/{imageName}");
                    return;
                }

                // === 3. REDIRECT TUNABLES ===
                else if (client.path.Count(c => c == '/') > 1)
                {
                    string redirectUrl = $"http://tunables.jamezvfx.lol/{fileName}";
                    client.response.Redirect(redirectUrl);
                    client.responseData = Encoding.UTF8.GetBytes($"Found. Redirecting to {redirectUrl}");
                    client.response.ContentLength64 = client.responseData.Length;
                    client.response.OutputStream.Write(client.responseData);
                    client.response.Close();
                    return;
                }

                // === 4. CREW EMBLEM ===
else if (client.path.EndsWith("emblem_128.dds"))
{
    byte[] crew_emblem = null;

    string session_ticket = client.request.Headers.Get("ros-SessionTicket");
    Globals.Member member = new Globals.Member();

    if (Database.GetMemberFromSessionTicket(ref member, session_ticket))
    {
        string xuid = member.xuid;

        string crewPath = $"bin/crews/{xuid}/";
        string fileNamePattern = "emblem_128";
        string[] extensions = { ".dds", ".png", ".jpg", ".webp" };

        string foundFile = extensions
            .Select(ext => Path.Combine(crewPath, $"{fileNamePattern}{ext}"))
            .FirstOrDefault(File.Exists);

        if (foundFile != null)
        {
            if (foundFile.EndsWith(".dds"))
                crew_emblem = File.ReadAllBytes(foundFile);
            else
                crew_emblem = Tools.ConvertPngToDdsBytes(foundFile);
        }
    }

    if (crew_emblem == null)
        crew_emblem = new byte[0]; // return empty

    client.responseData = crew_emblem;
    client.response.StatusCode = (int)HttpStatusCode.OK;
    client.response.ContentType = "image/dds";
    client.response.ContentLength64 = client.responseData.Length;
    client.response.OutputStream.Write(client.responseData);
    client.response.Close();
    return;
}

                // === 5. STATIC FILES (bin/) ===
                else
                {
                    string filePath = Path.Combine("bin/static", fileName);
                    if (!File.Exists(filePath))
                    {
                        client.response.StatusCode = (int)HttpStatusCode.NotFound;
                        client.response.Close();
                        return;
                    }

                    client.responseData = File.ReadAllBytes(filePath);
                    client.response.StatusCode = (int)HttpStatusCode.OK;
                    client.response.ContentType = Tools.GetContentType(fileExt);
                    client.response.ContentLength64 = client.responseData.Length;
                    client.response.OutputStream.Write(client.responseData);
                    client.response.Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] [HandleGetRequest] Exception: {ex.Message}\n{ex.StackTrace}");
                ServerLog.Error("HandleGetRequest", ex.Message, ex.StackTrace);
                client.response.StatusCode = (int)HttpStatusCode.InternalServerError;
                client.response.Close();
            }
        }

        private static async Task HandleRequest(HttpListenerContext context)
        {
            Globals.Client client = default;
            try
            {
                client = new Globals.Client()
                {
                    request = context.Request,
                    requestData = null,

                    response = context.Response,
                    responseData = null,

                    endPoint = context.Request.RemoteEndPoint,

                    method = context.Request.HttpMethod,
                    path = context.Request.Url.AbsolutePath
                };

                Console.WriteLine(string.Format("[{0}] {1} {2}", client.endPoint.ToString(), client.method, client.path));

                client.requestData = GetRequestData(client.request);

                Globals.Member member = new Globals.Member();

                bool Exists = Database.GetMemberFromSessionTicket(ref member, client.request.Headers.Get("ros-SessionTicket"));

                if (Exists)
                {
                    member.last_online = DateTime.Now;
                    member.session_ticket = client.request.Headers.Get("ros-SessionTicket");
                    Database.UpdateLastOnline(ref member);
                }

                switch (client.method)
                {
                    case "POST":
                        await HandlePostRequest(client);
                        break;
                    case var path when path.Contains("/share/gta5/mpchars/"):
                        if (client.request.HttpMethod == "POST")
                        {
                            await Members.SavePortrait(client);
                        }
                        break;
                    case "GET":
                        HandleGetRequest(client);
                        break;
                    default:
                        client.response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        client.response.Close();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("[DEBUG] [HandleRequest] Exception: {0}", ex.Message));
                ServerLog.Error("HandleRequest", ex.Message, ex.StackTrace);
            }
            finally
            {
                try
                {
                    if (context?.Request != null)
                    {
                        var req = context.Request;
                        var res = context.Response;
                        ServerLog.AccessLog(
                        remoteIp: client.endPoint?.Address?.ToString(),
                        remotePort: client.endPoint?.Port,
                        method: client.method,
                        path: client.path,
                        query: string.IsNullOrEmpty(req.Url?.Query) ? null : req.Url.Query,
                        userAgent: req.Headers["User-Agent"],
                        contentType: req.ContentType,
                        contentLength: req.ContentLength64 > 0 ? req.ContentLength64 : (long?)null,
                        referer: req.Headers["Referer"],
                        accept: req.Headers["Accept"],
                        rosSessionTicket: string.IsNullOrEmpty(req.Headers["ros-SessionTicket"]) ? null : "present",
                        responseStatusCode: res.StatusCode,
                        responseContentLength: res.ContentLength64 > 0 ? res.ContentLength64 : (long?)null);
                    }
                }
                catch { /* never crash request */ }
            }
        }

        private static async Task ListenAsync()
        {
            while (listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync();

                    _ = Task.Run(async () => await HandleRequest(context));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG] [Listen] Erro: {ex.Message}");
                    ServerLog.Error("Listen", ex.Message, ex.StackTrace);
                }
            }
        }

        private static void ShowLoadingScreen()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine();
            //Console.WriteLine(@"  ██████╗ ████████╗ █████╗     ███████╗███████╗██████╗ ██╗   ██╗███████╗██████╗ ");
            //Console.WriteLine(@" ██╔════╝ ╚══██╔══╝██╔══██╗    ██╔════╝██╔════╝██╔══██╗██║   ██║██╔════╝██╔══██╗");
            //Console.WriteLine(@" ██║  ███╗   ██║   ███████║    ███████╗█████╗  ██████╔╝██║   ██║█████╗  ██████╔╝");
            //Console.WriteLine(@" ██║   ██║   ██║   ██╔══██║    ╚════██║██╔══╝  ██╔══██╗╚██╗ ██╔╝██╔══╝  ██╔══██╗");
            //Console.WriteLine(@" ╚██████╔╝   ██║   ██║  ██║    ███████║███████╗██║  ██║ ╚████╔╝ ███████╗██║  ██║");
            //Console.WriteLine(@"  ╚═════╝    ╚═╝   ╚═╝  ╚═╝    ╚══════╝╚══════╝╚═╝  ╚═╝  ╚═══╝  ╚══════╝╚═╝  ╚═╝");
              Console.WriteLine(@"  ██████╗ ████████╗ █████╗     ██████╗ ███████╗██╗      ██████╗  █████╗ ██████╗ ███████╗██████╗ ");
              Console.WriteLine(@" ██╔════╝ ╚══██╔══╝██╔══██╗    ██╔══██╗██╔════╝██║     ██╔═══██╗██╔══██╗██╔══██╗██╔════╝██╔══██╗");
              Console.WriteLine(@" ██║  ███╗   ██║   ███████║    ██████╔╝█████╗  ██║     ██║   ██║███████║██║  ██║█████╗  ██║  ██║");
              Console.WriteLine(@" ██║   ██║   ██║   ██╔══██║    ██╔══██╗██╔══╝  ██║     ██║   ██║██╔══██║██║  ██║██╔══╝  ██║  ██║");
              Console.WriteLine(@" ╚██████╔╝   ██║   ██║  ██║    ██║  ██║███████╗███████╗╚██████╔╝██║  ██║██████╔╝███████╗██████╔╝");
              Console.WriteLine(@"  ╚═════╝    ╚═╝   ╚═╝  ╚═╝    ╚═╝  ╚═╝╚══════╝╚══════╝ ╚═════╝ ╚═╝  ╚═╝╚═════╝ ╚══════╝╚═════╝ ");

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("                    GTAO Reloaded · Auth & Cloud Services");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.Write("  Loading ");
            char[] spin = { '|', '/', '-', '\\' };
            for (int i = 0; i < 20; i++)
            {
                Console.Write(spin[i % 4]);
                Thread.Sleep(80);
                Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
            }
            Console.WriteLine(" ");
            Console.Write("  [");
            int barWidth = 40;
            for (int i = 0; i <= barWidth; i++)
            {
                Console.Write(new string('#', i) + new string('.', barWidth - i));
                Console.Write("] " + (i * 100 / barWidth) + "%");
                Thread.Sleep(40);
                if (i < barWidth)
                    Console.SetCursorPosition(12, Console.CursorTop);
            }
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine();
            Console.WriteLine("  ✓ Ready. Listening on http://+:80/");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Thread.Sleep(600);
        }

        private static async Task Main(string[] args)
        {
            try
            {
                Console.Title = "GTAServer";
                ShowLoadingScreen();
                listener = new HttpListener();
                listener.Prefixes.Add("http://+:80/");
                listener.Start();

                await ListenAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] [Main] Exception: {ex.Message}");
                ServerLog.Error("Main", ex.Message, ex.StackTrace);
            }
        }
    }
}
