using Client.Controls;
using Client.Envir.Translations;
using Client.Models;
using Shared.Rendering;
using Client.Scenes;
using Client.Scenes.Views;
using Client.UserModels;
using Library;
using Library.Network;
using Library.SystemModels;
using MirDB;
using Sentry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using S = Library.Network.ServerPackets;

namespace Client.Envir
{
    public static class CEnvir
    {
        public static TargetForm Target;
        public static Random Random = new Random();

        private static DateTime _FPSTime;
        private static int FPSCounter;
        private static int FPSCount;
        private static readonly long LimitFPSTicks = Stopwatch.Frequency / 60;
        private static long _nextLimitedFrameTicks;

        public static int DPSCounter;
        private static int DPSCount;

        public static bool Shift, Alt, Ctrl;
        public static DateTime Now;
        public static Point MouseLocation;
        private static Point _lastMouseMoveEventLocation = new Point(int.MinValue, int.MinValue);
        private static MouseEventArgs _lastMouseMoveEventArgs;
        private static readonly StringBuilder DebugTextBuilder = new StringBuilder(256);

        public static CConnection Connection;
        public static bool WrongVersion;

        public static Dictionary<LibraryFile, MirLibrary> LibraryList = new Dictionary<LibraryFile, MirLibrary>();
        public static Dictionary<string, MirLibrary> DynamicLibraryList = new Dictionary<string, MirLibrary>(StringComparer.OrdinalIgnoreCase);
        private static readonly object DynamicLibraryLock = new object();
        public static List<string> DynamicLibraryDiagnostics = new List<string>();
        private static readonly object DynamicLibraryDiagnosticLock = new object();
        public static int DynamicLibrariesLoaded;
        public static int DynamicLibrariesDisposed;
        public static Dictionary<int, CustomSkillInfo> ClientCustomSkillOverrideList = new Dictionary<int, CustomSkillInfo>();
        private static readonly object ClientCustomSkillOverrideLock = new object();

        public static ClientUserItem[] Storage, MainStorage, PartsStorage, MainPartsStorage;

        public static List<ClientBlockInfo> BlockList = new List<ClientBlockInfo>();
        public static DBCollection<KeyBindInfo> KeyBinds;
        public static DBCollection<WindowSetting> WindowSettings;
        public static DBCollection<CastleInfo> CastleInfoList;
        public static MirDB.Session Session;
        public static string ClientSystemDatabaseVersion, ServerSystemDatabaseVersion;
        public static bool ClientSystemDatabaseExists;

        public static ConcurrentQueue<string> ChatLog = new ConcurrentQueue<string>();

        public static bool Loaded;
        public static bool DatabaseLoadAttempted;
        public static bool Loading { get; private set; }

        public static string BuyAddress;
        public static string C;

        public static bool TestServer;

        public static StringMessages Language { get; set; }

        public static bool TryGetCustomLibrary(CustomResourceLibraryInfo resource, out MirLibrary library)
        {
            library = null;

            if (resource == null || !resource.Enabled || string.IsNullOrWhiteSpace(resource.Key))
                return false;

            if (resource.LibraryFile != LibraryFile.None)
            {
                if (LibraryList.TryGetValue(resource.LibraryFile, out library) && library != null)
                    return true;

                AddDynamicLibraryDiagnostic($"自定义资源引用的客户端图库未加载: {resource.Key} -> {resource.LibraryFile}");

                if (string.IsNullOrWhiteSpace(resource.RelativePath))
                    return false;
            }

            if (string.IsNullOrWhiteSpace(resource.RelativePath))
            {
                AddDynamicLibraryDiagnostic($"自定义资源未配置 RelativePath 或 LibraryFile: {resource.Key}");
                return false;
            }

            if (!TryGetSafeCustomPath(resource.RelativePath, out string fullPath))
            {
                AddDynamicLibraryDiagnostic($"自定义资源 {resource.Key} 的路径无效或不在 Data\\Custom 下: {resource.RelativePath}");
                return false;
            }

            string cacheKey = GetCustomLibraryCacheKey(resource);

            lock (DynamicLibraryLock)
            {
                if (DynamicLibraryList.TryGetValue(cacheKey, out library))
                    return library != null;
            }

            if (!File.Exists(fullPath))
            {
                AddDynamicLibraryDiagnostic($"缺少自定义资源文件: {resource.Key} -> {resource.RelativePath}");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(resource.Hash) && !HashMatches(fullPath, resource.Hash))
                AddDynamicLibraryDiagnostic($"自定义资源 Hash 不匹配: {resource.Key} -> {resource.RelativePath}");

            try
            {
                MirLibrary loadedLibrary = new MirLibrary(fullPath);

                lock (DynamicLibraryLock)
                {
                    if (DynamicLibraryList.TryGetValue(cacheKey, out library))
                    {
                        loadedLibrary.Dispose();
                        return library != null;
                    }

                    library = loadedLibrary;
                    DynamicLibraryList[cacheKey] = library;
                    DynamicLibrariesLoaded++;
                }

                return true;
            }
            catch (Exception ex)
            {
                AddDynamicLibraryDiagnostic($"自定义资源加载失败: {resource.Key} -> {resource.RelativePath}, {ex.Message}");
                return false;
            }
        }

        public static bool TryGetCustomSkill(int magicInfoIndex, out CustomSkillInfo skill)
        {
            lock (ClientCustomSkillOverrideLock)
            {
                if (ClientCustomSkillOverrideList.TryGetValue(magicInfoIndex, out skill))
                    return skill?.Enabled == true;
            }

            skill = Globals.CustomSkillInfoList?.Binding.FirstOrDefault(x => x?.Magic?.Index == magicInfoIndex && x.Enabled);
            return skill != null;
        }

        public static void RegisterClientCustomSkillOverride(S.ClientCustomSkillInfo info)
        {
            if (info == null || info.MagicIndex <= 0) return;

            MagicInfo magic = Globals.MagicInfoList?.Binding.FirstOrDefault(x => x?.Index == info.MagicIndex);
            magic ??= info.ToMagicInfo();

            CustomSkillInfo skill = new CustomSkillInfo
            {
                Magic = magic,
                Template = info.Template,
                TargetRule = info.TargetRule,
                Enabled = info.Enabled,
                Range = info.Range,
                Radius = info.Radius,
                MaxTargets = info.MaxTargets,
                Shape = info.Shape,
                Length = info.Length,
                Width = info.Width,
                Angle = info.Angle,
                WaveCount = info.WaveCount <= 0 ? 1 : info.WaveCount,
                WaveInterval = info.WaveInterval,
                CastDelay = info.CastDelay <= 0 ? 500 : info.CastDelay,
                ProjectileSpeed = info.ProjectileSpeed <= 0 ? Globals.ProjectileSpeed : info.ProjectileSpeed,
            };

            skill.Effects = new DBBindingList<CustomSkillEffectInfo>(
                skill,
                typeof(CustomSkillInfo).GetProperty(nameof(CustomSkillInfo.Effects)));

            if (info.Effects != null)
                foreach (S.ClientCustomSkillEffectInfo source in info.Effects.Where(x => x != null))
                {
                    CustomSkillEffectInfo effect = new CustomSkillEffectInfo
                    {
                        Resource = CreateRuntimeCustomResource(source.Resource),
                        Stage = source.Stage,
                        StartIndex = source.StartIndex,
                        EndIndex = source.EndIndex,
                        FrameCount = source.FrameCount,
                        FrameDelay = source.FrameDelay <= 0 ? 100 : source.FrameDelay,
                        OffsetX = source.OffsetX,
                        OffsetY = source.OffsetY,
                        StartLight = source.StartLight,
                        EndLight = source.EndLight,
                        Sound = source.Sound,
                        Blend = source.Blend,
                        Loop = source.Loop,
                        Duration = source.Duration,
                        FollowTarget = source.FollowTarget,
                    };

                    skill.Effects.Add(effect);
                }

            lock (ClientCustomSkillOverrideLock)
            {
                ClientCustomSkillOverrideList[info.MagicIndex] = skill;
            }
        }

        public static void SyncClientCustomSkillOverride(int magicInfoIndex, S.ClientCustomSkillInfo info)
        {
            if (info != null)
            {
                RegisterClientCustomSkillOverride(info);
                return;
            }

            if (magicInfoIndex > 0)
                lock (ClientCustomSkillOverrideLock)
                {
                    ClientCustomSkillOverrideList[magicInfoIndex] = null;
                }
        }

        private static CustomResourceLibraryInfo CreateRuntimeCustomResource(S.ClientCustomResourceLibraryInfo info)
        {
            if (info == null) return null;

            return new CustomResourceLibraryInfo
            {
                Key = info.Key,
                DisplayName = info.DisplayName,
                RelativePath = info.RelativePath,
                LibraryFile = info.LibraryFile,
                Hash = info.Hash,
                Version = info.Version,
                Enabled = info.Enabled,
                Scope = info.Scope,
            };
        }

        private static string GetCustomLibraryCacheKey(CustomResourceLibraryInfo resource)
        {
            return string.Join("|",
                resource.Key?.Trim() ?? string.Empty,
                resource.RelativePath?.Trim() ?? string.Empty,
                resource.LibraryFile,
                resource.Hash?.Trim() ?? string.Empty,
                resource.Version?.Trim() ?? string.Empty);
        }

        private static bool TryGetSafeCustomPath(string relativePath, out string fullPath)
        {
            fullPath = null;

            try
            {
                if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                    return false;

                string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                if (normalized.Contains(".." + Path.DirectorySeparatorChar) || normalized.Equals("..", StringComparison.Ordinal))
                    return false;

                string customRoot = Path.GetFullPath(Path.Combine(Application.StartupPath, "Data", "Custom"));
                fullPath = Path.GetFullPath(Path.Combine(Application.StartupPath, normalized));

                return fullPath.StartsWith(customRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(fullPath, customRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
            {
                fullPath = null;
                return false;
            }
        }

        private static bool HashMatches(string fullPath, string expectedHash)
        {
            try
            {
                string expected = expectedHash.Trim();
                if (expected.Length != 64) return false;

                using SHA256 sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(File.ReadAllBytes(fullPath));
                string actual = BitConverter.ToString(hash).Replace("-", string.Empty);

                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static void AddDynamicLibraryDiagnostic(string message)
        {
            lock (DynamicLibraryDiagnosticLock)
            {
                if (DynamicLibraryDiagnostics.Contains(message)) return;
                DynamicLibraryDiagnostics.Add(message);
            }

            SaveDynamicLibraryDiagnostic(message);
        }

        private static void SaveDynamicLibraryDiagnostic(string message)
        {
            try
            {
                const string logPath = @".\Errors\";

                if (!Directory.Exists(logPath))
                    Directory.CreateDirectory(logPath);

                File.AppendAllText(Path.Combine(logPath, "DynamicLibraryDiagnostics.txt"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {LogText.WithCategory(LogCategory.CustomSystem, message)}{Environment.NewLine}");
            }
            catch
            {
            }
        }


        static CEnvir()
        {
            LoadLanguage();

            Thread workThread = new Thread(SaveChatLoop) { IsBackground = true };
            workThread.Start();

            try
            { A(); }
            catch { }
        }

        public static void Init(string[] args)
        {
            ProcessArgs(args);
        }

        private static void ProcessArgs(string[] args)
        {
            if (args == null || args.Length == 0) return;
        }

        public static void LoadLanguage()
        {
            switch (Config.Language.ToUpper())
            {
                case "ENGLISH":
                    Language = (StringMessages)ConfigReader.ConfigObjects[typeof(EnglishMessages)];
                    break;
                case "CHINESE":
                    Language = (StringMessages)ConfigReader.ConfigObjects[typeof(ChineseMessages)];
                    break;
            }

        }

        private static void A()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Zircon");

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            string checkSumPath = Path.Combine(path, "CheckSum.bin");
            if (File.Exists(checkSumPath))
            {
                using (BinaryReader E = new BinaryReader(File.OpenRead(checkSumPath)))
                    C = E.ReadString();
            }
            else
            {
                string generated = Functions.RandomString(Random, 20);
                WriteCheckSumAtomically(checkSumPath, generated);
                C = generated;
            }

        }

        private static void WriteCheckSumAtomically(string path, string value)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
            string tempPath = Path.Combine(tempDirectory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (BinaryWriter writer = new BinaryWriter(File.Create(tempPath)))
                    writer.Write(value);

                File.Move(tempPath, fullPath);
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
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

        public static void SaveChatLoop()
        {
            List<string> lines = new List<string>();
            while (true)
            {
                while (ChatLog.IsEmpty)
                    Thread.Sleep(1000);

                while (!ChatLog.IsEmpty)
                {
                    string line;

                    if (!ChatLog.TryDequeue(out line)) continue;

                    lines.Add(line);
                }

                try
                {
                    File.AppendAllLines(@".\Chat Logs.txt", lines);
                    lines.Clear();
                }
                catch
                {
                }
            }
        }

        public static void GameLoop()
        {
            if (RenderingPipelineManager.ApplyPendingPipelineSwitch())
                return;

            UpdateGame();
            RenderGame();

            if (Config.LimitFPS)
                LimitFrameRate();
            else
                _nextLimitedFrameTicks = 0;
        }

        private static void LimitFrameRate()
        {
            long now = Stopwatch.GetTimestamp();

            if (_nextLimitedFrameTicks == 0 || _nextLimitedFrameTicks < now - LimitFPSTicks)
                _nextLimitedFrameTicks = now + LimitFPSTicks;

            while (true)
            {
                now = Stopwatch.GetTimestamp();
                long remainingTicks = _nextLimitedFrameTicks - now;

                if (remainingTicks <= 0)
                    break;

                int remainingMilliseconds = (int)(remainingTicks * 1000 / Stopwatch.Frequency);

                if (remainingMilliseconds > 1)
                    Thread.Sleep(remainingMilliseconds - 1);
                else
                    Thread.Sleep(0);
            }

            _nextLimitedFrameTicks += LimitFPSTicks;
        }

        private static void UpdateGame()
        {
            Now = Time.Now;
            DXControl.ActiveScene?.OnMouseMove(GetMouseMoveEventArgs());

            if (Now >= _FPSTime)
            {
                _FPSTime = Now.AddSeconds(1);
                FPSCount = FPSCounter;
                FPSCounter = 0;
                DPSCount = DPSCounter;
                DPSCounter = 0;
                RenderingPipelineManager.MemoryClear();
            }

            Connection?.Process();
            DXControl.ActiveScene?.Process();

            UpdateDebugDisplay();

            if (DXControl.MouseControl != null && DXControl.ActiveScene != null)
            {
                DXControl.HintLabel.Text = DXControl.MouseControl.Hint;

                var c = DXControl.MouseControl;

                int x;
                int y;

                switch (DXControl.MouseControl.HintPosition)
                {
                    default:
                    case HintPosition.TopLeft:
                        x = c.DisplayArea.Left;
                        y = c.DisplayArea.Top - DXControl.HintLabel.Size.Height;
                        break;
                    case HintPosition.BottomLeft:
                        x = c.DisplayArea.Left;
                        y = c.DisplayArea.Bottom;
                        break;
                    case HintPosition.FixedY:
                        x = MouseLocation.X;
                        y = c.DisplayArea.Top - DXControl.HintLabel.Size.Height;
                        break;
                    case HintPosition.Fluid:
                        x = MouseLocation.X;
                        y = MouseLocation.Y + 17;
                        break;
                }

                Point location = new(x, y);

                if (location.X + DXControl.HintLabel.Size.Width > DXControl.ActiveScene.Size.Width)
                    location.X = DXControl.ActiveScene.Size.Width - DXControl.HintLabel.Size.Width - 1;

                if (location.Y + DXControl.HintLabel.Size.Height > DXControl.ActiveScene.Size.Height)
                    location.Y = DXControl.ActiveScene.Size.Height - DXControl.HintLabel.Size.Height - 1;

                if (location.X < 0) location.X = 0;
                if (location.Y < 0) location.Y = 0;

                DXControl.HintLabel.Location = location;
            }
            else
            {
                DXControl.HintLabel.Text = null;
            }
        }

        private static MouseEventArgs GetMouseMoveEventArgs()
        {
            if (_lastMouseMoveEventArgs != null && _lastMouseMoveEventLocation == MouseLocation)
                return _lastMouseMoveEventArgs;

            _lastMouseMoveEventLocation = MouseLocation;
            _lastMouseMoveEventArgs = new MouseEventArgs(MouseButtons.None, 0, MouseLocation.X, MouseLocation.Y, 0);

            return _lastMouseMoveEventArgs;
        }

        private static void UpdateDebugDisplay()
        {
            if (!Config.DebugLabel)
            {
                SetDebugLabelsVisible(false);

                if (!Config.FullScreen && !Config.Borderless)
                    SetTargetTitle(Globals.ClientName);

                return;
            }

            string debugText = BuildDebugText();
            string connectionText = BuildConnectionText();

            if (Config.FullScreen || Config.Borderless)
            {
                DXControl.DebugLabel.Text = debugText;
                DXControl.DebugLabel.IsVisible = true;
                DXControl.PingLabel.IsVisible = true;

                if (Connection != null)
                {
                    DXControl.PingLabel.Text = connectionText;
                    DXControl.PingLabel.Location = new Point(DXControl.DebugLabel.DisplayArea.Right + 5, DXControl.DebugLabel.DisplayArea.Y);
                }
                else
                {
                    DXControl.PingLabel.Text = string.Empty;
                }

                return;
            }

            SetTargetTitle($"{Globals.ClientName} - {debugText} - {connectionText}");
            SetDebugLabelsVisible(false);
        }

        private static string BuildDebugText()
        {
            StringBuilder builder = DebugTextBuilder;
            builder.Clear();
            builder.Append("FPS: ").Append(FPSCount);

            if (DXControl.MouseControl != null)
                builder.Append(", Mouse Control: ").Append(DXControl.MouseControl.GetType().Name);

            if (DXControl.FocusControl != null)
                builder.Append(", Focus Control: ").Append(DXControl.FocusControl.GetType().Name);

            builder.Append(", Mouse: ").Append(MouseLocation);

            if (GameScene.Game != null)
            {
                if (DXControl.MouseControl is MapControl)
                    builder.Append(", Co Ords: ").Append(GameScene.Game.MapControl.MapLocation);

                builder.Append(", Objects: ").Append(GameScene.Game.MapControl.Objects.Count);

                if (MapObject.MouseObject != null)
                    builder.Append(", Mouse Object: ").Append(MapObject.MouseObject.Name);
            }

            builder.Append(", DPS: ").Append(DPSCount);

            return builder.ToString();
        }

        private static string BuildConnectionText()
        {
            if (Connection == null)
                return string.Empty;

            return $"Ping: {Connection.Ping}, Sent: {FormatByteCount(Connection.TotalBytesSent)}, Received: {FormatByteCount(Connection.TotalBytesReceived)}, Caps: {Connection.ServerCapabilities}";
        }

        private static string FormatByteCount(decimal bytes)
        {
            const decimal KB = 1024;
            const decimal MB = KB * 1024;

            if (bytes > MB)
                return $"{bytes / MB:#,##0.0}MB";

            if (bytes > KB)
                return $"{bytes / KB:#,##0}KB";

            return $"{bytes:#,##0}B";
        }

        private static void SetDebugLabelsVisible(bool visible)
        {
            if (!visible)
            {
                DXControl.PingLabel.Text = string.Empty;
                DXControl.DebugLabel.Text = string.Empty;
            }

            if (Config.FullScreen || Config.Borderless)
            {
                DXControl.DebugLabel.IsVisible = visible;
                DXControl.PingLabel.IsVisible = visible;
                return;
            }

            DXControl.DebugLabel.Visible = visible;
            DXControl.PingLabel.Visible = visible;
        }

        private static void SetTargetTitle(string title)
        {
            if (Target == null || Target.Text == title) return;

            Target.Text = title;
        }

        private static void RenderGame()
        {
            if (Target.ClientSize.Width == 0 || Target.ClientSize.Height == 0)
            {
                Thread.Sleep(1);
                return;
            }

            static void drawScene() => DXControl.ActiveScene?.Draw();

            if (RenderingPipelineManager.RenderFrame(drawScene))
                FPSCounter++;
        }

        public static void ReturnToLogin()
        {
            if (DXControl.ActiveScene is LoginScene) return; // handle ?

            DXControl.ActiveScene.Dispose();
            DXSoundManager.StopAllSounds();
            DXControl.ActiveScene = new LoginScene(Config.IntroSceneSize);

            BlockList = new List<ClientBlockInfo>();
        }

        public static void LoadDatabase()
        {
            DatabaseLoadAttempted = true;
            Loaded = false;
            ClientSystemDatabaseVersion = null;
            ClientSystemDatabaseExists = false;

            Loading = true;
            Task.Run(() =>
            {
                try
                {
                    Session = new MirDB.Session(SessionMode.Users, @".\Data\") { BackUp = false };

                    Session.Initialize(
                        Assembly.GetAssembly(typeof(ItemInfo)),
                        Assembly.GetAssembly(typeof(WindowSetting))
                    );

                    ClientSystemDatabaseVersion = Session.SystemDatabaseVersion;
                    ClientSystemDatabaseExists = Session.SystemDatabaseExists;

                    if (!ClientSystemDatabaseExists) return;

                    Globals.ItemInfoList = Session.GetCollection<ItemInfo>();
                    Globals.MagicInfoList = Session.GetCollection<MagicInfo>();
                    Globals.MapInfoList = Session.GetCollection<MapInfo>();
                    Globals.CurrencyInfoList = Session.GetCollection<CurrencyInfo>();
                    Globals.InstanceInfoList = Session.GetCollection<InstanceInfo>();
                    Globals.NPCPageList = Session.GetCollection<NPCPage>();
                    Globals.MonsterInfoList = Session.GetCollection<MonsterInfo>();
                    Globals.FishingInfoList = Session.GetCollection<FishingInfo>();
                    Globals.StoreInfoList = Session.GetCollection<StoreInfo>();
                    Globals.NPCInfoList = Session.GetCollection<NPCInfo>();
                    Globals.MovementInfoList = Session.GetCollection<MovementInfo>();
                    Globals.QuestInfoList = Session.GetCollection<QuestInfo>();
                    Globals.QuestTaskList = Session.GetCollection<QuestTask>();
                    Globals.CompanionInfoList = Session.GetCollection<CompanionInfo>();
                    Globals.CompanionLevelInfoList = Session.GetCollection<CompanionLevelInfo>();
                    Globals.DisciplineInfoList = Session.GetCollection<DisciplineInfo>();
                    Globals.FameInfoList = Session.GetCollection<FameInfo>();
                    Globals.BundleInfoList = Session.GetCollection<BundleInfo>();
                    Globals.LootBoxInfoList = Session.GetCollection<LootBoxInfo>();
                    Globals.HelpInfoList = Session.GetCollection<HelpInfo>();
                    Globals.MilestoneInfoList = Session.GetCollection<MilestoneInfo>();
                    Globals.MilestoneTaskInfoList = Session.GetCollection<MilestoneInfoTask>();
                    Globals.CustomResourceLibraryInfoList = Session.GetCollection<CustomResourceLibraryInfo>();
                    Globals.CustomMonsterVisualInfoList = Session.GetCollection<CustomMonsterVisualInfo>();
                    Globals.CustomMonsterAnimationInfoList = Session.GetCollection<CustomMonsterAnimationInfo>();
                    Globals.CustomMonsterBehaviorInfoList = Session.GetCollection<CustomMonsterBehaviorInfo>();
                    Globals.CustomMonsterSkillInfoList = Session.GetCollection<CustomMonsterSkillInfo>();
                    Globals.CustomMonsterAiRuleInfoList = Session.GetCollection<CustomMonsterAiRuleInfo>();
                    Globals.CustomMonsterAiActionInfoList = Session.GetCollection<CustomMonsterAiActionInfo>();
                    Globals.CustomSkillInfoList = Session.GetCollection<CustomSkillInfo>();
                    Globals.CustomSkillEffectInfoList = Session.GetCollection<CustomSkillEffectInfo>();

                    KeyBinds = Session.GetCollection<KeyBindInfo>();
                    WindowSettings = Session.GetCollection<WindowSetting>();
                    CastleInfoList = Session.GetCollection<CastleInfo>();

                    Globals.GoldInfo = Globals.CurrencyInfoList.Binding.First(x => x.Type == CurrencyType.Gold).DropItem;

                    CheckKeyBinds();

                    IReadOnlyList<Size> supportedResolutions = RenderingPipelineManager.GetSupportedResolutions();

                    if (supportedResolutions.Count > 0)
                    {
                        if (!supportedResolutions.Contains(Config.GameSize))
                            Config.GameSize = supportedResolutions[0];
                    }
                    else
                    {
                        // Handle the case where no valid resolutions are found.
                        // For example, set a default resolution.
                        Config.GameSize = new Size(1024, 768);  // Default resolution
                    }

                    Loaded = true;
                }
                finally
                {
                    Loading = false;
                }
            });
        }

        public static IEnumerable<KeyBindAction> GetKeyAction(Keys key)
        {
            if (!Loaded) yield break;

            switch (key)
            {
                case Keys.NumPad0:
                    key = Keys.D0;
                    break;
                case Keys.NumPad1:
                    key = Keys.D1;
                    break;
                case Keys.NumPad2:
                    key = Keys.D2;
                    break;
                case Keys.NumPad3:
                    key = Keys.D3;
                    break;
                case Keys.NumPad4:
                    key = Keys.D4;
                    break;
                case Keys.NumPad5:
                    key = Keys.D5;
                    break;
                case Keys.NumPad6:
                    key = Keys.D6;
                    break;
                case Keys.NumPad7:
                    key = Keys.D7;
                    break;
                case Keys.NumPad8:
                    key = Keys.D8;
                    break;
                case Keys.NumPad9:
                    key = Keys.D9;
                    break;
            }

            foreach (KeyBindInfo bind in KeyBinds.Binding)
            {
                if ((bind.Control1 == Ctrl && bind.Alt1 == Alt && bind.Shift1 == Shift && bind.Key1 == key) ||
                    (bind.Control2 == Ctrl && bind.Alt2 == Alt && bind.Shift2 == Shift && bind.Key2 == key))
                    yield return bind.Action;
            }
        }

        public static string GetKeyBindLabel(KeyBindAction action)
        {
            var bind = KeyBinds.Binding.FirstOrDefault(x => x.Action == action);

            if (bind == null)
            {
                return Keys.None.ToString();
            }

            string text = "";
            if (bind.Control1)
                text += "Ctrl + ";

            if (bind.Alt1)
                text += "Alt + ";

            if (bind.Shift1)
                text += "Shift + ";

            text += CEnvir.GetText(bind.Key1);

            if (bind.Key2 != Keys.None)
            {
                text += ", ";
                if (bind.Control2)
                    text += "Ctrl + ";

                if (bind.Alt2)
                    text += "Alt + ";

                if (bind.Shift2)
                    text += "Shift + ";

                text += CEnvir.GetText(bind.Key2);
            }

            return text;
        }

        public static void FillStorage(List<ClientUserItem> items, bool observer)
        {
            Storage = new ClientUserItem[1000];
            PartsStorage = new ClientUserItem[1000];

            if (!observer)
            {
                MainStorage = Storage;
                MainPartsStorage = PartsStorage;
            }

            foreach (ClientUserItem item in items)
                if (item.Slot >= 2000)
                    PartsStorage[item.Slot - Globals.PartsStorageOffset] = item;
                else
                    Storage[item.Slot] = item;
        }
        public static void Enqueue(Packet packet)
        {
            Connection?.Enqueue(packet);
        }

        public static void ResetKeyBinds()
        {
            for (int i = KeyBinds.Count - 1; i >= 0; i--)
                KeyBinds[i].Delete();

            CheckKeyBinds();
        }
        public static void CheckKeyBinds()
        {
            foreach (KeyBindAction action in Enum.GetValues(typeof(KeyBindAction)).Cast<KeyBindAction>())
            {
                switch (action)
                {
                    case KeyBindAction.None:
                        break;
                    default:
                        if (KeyBinds.Binding.Any(x => x.Action == action)) continue;

                        ResetKeyBind(action);
                        break;
                }
            }
        }

        public static void ResetKeyBind(KeyBindAction action)
        {
            KeyBindInfo bind = KeyBinds.CreateNewObject();
            bind.Action = action;

            switch (action)
            {
                case KeyBindAction.MenuWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.N;
                    break;
                case KeyBindAction.HelpWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.H;
                    break;
                case KeyBindAction.ConfigWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.O;
                    break;
                case KeyBindAction.CharacterWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.Q;
                    break;
                case KeyBindAction.InventoryWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.W;
                    break;
                case KeyBindAction.MagicWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.E;
                    break;
                case KeyBindAction.MagicBarWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.E;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.RankingWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.R;
                    break;
                case KeyBindAction.GameStoreWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.Y;
                    break;
                case KeyBindAction.DungeonFinderWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.D;
                    bind.Shift1 = true;
                    break;
                case KeyBindAction.CompanionWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.U;
                    break;
                case KeyBindAction.HeroWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.I;
                    break;
                case KeyBindAction.GroupWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.P;
                    break;
                case KeyBindAction.AutoPotionWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.A;
                    break;
                case KeyBindAction.StorageWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.S;
                    break;
                case KeyBindAction.BlockListWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.F;
                    break;
                case KeyBindAction.GuildWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.G;
                    break;
                case KeyBindAction.QuestLogWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.J;
                    break;
                case KeyBindAction.QuestTrackerWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.L;
                    break;
                case KeyBindAction.BeltWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.Z;
                    break;
                case KeyBindAction.MarketPlaceWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.C;
                    break;
                case KeyBindAction.MapMiniWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.V;
                    break;
                case KeyBindAction.MapBigWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.B;
                    break;
                case KeyBindAction.MailBoxWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.Oemcomma;
                    break;
                case KeyBindAction.MailSendWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.OemPeriod;
                    break;
                case KeyBindAction.ChatOptionsWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.O;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.ExitGameWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.Q;
                    bind.Alt1 = true;
                    bind.Key2 = Keys.X;
                    bind.Alt2 = true;
                    break;
                case KeyBindAction.ChangeAttackMode:
                    bind.Category = "Functions";
                    bind.Key1 = Keys.H;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.ChangePetMode:
                    bind.Category = "Functions";
                    bind.Key1 = Keys.A;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.GroupAllowSwitch:
                    bind.Category = "Functions";
                    bind.Key1 = Keys.P;
                    bind.Alt1 = true;
                    break;
                case KeyBindAction.GroupTarget:
                    bind.Category = "Functions";
                    bind.Key1 = Keys.G;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.TradeRequest:
                    bind.Category = "Functions";
                    bind.Key1 = Keys.T;
                    break;
                case KeyBindAction.TradeAllowSwitch:
                    bind.Category = "Functions";
                    bind.Key1 = Keys.T;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.MountToggle:
                    bind.Category = "Functions";
                    bind.Key1 = Keys.M;
                    break;
                case KeyBindAction.AutoRunToggle:
                    bind.Category = "Functions";
                    bind.Key1 = Keys.D;
                    break;
                case KeyBindAction.AutoFight:
                    bind.Category = "Functions";
                    bind.Key1 = Keys.D;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.ChangeChatMode:
                    bind.Category = "Functions";
                    bind.Key1 = Keys.K;
                    break;
                case KeyBindAction.ItemPickUp:
                    bind.Category = "Items";
                    bind.Key1 = Keys.Tab;
                    break;
                case KeyBindAction.PartnerTeleport:
                    bind.Category = "Items";
                    bind.Key1 = Keys.Z;
                    bind.Shift1 = true;
                    break;
                case KeyBindAction.ToggleItemLock:
                    bind.Category = "Items";
                    bind.Key1 = Keys.Scroll;
                    break;
                case KeyBindAction.UseBelt01:
                    bind.Category = "Items";
                    bind.Key1 = Keys.D1;
                    bind.Key2 = Keys.D1;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.UseBelt02:
                    bind.Category = "Items";
                    bind.Key1 = Keys.D2;
                    bind.Key2 = Keys.D2;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.UseBelt03:
                    bind.Category = "Items";
                    bind.Key1 = Keys.D3;
                    bind.Key2 = Keys.D3;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.UseBelt04:
                    bind.Category = "Items";
                    bind.Key1 = Keys.D4;
                    bind.Key2 = Keys.D4;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.UseBelt05:
                    bind.Category = "Items";
                    bind.Key1 = Keys.D5;
                    bind.Key2 = Keys.D5;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.UseBelt06:
                    bind.Category = "Items";
                    bind.Key1 = Keys.D6;
                    bind.Key2 = Keys.D6;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.UseBelt07:
                    bind.Category = "Items";
                    bind.Key1 = Keys.D7;
                    bind.Key2 = Keys.D7;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.UseBelt08:
                    bind.Category = "Items";
                    bind.Key1 = Keys.D8;
                    bind.Key2 = Keys.D8;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.UseBelt09:
                    bind.Category = "Items";
                    bind.Key1 = Keys.D9;
                    bind.Key2 = Keys.D9;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.UseBelt10:
                    bind.Category = "Items";
                    bind.Key1 = Keys.D0;
                    bind.Key2 = Keys.D0;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellSet01:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F1;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.SpellSet02:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F2;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.SpellSet03:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F3;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.SpellSet04:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F4;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.SpellUse01:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F1;
                    bind.Key2 = Keys.F1;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellUse02:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F2;
                    bind.Key2 = Keys.F2;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellUse03:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F3;
                    bind.Key2 = Keys.F3;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellUse04:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F4;
                    bind.Key2 = Keys.F4;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellUse05:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F5;
                    bind.Key2 = Keys.F5;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellUse06:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F6;
                    bind.Key2 = Keys.F6;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellUse07:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F7;
                    bind.Key2 = Keys.F7;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellUse08:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F8;
                    bind.Key2 = Keys.F8;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellUse09:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F9;
                    bind.Key2 = Keys.F9;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellUse10:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F10;
                    bind.Key2 = Keys.F10;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellUse11:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F11;
                    bind.Key2 = Keys.F11;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellUse12:
                    bind.Category = "Magic";
                    bind.Key1 = Keys.F12;
                    bind.Key2 = Keys.F12;
                    bind.Shift2 = true;
                    break;
                case KeyBindAction.SpellUse13:
                    bind.Category = "Magic";
                    break;
                case KeyBindAction.SpellUse14:
                    bind.Category = "Magic";
                    break;
                case KeyBindAction.SpellUse15:
                    bind.Category = "Magic";
                    break;
                case KeyBindAction.SpellUse16:
                    bind.Category = "Magic";
                    break;
                case KeyBindAction.SpellUse17:
                    bind.Category = "Magic";
                    break;
                case KeyBindAction.SpellUse18:
                    bind.Category = "Magic";
                    break;
                case KeyBindAction.SpellUse19:
                    bind.Category = "Magic";
                    break;
                case KeyBindAction.SpellUse20:
                    bind.Category = "Magic";
                    break;
                case KeyBindAction.SpellUse21:
                    bind.Category = "Magic";
                    break;
                case KeyBindAction.SpellUse22:
                    bind.Category = "Magic";
                    break;
                case KeyBindAction.SpellUse23:
                    bind.Category = "Magic";
                    break;
                case KeyBindAction.SpellUse24:
                    bind.Category = "Magic";
                    break;
                case KeyBindAction.FortuneWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.W;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.CurrencyWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.C;
                    bind.Control1 = true;
                    break;
                case KeyBindAction.FilterDropWindow:
                    bind.Category = "Windows";
                    bind.Key1 = Keys.F;
                    bind.Control1 = true;
                    break;
            }
        }

        public static float FontSize(float size)
        {
            return (size - Config.FontSizeMod) * (96F / RenderingPipelineManager.GetHorizontalDpi());
        }

        public static int ErrorCount;
        private static string LastError;
        public static void SaveError(string ex, LogCategory category = LogCategory.General)
        {
            try
            {
                if (++ErrorCount > 20 || String.Compare(ex, LastError, StringComparison.OrdinalIgnoreCase) == 0) return;

                const string LogPath = @".\Errors\";

                LastError = ex;
                string message = LogText.WithCategory(category, LastError);
                string state = $"State = {Target?.DisplayRectangle}";

                if (!Directory.Exists(LogPath))
                    Directory.CreateDirectory(LogPath);

                File.AppendAllText($"{LogPath}{Now.Year}-{Now.Month}-{Now.Day}.txt", message + Environment.NewLine + state + Environment.NewLine);
            }
            catch
            { }
        }

        public static void SaveException(Exception ex)
        {
            SaveError(ex.ToString());
            if (Config.SentryEnabled) SentrySdk.CaptureException(ex);
        }

        public static void Unload()
        {
            CConnection con = Connection;

            Connection = null;

            con?.Disconnect();

            List<MirLibrary> dynamicLibraries;
            lock (DynamicLibraryLock)
            {
                dynamicLibraries = DynamicLibraryList.Values.Where(x => x != null).ToList();
                DynamicLibraryList.Clear();
            }

            foreach (MirLibrary library in dynamicLibraries)
            {
                library.Dispose();
                DynamicLibrariesDisposed++;
            }

            if (dynamicLibraries.Count > 0)
                AddDynamicLibraryDiagnostic($"客户端卸载时释放动态图库: {dynamicLibraries.Count}, 累计释放: {DynamicLibrariesDisposed}");

            lock (DynamicLibraryDiagnosticLock)
            {
                DynamicLibraryDiagnostics.Clear();
            }

            lock (ClientCustomSkillOverrideLock)
            {
                ClientCustomSkillOverrideList.Clear();
            }
        }
        public static KeyBindInfo GetKeyBind(KeyBindAction action)
        {
            return KeyBinds.Binding.FirstOrDefault(x => x.Action == action);
        }
        public static string GetText(Keys key)
        {
            switch (key)
            {
                case Keys.None:
                    return string.Empty;
                case Keys.Back:
                    return "Backspace";
                case Keys.Capital:
                    return "Cap Lock";
                case Keys.Scroll:
                    return "Scroll Lock";
                case Keys.NumLock:
                    return "Num Lock";
                case Keys.Prior:
                    return "Page Up";
                case Keys.Next:
                    return "Page Down";
                case Keys.Multiply:
                    return "Num Pad *";
                case Keys.Add:
                    return "Num Pad +";
                case Keys.Subtract:
                    return "Num Pad -";
                case Keys.Decimal:
                    return "Num Pad .";
                case Keys.Divide:
                    return "Num Pad /";
                case Keys.OemSemicolon:
                    return ";";
                case Keys.Oemplus:
                    return "=";
                case Keys.Oemcomma:
                    return ",";
                case Keys.OemMinus:
                    return "-";
                case Keys.OemPeriod:
                    return ".";
                case Keys.OemQuestion:
                    return "/";
                case Keys.Oemtilde:
                    return "'";
                case Keys.OemOpenBrackets:
                    return "[";
                case Keys.OemCloseBrackets:
                    return "]";
                case Keys.OemQuotes:
                    return "#";
                case Keys.Oem8:
                    return "`";
                case Keys.OemBackslash:
                    return "\\";
                case Keys.D1:
                    return "1";
                case Keys.D2:
                    return "2";
                case Keys.D3:
                    return "3";
                case Keys.D4:
                    return "4";
                case Keys.D5:
                    return "5";
                case Keys.D6:
                    return "6";
                case Keys.D7:
                    return "7";
                case Keys.D8:
                    return "8";
                case Keys.D9:
                    return "9";
                case Keys.D0:
                    return "0";
                default:
                    return key.ToString();
            }
        }

        public static bool IsCurrencyItem(ItemInfo info)
        {
            if (info == null || Globals.CurrencyInfoList?.Binding == null) return false;

            foreach (CurrencyInfo currency in Globals.CurrencyInfoList.Binding)
                if (currency.DropItem == info)
                    return true;

            return false;
        }

        public static int CurrencyImage(ItemInfo info, long count)
        {
            if (info == null || Globals.CurrencyInfoList?.Binding == null) return 0;

            CurrencyInfo currency = null;

            foreach (CurrencyInfo candidate in Globals.CurrencyInfoList.Binding)
            {
                if (candidate.DropItem != info) continue;

                currency = candidate;
                break;
            }

            if (currency == null)
                return 0;

            CurrencyInfoImage image = null;

            foreach (CurrencyInfoImage candidate in currency.Images)
            {
                if (candidate.Amount > count) continue;

                if (image == null || candidate.Amount > image.Amount)
                    image = candidate;
            }

            return image?.Image ?? currency.DropItem.Image;
        }
    }
}
