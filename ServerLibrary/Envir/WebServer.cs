using Library;
using MirDB;
using Server.DBModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Server.Envir
{
    public static class WebServer
    {
        public static ConcurrentQueue<WebCommand> WebCommandQueue;
        public static bool WebServerStarted { get; set; }
        private const int MaxSystemDatabaseSyncBytes = 10 * 1024 * 1024;
        private const int MaxIPNBytes = 256 * 1024;
        private static readonly TimeSpan IPNVerificationTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan RecentIPNRetention = TimeSpan.FromDays(1);
        private static readonly ConcurrentDictionary<string, DateTime> RecentIPNIds = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static HttpListener WebListener;
        public const string ActivationCommand = "Activation", ResetCommand = "Reset", DeleteCommand = "Delete",
            SystemDBSyncCommand = "SystemDBSync";
        public const string ActivationKey = "ActivationKey", ResetKey = "ResetKey", DeleteKey = "DeleteKey";

        public const string Completed = "Completed";
        public const string Currency = "GBP";

        public static Dictionary<decimal, long> GoldTable = new Dictionary<decimal, long>
        {
            [5M] = 500,
            [10M] = 1030,
            [15M] = 1590,
            [20M] = 2180,
            [30M] = 3360,
            [50M] = 5750,
            [100M] = 12000,
        };

        public const string VerifiedPath = @".\Database\Store\Verified\",
            InvalidPath = @".\Database\Store\Invalid\",
            CompletePath = @".\Database\Store\Complete\";

        private static HttpListener BuyListener, IPNListener;
        public static ConcurrentQueue<IPNMessage> Messages = new ConcurrentQueue<IPNMessage>();
        public static List<IPNMessage> PaymentList = new List<IPNMessage>(), HandledPayments = new List<IPNMessage>();
        private static readonly object PaymentLock = new object();

        static WebServer()
        {
            ReloadVerifiedMessages();
        }

        private static void ReloadVerifiedMessages()
        {
            ClearPaymentLists();
            while (Messages.TryDequeue(out _))
            {
            }

            try
            {
                if (!Directory.Exists(VerifiedPath))
                    return;

                string[] files = Directory.GetFiles(VerifiedPath);

                foreach (string file in files)
                {
                    try
                    {
                        Messages.Enqueue(new IPNMessage { FileName = file, Message = File.ReadAllText(file), Verified = true });
                    }
                    catch (Exception ex)
                    {
                        SEnvir.Log($"Failed to load verified IPN file '{file}': {ex}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                SEnvir.Log($"Failed to enumerate verified IPN directory '{VerifiedPath}': {ex}");
            }
        }

        public static void StartWebServer(bool log = true)
        {
            StopWebServer(log: false);

            if (!Config.EnableWebServer) return;

            try
            {
                ReloadVerifiedMessages();
                WebCommandQueue = new ConcurrentQueue<WebCommand>();

                WebListener = new HttpListener();
                WebListener.Prefixes.Add(Config.WebPrefix);

                WebListener.Start();
                WebListener.BeginGetContext(WebConnection, WebListener);

                BuyListener = new HttpListener();
                BuyListener.Prefixes.Add(Config.BuyPrefix);

                IPNListener = new HttpListener();
                IPNListener.Prefixes.Add(Config.IPNPrefix);

                BuyListener.Start();
                BuyListener.BeginGetContext(BuyConnection, BuyListener);

                IPNListener.Start();
                IPNListener.BeginGetContext(IPNConnection, IPNListener);



                WebServerStarted = true;

                if (log) SEnvir.Log("Web Server Started.");
            }
            catch (Exception ex)
            {
                WebServerStarted = false;
                WebCommandQueue = null;
                SEnvir.Log(ex.ToString());

                CloseListener(WebListener);
                WebListener = null;

                CloseListener(BuyListener);
                BuyListener = null;

                CloseListener(IPNListener);
                IPNListener = null;
            }
        }
        public static void StopWebServer(bool log = true)
        {
            bool wasStarted = WebServerStarted ||
                              WebListener != null ||
                              BuyListener != null ||
                              IPNListener != null ||
                              WebCommandQueue != null;
            if (!wasStarted) return;

            HttpListener expiredWebListener = WebListener;
            WebListener = null;

            HttpListener expiredBuyListener = BuyListener;
            BuyListener = null;
            HttpListener expiredIPNListener = IPNListener;
            IPNListener = null;


            WebServerStarted = false;
            CloseListener(expiredWebListener);
            CloseListener(expiredBuyListener);
            CloseListener(expiredIPNListener);

            WebCommandQueue = null;
            ClearPaymentLists();
            while (Messages.TryDequeue(out _))
            {
            }

            if (log) SEnvir.Log("Web Server Stopped.");
        }

        private static void WebConnection(IAsyncResult result)
        {
            HttpListenerContext context = null;
            HttpListener listener = result.AsyncState as HttpListener;

            try
            {
                if (!IsCurrentListener(listener, WebListener)) return;

                context = listener.EndGetContext(result);

                string command = context.Request.QueryString["Type"];

                switch (command)
                {
                    case ActivationCommand:
                        Activation(context);
                        break;
                    case ResetCommand:
                        ResetPassword(context);
                        break;
                    case DeleteCommand:
                        DeleteAccount(context);
                        break;
                    case SystemDBSyncCommand:
                        SystemDBSync(context);
                        break;
                    default:
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        context.Response.Close();
                        break;
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (HttpListenerException ex)
            {
                if (IsCurrentListener(listener, WebListener))
                    SEnvir.Log("Web command listener exception: " + ex);

                TryCloseResponse(context);
            }
            catch (Exception ex)
            {
                SEnvir.Log("Web command exception: " + ex);
                TryCloseResponse(context, HttpStatusCode.InternalServerError);
            }
            finally
            {
                TryCloseResponse(context);

                if (IsCurrentListener(listener, WebListener))
                    listener.BeginGetContext(WebConnection, listener);
            }
        }

        private static void SystemDBSync(HttpListenerContext context)
        {
            try
            {
                if (!Config.AllowSystemDBSync)
                {
                    SEnvir.Log($"Trying sync but not enabled");
                    context.Response.StatusCode = 401;
                    return;
                }

                if (context.Request.HttpMethod != "POST" || !context.Request.HasEntityBody)
                {
                    SEnvir.Log($"Trying sync but method is not post or not have body");
                    context.Response.StatusCode = 401;
                    return;
                }

                if (context.Request.ContentLength64 > MaxSystemDatabaseSyncBytes)
                {
                    SEnvir.Log($"Trying sync but exceeded SystemDB size");
                    context.Response.StatusCode = 400;
                    return;
                }

                var masterPassword = context.Request.QueryString["Key"];
                if (string.IsNullOrEmpty(masterPassword) || !string.Equals(masterPassword, Config.SyncKey, StringComparison.Ordinal))
                {
                    SEnvir.Log($"Trying sync but key received is not valid");
                    context.Response.StatusCode = 400;
                    return;
                }

                Session session = SEnvir.Session;
                if (session == null)
                {
                    SEnvir.Log("Trying sync but database session is not ready");
                    context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    return;
                }

                SEnvir.Log($"Starting remote syncronization...");

                byte[] buffer = ReadRequestBody(context.Request.InputStream, context.Request.ContentLength64, MaxSystemDatabaseSyncBytes);
                if (buffer.Length == 0)
                {
                    SEnvir.Log($"Trying sync but body is empty");
                    context.Response.StatusCode = 400;
                    return;
                }

                ValidateSystemDatabasePayload(session, buffer);

                if (session.BackUp && !Directory.Exists(session.SystemBackupPath))
                    Directory.CreateDirectory(session.SystemBackupPath);

                ReplaceSystemDatabase(session, buffer);

                context.Response.StatusCode = 200;

                SEnvir.Log($"Syncronization completed...");
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                context.Response.StatusCode = 400;
                SEnvir.Log("Syncronization request rejected: " + ex.Message);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "text/plain";
                var message = Encoding.UTF8.GetBytes(ex.ToString());
                context.Response.OutputStream.Write(message, 0, message.Length);
                SEnvir.Log("Syncronization exception: " + ex.ToString());
            }
            finally
            {
                context.Response.Close();
            }
        }

        private static byte[] ReadRequestBody(Stream input, long contentLength, int maxBytes)
        {
            if (contentLength > maxBytes)
                throw new InvalidDataException($"Request body exceeds {maxBytes} bytes.");

            using MemoryStream memory = contentLength > 0 && contentLength <= maxBytes
                ? new MemoryStream((int)contentLength)
                : new MemoryStream();

            byte[] chunk = new byte[16 * 1024];
            int length;
            while ((length = input.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (memory.Length + length > maxBytes)
                    throw new InvalidDataException($"Request body exceeds {maxBytes} bytes.");

                memory.Write(chunk, 0, length);
            }

            if (contentLength >= 0 && memory.Length != contentLength)
                throw new EndOfStreamException($"Expected {contentLength} bytes but received {memory.Length} bytes.");

            return memory.ToArray();
        }

        private static void ValidateSystemDatabasePayload(Session session, byte[] buffer)
        {
            if (session?.SystemHeader == null || session.SystemHeader.Length < sizeof(int))
                throw new InvalidDataException("Current System.db header is not available for synchronization validation.");

            int expectedSections = BitConverter.ToInt32(session.SystemHeader, 0);
            if (expectedSections <= 0)
                throw new InvalidDataException("Current System.db header does not contain any system collections.");

            try
            {
                using MemoryStream stream = new MemoryStream(buffer, false);
                using BinaryReader reader = Library.Encryption.GetReader(stream);

                byte[] header = reader.ReadBytes(session.SystemHeader.Length);
                if (!BytesEqual(header, session.SystemHeader))
                    throw new InvalidDataException("System.db header does not match the current server schema or encryption key.");

                for (int i = 0; i < expectedSections; i++)
                {
                    int length = reader.ReadInt32();
                    if (length < 0 || length > MaxSystemDatabaseSyncBytes)
                        throw new InvalidDataException($"System.db section {i + 1} has invalid length {length}.");

                    byte[] section = reader.ReadBytes(length);
                    if (section.Length != length)
                        throw new EndOfStreamException($"System.db section {i + 1} ended early.");
                }

                if (reader.BaseStream.ReadByte() != -1)
                    throw new InvalidDataException("System.db payload contains trailing data.");
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or CryptographicException or ArgumentException or NotSupportedException)
            {
                throw new InvalidDataException("System.db payload could not be read; it may be corrupt or encrypted with a different key.", ex);
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;

            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i])
                    return false;

            return true;
        }

        private static void ReplaceSystemDatabase(Session session, byte[] buffer)
        {
            string systemPath = session.SystemPath;
            string directory = Path.GetDirectoryName(systemPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = systemPath + "." + Guid.NewGuid().ToString("N") + ".sync.tmp";

            try
            {
                File.WriteAllBytes(tempPath, buffer);

                if (File.Exists(systemPath))
                {
                    if (session.BackUp)
                    {
                        string backupPath = GetUniqueBackupPath(
                            session.SystemBackupPath,
                            "System " + session.ToBackUpFileName(DateTime.UtcNow));

                        WriteCompressedBackupAtomically(systemPath, backupPath);
                    }

                    File.Replace(tempPath, systemPath, null);
                }
                else
                {
                    File.Move(tempPath, systemPath);
                }
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        private static string GetUniqueBackupPath(string directory, string baseName)
        {
            Directory.CreateDirectory(directory);

            string extension = Session.Extension + Session.CompressExtension;
            string target = Path.Combine(directory, baseName + extension);
            if (!File.Exists(target)) return target;

            for (int i = 2; ; i++)
            {
                target = Path.Combine(directory, $"{baseName} ({i}){extension}");
                if (!File.Exists(target)) return target;
            }
        }

        private static void WriteCompressedBackupAtomically(string sourcePath, string backupPath)
        {
            string directory = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
            string tempPath = Path.Combine(tempDirectory, $".{Path.GetFileName(backupPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (FileStream sourceStream = File.OpenRead(sourcePath))
                using (FileStream destStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (GZipStream compress = new GZipStream(destStream, CompressionMode.Compress))
                    sourceStream.CopyTo(compress);

                File.Move(tempPath, backupPath);
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        private static void Activation(HttpListenerContext context)
        {
            string key = context.Request.QueryString[ActivationKey];

            if (string.IsNullOrEmpty(key))
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.Close();
                return;
            }

            AccountInfo account = null;
            for (int i = 0; i < SEnvir.AccountInfoList.Count; i++)
            {
                AccountInfo temp = SEnvir.AccountInfoList[i]; //Different Threads, Caution must be taken to prevent errors
                if (string.Compare(temp.ActivationKey, key, StringComparison.Ordinal) != 0) continue;

                account = temp;
                break;
            }

            if (Config.AllowWebActivation && account != null && TryEnqueueWebCommand(new WebCommand(CommandType.Activation, account)))
            {
                context.Response.Redirect(Config.ActivationSuccessLink);
            }
            else
                context.Response.Redirect(Config.ActivationFailLink);

            context.Response.Close();
        }
        private static void ResetPassword(HttpListenerContext context)
        {
            string key = context.Request.QueryString[ResetKey];

            if (string.IsNullOrEmpty(key))
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.Close();
                return;
            }

            AccountInfo account = null;
            for (int i = 0; i < SEnvir.AccountInfoList.Count; i++)
            {
                AccountInfo temp = SEnvir.AccountInfoList[i]; //Different Threads, Caution must be taken to prevent errors
                if (string.Compare(temp.ResetKey, key, StringComparison.Ordinal) != 0) continue;

                account = temp;
                break;
            }

            if (Config.AllowWebResetPassword && account != null && account.ResetTime.AddMinutes(25) > SEnvir.Now &&
                TryEnqueueWebCommand(new WebCommand(CommandType.PasswordReset, account)))
            {
                context.Response.Redirect(Config.ResetSuccessLink);
            }
            else
                context.Response.Redirect(Config.ResetFailLink);

            context.Response.Close();
        }
        private static void DeleteAccount(HttpListenerContext context)
        {
            string key = context.Request.QueryString[DeleteKey];

            if (string.IsNullOrEmpty(key))
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.Close();
                return;
            }

            AccountInfo account = null;
            for (int i = 0; i < SEnvir.AccountInfoList.Count; i++)
            {
                AccountInfo temp = SEnvir.AccountInfoList[i]; //Different Threads, Caution must be taken to prevent errors
                if (string.Compare(temp.ActivationKey, key, StringComparison.Ordinal) != 0) continue;

                account = temp;
                break;
            }

            if (Config.AllowDeleteAccount && account != null && TryEnqueueWebCommand(new WebCommand(CommandType.AccountDelete, account)))
            {
                context.Response.Redirect(Config.DeleteSuccessLink);
            }
            else
                context.Response.Redirect(Config.DeleteFailLink);

            context.Response.Close();
        }

        private static void BuyConnection(IAsyncResult result)
        {
            HttpListenerContext context = null;
            HttpListener listener = result.AsyncState as HttpListener;

            try
            {
                if (!IsCurrentListener(listener, BuyListener)) return;

                context = listener.EndGetContext(result);

                string characterName = context.Request.QueryString["Character"];

                CharacterInfo character = null;
                for (int i = 0; i < SEnvir.CharacterInfoList.Count; i++)
                {
                    if (string.Compare(SEnvir.CharacterInfoList[i].CharacterName, characterName, StringComparison.OrdinalIgnoreCase) != 0) continue;

                    character = SEnvir.CharacterInfoList[i];
                    break;
                }

                if (character?.Account.Key != context.Request.QueryString["Key"])
                    character = null;

                string response = character == null ? Properties.Resources.CharacterNotFound : Properties.Resources.BuyGameGold.Replace("$CHARACTERNAME$", character.CharacterName);

                using (StreamWriter writer = new StreamWriter(context.Response.OutputStream, context.Request.ContentEncoding, 1024, true))
                    writer.Write(response);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (HttpListenerException ex)
            {
                if (IsCurrentListener(listener, BuyListener))
                    SEnvir.Log("Buy listener exception: " + ex);
            }
            catch (Exception ex)
            {
                SEnvir.Log("Buy request exception: " + ex);
                TryCloseResponse(context, HttpStatusCode.InternalServerError);
            }
            finally
            {
                TryCloseResponse(context);

                if (IsCurrentListener(listener, BuyListener)) //IsBound ?
                    listener.BeginGetContext(BuyConnection, listener);
            }

        }
        private static void IPNConnection(IAsyncResult result)
        {
            const string LiveURL = @"https://ipnpb.paypal.com/cgi-bin/webscr";

            const string verified = "VERIFIED";
            HttpListenerContext context = null;
            HttpListener listener = result.AsyncState as HttpListener;

            try
            {
                if (!IsCurrentListener(listener, IPNListener)) return;

                context = listener.EndGetContext(result);

                if (context.Request.HttpMethod != "POST" || !context.Request.HasEntityBody)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(context.Request.ContentType) &&
                    !context.Request.ContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.UnsupportedMediaType;
                    return;
                }

                if (context.Request.ContentLength64 > MaxIPNBytes)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                byte[] rawBytes = ReadRequestBody(context.Request.InputStream, context.Request.ContentLength64, MaxIPNBytes);
                if (rawBytes.Length == 0)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                Encoding encoding = context.Request.ContentEncoding ?? Encoding.UTF8;
                string rawMessage = encoding.GetString(rawBytes);

                if (!TryValidateIPNPayload(rawMessage, out string transactionId, out bool duplicate, out string rejection))
                {
                    context.Response.StatusCode = duplicate ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
                    SEnvir.Log(LogLevel.Warning, LogCategory.Web, $"IPN request rejected: {rejection}");
                    return;
                }

                _ = Task.Run(() => VerifyIPNAsync(rawMessage, LiveURL, verified));

                context.Response.StatusCode = (int)HttpStatusCode.OK;
            }
            catch (ObjectDisposedException)
            {
            }
            catch (HttpListenerException ex)
            {
                if (IsCurrentListener(listener, IPNListener))
                    SEnvir.Log("IPN listener exception: " + ex);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                SEnvir.Log(LogLevel.Warning, LogCategory.Web, "IPN request rejected: " + ex.Message);
            }
            catch (Exception ex)
            {
                SEnvir.Log(LogLevel.Error, LogCategory.Web, ex.ToString());
                TryCloseResponse(context, HttpStatusCode.InternalServerError);
            }
            finally
            {
                TryCloseResponse(context);

                if (IsCurrentListener(listener, IPNListener)) //IsBound ?
                    listener.BeginGetContext(IPNConnection, listener);
            }
        }

        internal static bool TryValidateIPNPayload(string rawMessage, out string transactionId, out bool duplicate, out string rejection)
        {
            transactionId = null;
            duplicate = false;
            rejection = null;

            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                rejection = "empty body";
                return false;
            }

            var values = HttpUtility.ParseQueryString(rawMessage, Encoding.UTF8);
            string[] requiredFields =
            {
                "payment_status",
                "txn_id",
                "receiver_email",
                "mc_gross",
                "custom",
                "mc_currency",
                "txn_type",
            };

            foreach (string field in requiredFields)
            {
                if (!string.IsNullOrWhiteSpace(values[field])) continue;

                rejection = $"missing required field `{field}`";
                return false;
            }

            transactionId = values["txn_id"];
            CleanupRecentIPNIds();

            if (RecentIPNIds.TryAdd(transactionId, DateTime.UtcNow))
                return true;

            duplicate = true;
            rejection = $"duplicate queued txn_id `{transactionId}`";
            return false;
        }

        private static void CleanupRecentIPNIds()
        {
            DateTime threshold = DateTime.UtcNow - RecentIPNRetention;

            foreach (KeyValuePair<string, DateTime> pair in RecentIPNIds)
            {
                if (pair.Value >= threshold) continue;

                RecentIPNIds.TryRemove(pair.Key, out _);
            }
        }

        internal static void ClearRecentIPNIdsForTests()
        {
            RecentIPNIds.Clear();
        }

        private static async Task VerifyIPNAsync(string rawMessage, string liveUrl, string verifiedResponse)
        {
            try
            {
                string data = "cmd=_notify-validate&" + rawMessage;

                using HttpClientHandler handler = new HttpClientHandler
                {
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                                 | System.Security.Authentication.SslProtocols.Tls13
                };

                using HttpClient httpClient = new HttpClient(handler)
                {
                    Timeout = IPNVerificationTimeout
                };
                using StringContent content = new StringContent(data, Encoding.ASCII, "application/x-www-form-urlencoded");
                using HttpResponseMessage response = await httpClient.PostAsync(liveUrl, content);

                string verificationResult = (await response.Content.ReadAsStringAsync()).Trim();

                IPNMessage message = new IPNMessage
                {
                    Message = rawMessage,
                    Verified = string.Equals(verificationResult, verifiedResponse, StringComparison.Ordinal)
                };

                PersistIPNMessage(message);
            }
            catch (Exception ex)
            {
                SEnvir.Log("IPN verification exception: " + ex);
                TryPersistFailedIPN(rawMessage);
            }
        }

        private static void PersistIPNMessage(IPNMessage message)
        {
            Directory.CreateDirectory(VerifiedPath);
            Directory.CreateDirectory(InvalidPath);

            string path = Path.Combine(message.Verified ? VerifiedPath : InvalidPath, Path.GetRandomFileName());

            WriteAllTextAtomically(path, message.Message);

            message.FileName = path;

            Messages.Enqueue(message);
        }

        private static void WriteAllTextAtomically(string path, string text)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
            string tempPath = Path.Combine(tempDirectory, Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                File.WriteAllText(tempPath, text, new UTF8Encoding(false));
                ReplaceOrMoveTempFile(tempPath, fullPath);
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        private static void ReplaceOrMoveTempFile(string tempPath, string destinationPath)
        {
            if (File.Exists(destinationPath))
                File.Replace(tempPath, destinationPath, null);
            else
                File.Move(tempPath, destinationPath);
        }

        private static void TryDeleteTempFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }
        }

        private static void TryPersistFailedIPN(string rawMessage)
        {
            try
            {
                PersistIPNMessage(new IPNMessage
                {
                    Message = rawMessage,
                    Verified = false
                });
            }
            catch (Exception ex)
            {
                SEnvir.Log("IPN failure persistence exception: " + ex);
            }
        }

        public static void Process()
        {
            ConcurrentQueue<WebCommand> queue = WebCommandQueue;
            if (queue == null) return;

            while (queue.TryDequeue(out WebCommand webCommand))
            {
                switch (webCommand.Command)
                {
                    case CommandType.None:
                        break;
                    case CommandType.Activation:
                        webCommand.Account.Activated = true;
                        webCommand.Account.ActivationKey = string.Empty;
                        break;
                    case CommandType.PasswordReset:
                        string password = Functions.RandomString(SEnvir.Random, 10);

                        webCommand.Account.Password = SEnvir.CreateHash(password);
                        webCommand.Account.ResetKey = string.Empty;
                        webCommand.Account.WrongPasswordCount = 0;
                        EmailService.SendResetPasswordEmail(webCommand.Account, password);
                        break;
                    case CommandType.AccountDelete:
                        if (webCommand.Account.Activated) continue;

                        webCommand.Account.Delete();
                        break;
                }
            }
        }

        public static void Save()
        {
            lock (PaymentLock)
            {
                foreach (IPNMessage message in PaymentList)
                {
                    if (!HandledPayments.Contains(message))
                        HandledPayments.Add(message);
                }
            }
        }

        public static void CommitChanges(object data)
        {
            lock (PaymentLock)
            {
                foreach (IPNMessage message in HandledPayments.ToArray())
                {
                    try
                    {
                        if (message.Duplicate)
                        {
                            if (!string.IsNullOrWhiteSpace(message.FileName) && File.Exists(message.FileName))
                                File.Delete(message.FileName);

                            PaymentList.Remove(message);
                            continue;
                        }

                        Directory.CreateDirectory(CompletePath);

                        if (string.IsNullOrWhiteSpace(message.FileName) || !File.Exists(message.FileName))
                        {
                            SEnvir.Log("IPN file commit skipped because source file is missing: " + (message.FileName ?? "<empty>"));
                            PaymentList.Remove(message);
                            continue;
                        }

                        string fileName = Path.GetFileName(message.FileName);
                        string targetPath = Path.Combine(CompletePath, fileName + ".txt");
                        if (File.Exists(targetPath))
                            targetPath = Path.Combine(CompletePath, fileName + "." + Guid.NewGuid().ToString("N") + ".txt");

                        File.Move(message.FileName, targetPath);
                        PaymentList.Remove(message);
                    }
                    catch (Exception ex)
                    {
                        SEnvir.Log("IPN file commit exception: " + ex);
                    }
                }

                HandledPayments.Clear();
            }
        }

        public static void AddPaymentMessage(IPNMessage message)
        {
            if (message == null) return;

            lock (PaymentLock)
                PaymentList.Add(message);
        }

        private static void ClearPaymentLists()
        {
            lock (PaymentLock)
            {
                PaymentList.Clear();
                HandledPayments.Clear();
            }
        }

        private static void TryCloseResponse(HttpListenerContext context, HttpStatusCode? statusCode = null)
        {
            if (context == null) return;

            try
            {
                if (statusCode.HasValue)
                    context.Response.StatusCode = (int)statusCode.Value;

                context.Response.Close();
            }
            catch
            {
            }
        }

        private static bool IsCurrentListener(HttpListener listener, HttpListener current)
        {
            if (listener == null || !ReferenceEquals(listener, current)) return false;

            try
            {
                return listener.IsListening;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private static bool TryEnqueueWebCommand(WebCommand command)
        {
            ConcurrentQueue<WebCommand> queue = WebCommandQueue;
            if (queue == null) return false;

            queue.Enqueue(command);
            return true;
        }

        private static void CloseListener(HttpListener listener)
        {
            if (listener == null) return;

            try
            {
                listener.Close();
            }
            catch
            {
            }
        }
    }
}
