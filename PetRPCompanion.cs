using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PetRPCompanion
{
    public sealed class GlanceSlotData
    {
        public bool Active;
        public string Icon = "";
        public string Title = "";
        public string Text = "";
    }

    public sealed class CompanionProfile
    {
        public string BoundName = "";
        public string RPName = "";
        public string NameHex = "";
        public string Title = "";
        public string Icon = "";
        public GlanceSlotData[] GlanceSlots = new GlanceSlotData[5]
        {
            new GlanceSlotData(), new GlanceSlotData(), new GlanceSlotData(), new GlanceSlotData(), new GlanceSlotData()
        };
        public string CleanAbout = "";
        public string AboutImageTag = "";

        public string GetGlancesSummary()
        {
            List<string> active = new List<string>();
            foreach (GlanceSlotData g in GlanceSlots)
            {
                if (g != null && g.Active && (!string.IsNullOrEmpty(g.Title) || !string.IsNullOrEmpty(g.Text)))
                    active.Add(string.IsNullOrEmpty(g.Text) ? g.Title : (g.Title + ": " + g.Text));
            }
            return active.Count > 0 ? string.Join("; ", active.ToArray()) : "None";
        }

        public string ToPersonaString()
        {
            return string.Format("RP Name: {0} | Title: {1} | At First Glance Traits: {2} | About/Backstory: {3}",
                string.IsNullOrEmpty(RPName) ? BoundName : RPName,
                string.IsNullOrEmpty(Title) ? "None" : Title,
                GetGlancesSummary(),
                string.IsNullOrEmpty(CleanAbout) ? "None" : CleanAbout);
        }
    }

    public sealed class PetMessagePools
    {
        public readonly List<string> Reactive = new List<string>();
        public readonly List<string> Emotes = new List<string>();
        public readonly List<string> Says = new List<string>();
        public readonly List<string> DefaultEmotes = new List<string>();
        public readonly List<string> DefaultSays = new List<string>();
        public bool DefaultsGenerated = true;
        public int IntervalSeconds = 180;
    }

    public sealed class ModelOption
    {
        public string ModelId;
        public string DisplayLabel;
        public override string ToString() { return DisplayLabel; }
    }

    public sealed class UsageStatsSummary
    {
        public int ProMessagesUsed;
        public int ProTokensUsed;
        public int FreeMessagesUsed;
        public int FreeTokensUsed;
        public int ProQuotaLimit = 500;
        public string LiveCliQuotaReport = "";
    }

    public sealed class TRP3BorderPanel : Panel
    {
        public Color OuterGold = Color.FromArgb(178, 142, 68);
        public Color InnerBevel = Color.FromArgb(78, 58, 28);
        public bool DrawCornerRivets = true;

        public TRP3BorderPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int w = Width - 1;
            int h = Height - 1;
            if (w <= 4 || h <= 4) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pInner = new Pen(InnerBevel, 1f))
            using (Pen pOuter = new Pen(OuterGold, 1.5f))
            {
                e.Graphics.DrawRectangle(pInner, 2, 2, w - 4, h - 4);
                e.Graphics.DrawRectangle(pOuter, 0, 0, w, h);
            }
            if (!DrawCornerRivets) return;
            using (Brush rivetBrush = new SolidBrush(Color.FromArgb(225, 188, 95)))
            {
                e.Graphics.FillEllipse(rivetBrush, 2, 2, 4, 4);
                e.Graphics.FillEllipse(rivetBrush, w - 6, 2, 4, 4);
                e.Graphics.FillEllipse(rivetBrush, 2, h - 6, 4, 4);
                e.Graphics.FillEllipse(rivetBrush, w - 6, h - 6, 4, 4);
            }
        }
    }

    public sealed class MainForm : Form
    {
        [DllImport("gdi32.dll", EntryPoint = "AddFontResourceEx", SetLastError = true)]
        private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

        private const string NO_PET_LABEL = "(No Pet Loaded)";

        private static bool TryReadWoWBeaconPixel(IntPtr wowHwnd, out Color pixelColor)
        {
            pixelColor = Color.Empty;
            if (wowHwnd == IntPtr.Zero) return false;
            try
            {
                Point pt = new Point(1, 1);
                if (!ClientToScreen(wowHwnd, ref pt)) return false;
                using (Bitmap bmp = new Bitmap(1, 1))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(pt.X, pt.Y, 0, 0, new Size(1, 1));
                    }
                    pixelColor = bmp.GetPixel(0, 0);
                    return true;
                }
            }
            catch { return false; }
        }

        private static int ComputePetBeaconByte(string nameStr)
        {
            if (string.IsNullOrEmpty(nameStr)) return 0;
            string s = nameStr.ToLowerInvariant();
            int sum = 0;
            for (int i = 0; i < s.Length; i++)
            {
                sum = (sum * 31 + ((int)s[i] & 0xFF)) % 231;
            }
            return 20 + sum;
        }

        private const uint FR_PRIVATE = 0x10;
        private const int MAX_IC_CONTEXT_MESSAGES = 30;
        private const int SMUGGLE_SIGNAL_THRESHOLD = 1;
        private const string HTTP_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

        private static readonly PrivateFontCollection privateFonts = new PrivateFontCollection();
        private static FontFamily openDyslexicFamily;
        private static string resolvedFontName = "OpenDyslexic";

        private static readonly Color TRP3_BG_DARK = Color.FromArgb(20, 15, 11);
        private static readonly Color TRP3_HEADER_BG = Color.FromArgb(38, 27, 18);
        private static readonly Color TRP3_PANEL_BG = Color.FromArgb(30, 22, 16);
        private static readonly Color TRP3_SLOT_BG = Color.FromArgb(24, 18, 13);
        private static readonly Color TRP3_INSET_BG = Color.FromArgb(15, 11, 8);
        private static readonly Color TRP3_GOLD = Color.FromArgb(255, 209, 0);
        private static readonly Color TRP3_GOLD_SOFT = Color.FromArgb(228, 192, 110);
        private static readonly Color TRP3_BRONZE = Color.FromArgb(172, 136, 64);
        private static readonly Color TRP3_IVORY = Color.FromArgb(242, 232, 212);
        private static readonly Color TRP3_MUTED = Color.FromArgb(150, 135, 112);
        private static readonly Color TRP3_CRIMSON = Color.FromArgb(115, 22, 18);
        private static readonly Color TRP3_GREEN = Color.FromArgb(45, 225, 85);

        private const string REACTIVE_PROMPT =
            "You are an immersive WoW companion pet. Deeply embody the personality, species, quirks, 'At First Glance' traits, and 'About' description from your TRP3 Companion Profile below. Generate a 1-sentence physical emote reacting to the chat. Format strictly as a physical description without quotes or speaking.";
        private const string IDLE_EMOTE_PROMPT =
            "You are an immersive WoW companion pet. Deeply embody the personality, quirks, 'At First Glance' traits, and 'About' description from your TRP3 Companion Profile below. Generate a 1-sentence idle physical emote reflecting your unique character. Format strictly as a physical description without quotes or speaking.";
        private const string IDLE_SAY_PROMPT =
            "You are an immersive WoW companion pet. Deeply embody the personality, voice, 'At First Glance' traits, and 'About' description from your TRP3 Companion Profile below. Generate a 1-sentence vocalization or short spoken line fitting how your character communicates. Output ONLY the spoken text itself without surrounding quotes.";
        private const string TEST_PROMPT =
            "You are an immersive WoW companion pet. Deeply embody the personality, voice, 'At First Glance' traits, and 'About' description from your TRP3 Companion Profile below. Generate a fun 1-sentence random in-character action or vocalization showing off your personality. Output ONLY the 1-sentence reaction.";

        private static readonly string[] DEFAULT_PET_EMOTES = new string[]
        {
            "tilts its head curiously and watches everyone nearby.",
            "perks up suddenly, scanning the area with bright eyes.",
            "circles around its master's heels before settling down.",
            "stretches lazily from head to toe and gives a content shake.",
            "sniffs at the air inquisitively, catching an unfamiliar scent.",
            "chases a drifting speck of dust in a tight little circle.",
            "nudges gently against its master's leg for attention.",
            "peers intently at a passing adventurer with undisguised curiosity.",
            "bounces in place with a sudden burst of playful energy.",
            "settles comfortably and observes the surroundings with quiet vigilance.",
            "cocks its head to one side as if listening to a distant whisper.",
            "pats softly at the ground while keeping watch over the group.",
            "fluffs up proudly and struts a few paces forward.",
            "blinks slowly, looking thoroughly pleased with itself.",
            "darts a quick glance toward the horizon before returning to its master's side.",
            "sways gently in rhythm with the ambient sounds of the world.",
            "paws playfully at a loose pebble on the ground.",
            "stands tall and alert, mimicking a miniature guardian.",
            "leans in close to inspect a nearby shadow before hopping back.",
            "gives a spirited little hop, eager to keep moving.",
            "curls up briefly, then pops right back up as if refusing to miss anything.",
            "watches the conversation intently, shifting its gaze from speaker to speaker.",
            "lets out a tiny puff of breath and wiggles with excitement.",
            "sidles closer to its master, radiating loyal companionship.",
            "traces a small figure-eight pattern around its master's boots.",
            "twitches attentively at every sudden sound in the distance.",
            "performs a quick, theatrical spin before freezing in a proud pose.",
            "rubs its cheek affectionately against its master's knee.",
            "peers up at its master as though waiting for a secret signal.",
            "keeps a watchful, mischievous gaze fixed on the nearest shiny object."
        };

        private static readonly string[] DEFAULT_PET_SAYS = new string[]
        {
            "*lets out a soft, inquisitive chirp.*",
            "*makes a cheerful little trill of approval.*",
            "*murmurs softly, eager for the next adventure.*",
            "*chitters quietly as if sharing a secret observation.*",
            "*coos with unmistakable contentment.*",
            "*pipes up with a brief, melodic note.*",
            "*hums a faint, rhythmic little tune under its breath.*",
            "*squeaks excitedly while bouncing on its heels.*",
            "*purrs with a low, soothing resonance.*",
            "*yips softly to remind everyone it is still supervising.*",
            "*makes an amused little snort at the ongoing conversation.*",
            "*whistles a short, curious two-tone call.*",
            "*clicks and chirps in animated agreement.*",
            "*lets out a dramatic little sigh, demanding attention.*",
            "*burbles happily, completely in its element.*",
            "*chatters softly at the surrounding scenery.*",
            "*gives a bright, encouraging peep.*",
            "*rumbles with quiet, pint-sized determination.*",
            "*trills a playful greeting to those nearby.*",
            "*mutters a tiny, mischievous sound.*",
            "*warbles a sweet, lingering note.*",
            "*lets out an expectant little hum, watching its master closely.*",
            "*chirrups with sudden enthusiasm!*",
            "*makes a sleepy, contented little murmur.*",
            "*offers a crisp, attentive vocalization.*",
            "*chuckles in its own tiny, unmistakable way.*",
            "*whispers a faint, echoing sound into the breeze.*",
            "*peeps inquisitively, tilting its head for an answer.*",
            "*buzzes with barely contained excitement.*",
            "*croons a gentle, reassuring note from beside its master.*"
        };

        private readonly string GEN_LOG_PATH = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watcher-gen.txt");
        private readonly string SETTINGS_PATH = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "petrp-settings.txt");
        private readonly string ICON_CACHE_DIR = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon_cache");
        private readonly string LOCAL_RESPONSE_PATH = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PetRP", "response.lua");
        private readonly string LOCAL_SIGNAL_SOUND_PATH = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PetRP", "ping.mp3");

        private string wowDir = "";
        private string customWowDirOverride = "";
        private string LogPath
        {
            get { return string.IsNullOrEmpty(wowDir) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WoWChatLog.txt") : Path.Combine(wowDir, "Logs", "WoWChatLog.txt"); }
        }
        private string ResponsePath
        {
            get { return string.IsNullOrEmpty(wowDir) ? LOCAL_RESPONSE_PATH : Path.Combine(wowDir, "Interface", "AddOns", "PetRP", "response.lua"); }
        }
        private string SignalSoundPath
        {
            get { return string.IsNullOrEmpty(wowDir) ? LOCAL_SIGNAL_SOUND_PATH : Path.Combine(wowDir, "Interface", "AddOns", "PetRP", "ping.mp3"); }
        }

        private TRP3BorderPanel pnlHeaderPlaque, pnlAccountBanner, sheetPanel, aboutBox, ctrlBar, logFrame, pnlMessagesTab;
        private Panel pnlMainBody, pnlTabBar;
        private Label lblHeaderTitle, lblAccountBanner, lblPetHeadline, lblPetSubtitle, lblGlanceSection, lblAboutSection, lblPet, lblMod, lblInt, lblIntUnit, lblStats;
        private Label lblDefaultsStatus, lblFilterCat, lblEditCat, lblEditPrompt;
        private Button btnOptions, btnToggleAccount, btnUsage, btnStartStop, btnGenNow, btnTestLLM, btnReloadTRP3, btnOpenGenLog;
        private Button btnTabProfile, btnTabMessages, btnRegenDefaults, btnAddMsg, btnSaveEditMsg, btnDeleteMsg;
        private PictureBox picPetIcon, picAboutExtractedImg;
        private TextBox txtAboutBody, txtHistory, txtEditMessage;
        private ComboBox cmbPetSelect, cmbModel, cmbMsgFilter, cmbEditKind;
        private ListBox lstStoredMessages;
        private NumericUpDown numInterval;
        private ToolTip trpTooltip;
        private int activeTabIndex;

        private readonly TRP3BorderPanel[] glanceBoxes = new TRP3BorderPanel[5];
        private readonly PictureBox[] glanceIcons = new PictureBox[5];
        private readonly Label[] glanceTitles = new Label[5];
        private readonly TextBox[] glanceTexts = new TextBox[5];

        private readonly Dictionary<string, CompanionProfile> profiles = new Dictionary<string, CompanionProfile>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> savedIconNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> seenPetsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> masterNamesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> libRpMediaIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PetMessagePools> petPoolsMap = new Dictionary<string, PetMessagePools>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> pendingSmuggleItems = new List<string>();
        private readonly object poolLock = new object();

        private bool isWatching;
        private bool forceSyncHighlight;
        private int currentSyncSeq = 100;
        private int unsyncedStartSeq;
        private string inGamePetStatus = "Unknown (Waiting for WoW)";
        private string inGameActivePet = "";
        private int lastBeaconStateCode = -1;
        private CancellationTokenSource cts;
        private int totalMessages, promptTokens, outputTokens, syncTriggerCount;
        private string lastSyncTriggerText = "None yet";
        private DateTime lastSyncDeliveredUtc = DateTime.MinValue;
        private int periodicGenCycle = 1; // 1 = Emote, 2 = Say, 3 = Sound Emote
        private string reactiveBuf = "", periodicEmoteBuf = "", periodicSayBuf = "";
        private string detectedGoogleAccount = "";
        private string resolvedAgyExecutable = "agy";
        private int configuredProQuota = 500;
        private bool hideAccountBanner;
        private float configuredFontSize = 10.0f;

        private string GetCanonicalPetKey(string rawPetName)
        {
            if (string.IsNullOrWhiteSpace(rawPetName)) return "";
            string trimmed = rawPetName.Trim();
            if (string.Equals(trimmed, NO_PET_LABEL, StringComparison.OrdinalIgnoreCase))
                return "";
            CompanionProfile cp;
            if (profiles.TryGetValue(trimmed, out cp) && !string.IsNullOrEmpty(cp.BoundName))
                return cp.BoundName;
            foreach (KeyValuePair<string, CompanionProfile> kv in profiles)
            {
                if (!string.IsNullOrEmpty(kv.Value.RPName) && string.Equals(kv.Value.RPName, trimmed, StringComparison.OrdinalIgnoreCase))
                    return !string.IsNullOrEmpty(kv.Value.BoundName) ? kv.Value.BoundName : kv.Key;
            }
            return trimmed;
        }

        private PetMessagePools GetPoolsForPetLocked(string rawPetName, bool seedDefaultsIfEmpty = true)
        {
            string key = GetCanonicalPetKey(rawPetName);
            if (string.IsNullOrEmpty(key)) key = "Pachimari";

            PetMessagePools pools;
            if (!petPoolsMap.TryGetValue(key, out pools))
            {
                CompanionProfile cp;
                if (profiles.TryGetValue(key, out cp) && !string.IsNullOrEmpty(cp.RPName) && petPoolsMap.TryGetValue(cp.RPName, out pools))
                {
                    petPoolsMap[key] = pools;
                }
                else
                {
                    pools = new PetMessagePools();
                    petPoolsMap[key] = pools;
                }
            }
            if (seedDefaultsIfEmpty)
            {
                if (pools.DefaultEmotes.Count == 0) pools.DefaultEmotes.AddRange(DEFAULT_PET_EMOTES);
                if (pools.DefaultSays.Count == 0) pools.DefaultSays.AddRange(DEFAULT_PET_SAYS);
            }
            return pools;
        }

        private string GetActiveEditingPetKey()
        {
            if (cmbPetSelect == null) return "";
            string txt = (cmbPetSelect.Text ?? "").Trim();
            if (string.IsNullOrEmpty(txt) || string.Equals(txt, NO_PET_LABEL, StringComparison.OrdinalIgnoreCase))
                return "";
            return txt;
        }

        public MainForm()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Directory.CreateDirectory(ICON_CACHE_DIR);
            InitOpenDyslexicFont();
            LoadLocalBridgeSettings();
            wowDir = AutoDetectWoWDirectory();

            Text = "Total RP 3 — PetRP Companion LLM Bridge";
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = TRP3_BG_DARK;
            ForeColor = TRP3_IVORY;
            Font = MakeFont(configuredFontSize, FontStyle.Regular);
            DoubleBuffered = true;

            trpTooltip = new ToolTip { AutoPopDelay = 15000, InitialDelay = 250, ReshowDelay = 150, BackColor = TRP3_INSET_BG, ForeColor = TRP3_GOLD };

            TryLoadExeIcon();
            BuildUI();
            ApplyFontAndProportionalScale(configuredFontSize, true);
            LoadLibRPMediaIcons();
            LoadExistingResponsePools();
            LoadTRP3Data();
            WriteLuaState();
            UpdateSignalSoundFile();
            StartBackgroundSmuggleListener();

            Resize += (s, e) => PerformProportionalChildLayout();
            Shown += (s, e) =>
            {
                PerformProportionalChildLayout();
                UpdatePetCard(cmbPetSelect.Text);
                ResolveAccountAndModelsAsync();
                CheckAndPromptWoWAddonInstall();
            };
        }

        // =====================================================================
        // 1. FONT, HTTP & PROCESS UTILITIES
        // =====================================================================

        private static Font MakeFont(float sizePt, FontStyle style)
        {
            float safeSize = Math.Max(6.0f, sizePt);
            try
            {
                if (openDyslexicFamily != null)
                {
                    FontStyle chosen = openDyslexicFamily.IsStyleAvailable(style) ? style : FontStyle.Regular;
                    return new Font(openDyslexicFamily, safeSize, chosen);
                }
                return new Font(resolvedFontName, safeSize, style);
            }
            catch
            {
                return new Font(FontFamily.GenericSansSerif, safeSize, style);
            }
        }

        private int ScalePx(int basePxAt10Pt)
        {
            return (int)Math.Round(basePxAt10Pt * (configuredFontSize / 10.0f));
        }

        private static bool TryDownloadFile(string url, string destPath)
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", HTTP_USER_AGENT);
                    wc.DownloadFile(url, destPath);
                }
                return File.Exists(destPath) && new FileInfo(destPath).Length > 100;
            }
            catch
            {
                return false;
            }
        }

        private static string TryDownloadString(string url)
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", HTTP_USER_AGENT);
                    return wc.DownloadString(url);
                }
            }
            catch
            {
                return "";
            }
        }

        private string RunCliCommand(string arguments, int timeoutMs, out string stderr)
        {
            stderr = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c " + resolvedAgyExecutable + " " + arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (Process p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd().Trim();
                    stderr = p.StandardError.ReadToEnd().Trim();
                    p.WaitForExit(timeoutMs);
                    return stdout;
                }
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
                return "";
            }
        }

        private static void InitOpenDyslexicFont()
        {
            try
            {
                string localFontDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts");
                string addonFontDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PetRP", "fonts");
                Directory.CreateDirectory(localFontDir);
                Directory.CreateDirectory(addonFontDir);

                string[] fontFiles = { "OpenDyslexic-Regular.ttf", "OpenDyslexic-Bold.ttf", "OpenDyslexic-Italic.ttf", "OpenDyslexic-BoldItalic.ttf" };
                foreach (string fn in fontFiles)
                {
                    EnsureFontFileLoaded(fn, localFontDir, addonFontDir);
                }

                if (privateFonts.Families.Length > 0)
                {
                    openDyslexicFamily = privateFonts.Families[0];
                    resolvedFontName = openDyslexicFamily.Name;
                }
            }
            catch { }
        }

        private static void EnsureFontFileLoaded(string fileName, string localFontDir, string addonFontDir)
        {
            string targetPath = Path.Combine(localFontDir, fileName);
            if (!File.Exists(targetPath))
            {
                if (!TryDownloadFile("https://cdn.jsdelivr.net/gh/antijingoist/open-dyslexic@master/ttf/" + fileName, targetPath))
                    TryDownloadFile("https://raw.githubusercontent.com/antijingoist/open-dyslexic/master/ttf/" + fileName, targetPath);
            }
            if (!File.Exists(targetPath)) return;

            AddFontResourceEx(targetPath, FR_PRIVATE, IntPtr.Zero);
            privateFonts.AddFontFile(targetPath);

            string addonCopy = Path.Combine(addonFontDir, fileName);
            if (!File.Exists(addonCopy))
            {
                try { File.Copy(targetPath, addonCopy, true); } catch { }
            }
        }

        // =====================================================================
        // 2. SETTINGS & AUTOMATIC WOW DIRECTORY DETECTION
        // =====================================================================

        private void LoadLocalBridgeSettings()
        {
            if (!File.Exists(SETTINGS_PATH)) return;
            try
            {
                foreach (string line in File.ReadAllLines(SETTINGS_PATH, Encoding.UTF8))
                {
                    ParseSettingLine(line);
                }
            }
            catch { }
        }

        private void ParseSettingLine(string line)
        {
            if (line.StartsWith("CustomWoWDir=", StringComparison.OrdinalIgnoreCase))
            {
                string dir = line.Substring("CustomWoWDir=".Length).Trim();
                if (Directory.Exists(dir)) customWowDirOverride = dir;
            }
            else if (line.StartsWith("ProQuotaLimit=", StringComparison.OrdinalIgnoreCase))
            {
                int q;
                if (int.TryParse(line.Substring("ProQuotaLimit=".Length).Trim(), out q) && q > 0)
                    configuredProQuota = q;
            }
            else if (line.StartsWith("HideAccountBanner=", StringComparison.OrdinalIgnoreCase))
            {
                bool b;
                if (bool.TryParse(line.Substring("HideAccountBanner=".Length).Trim(), out b))
                    hideAccountBanner = b;
            }
            else if (line.StartsWith("FontSize=", StringComparison.OrdinalIgnoreCase))
            {
                float fs;
                if (float.TryParse(line.Substring("FontSize=".Length).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out fs))
                    configuredFontSize = Math.Max(7.0f, Math.Min(24.0f, fs));
            }
        }

        private void SaveLocalBridgeSettings()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture, "ProQuotaLimit={0}\r\nHideAccountBanner={1}\r\nFontSize={2}\r\n",
                    configuredProQuota, hideAccountBanner, configuredFontSize);
                if (!string.IsNullOrEmpty(customWowDirOverride))
                    sb.AppendFormat("CustomWoWDir={0}\r\n", customWowDirOverride);
                File.WriteAllText(SETTINGS_PATH, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private static string ResolveBestWoWFlavorFolder(string candidatePath)
        {
            if (string.IsNullOrEmpty(candidatePath) || !Directory.Exists(candidatePath))
                return "";

            if (Directory.Exists(Path.Combine(candidatePath, "Interface")) ||
                Directory.Exists(Path.Combine(candidatePath, "WTF")) ||
                Directory.Exists(Path.Combine(candidatePath, "Logs")) ||
                File.Exists(Path.Combine(candidatePath, "Wow.exe")))
            {
                return candidatePath;
            }

            string[] flavors = { "_forever_", "_retail_", "_classic_", "_classic_era_", "_ptr_", "_xptr_" };
            foreach (string fl in flavors)
            {
                string sub = Path.Combine(candidatePath, fl);
                if (Directory.Exists(Path.Combine(sub, "WTF")) || Directory.Exists(Path.Combine(sub, "Interface")))
                    return sub;
            }
            foreach (string fl in flavors)
            {
                string sub = Path.Combine(candidatePath, fl);
                if (Directory.Exists(sub)) return sub;
            }
            return "";
        }

        private static string FindRunningWoWDirectory()
        {
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string pn = p.ProcessName;
                        if (string.IsNullOrEmpty(pn) || pn.IndexOf("wow", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        if (p.MainModule != null && !string.IsNullOrEmpty(p.MainModule.FileName))
                        {
                            string dir = Path.GetDirectoryName(p.MainModule.FileName);
                            string resolved = ResolveBestWoWFlavorFolder(dir);
                            return string.IsNullOrEmpty(resolved) ? dir : resolved;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return "";
        }

        private string AutoDetectWoWDirectory()
        {
            if (!string.IsNullOrEmpty(customWowDirOverride) && Directory.Exists(customWowDirOverride))
            {
                string resolved = ResolveBestWoWFlavorFolder(customWowDirOverride);
                return string.IsNullOrEmpty(resolved) ? customWowDirOverride : resolved;
            }

            string runningDir = FindRunningWoWDirectory();
            if (!string.IsNullOrEmpty(runningDir))
                return runningDir;

            List<string> candidates = new List<string>();
            AddRelativeCandidates(candidates);
            AddRunningProcessCandidates(candidates);
            AddBattleNetConfigCandidates(candidates);
            AddRegistryCandidates(candidates);
            AddDriveScanCandidates(candidates);

            foreach (string cand in candidates)
            {
                string resolved = ResolveBestWoWFlavorFolder(cand);
                if (!string.IsNullOrEmpty(resolved) && HasTotalRP3Data(resolved))
                    return resolved;
            }
            foreach (string cand in candidates)
            {
                string resolved = ResolveBestWoWFlavorFolder(cand);
                if (!string.IsNullOrEmpty(resolved))
                    return resolved;
            }
            return "";
        }

        private static bool HasTotalRP3Data(string wowClientDir)
        {
            try
            {
                string wtfAcct = Path.Combine(wowClientDir, "WTF", "Account");
                return Directory.Exists(wtfAcct) && Directory.GetFiles(wtfAcct, "totalRP3.lua", SearchOption.AllDirectories).Length > 0;
            }
            catch { return false; }
        }

        private static void AddRelativeCandidates(List<string> candidates)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                for (int depth = 0; depth < 4 && dir != null; depth++, dir = dir.Parent)
                    candidates.Add(dir.FullName);
            }
            catch { }
        }

        private static void AddRunningProcessCandidates(List<string> candidates)
        {
            string[] procNames = { "Wow", "WowClassic", "WowB", "WowT" };
            foreach (string pn in procNames)
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(pn))
                    {
                        try
                        {
                            if (p.MainModule != null && !string.IsNullOrEmpty(p.MainModule.FileName))
                                candidates.Add(Path.GetDirectoryName(p.MainModule.FileName));
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private static void AddBattleNetConfigCandidates(List<string> candidates)
        {
            try
            {
                string bnetCfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Battle.net", "Battle.net.config");
                if (!File.Exists(bnetCfg)) return;
                string json = File.ReadAllText(bnetCfg, Encoding.UTF8);
                foreach (Match m in Regex.Matches(json, @"\""(?:DefaultInstallPath|InstallPath)\""\s*:\s*\""([^\""]+)\""", RegexOptions.IgnoreCase))
                {
                    string rawPath = m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\/", "\\");
                    if (!string.IsNullOrEmpty(rawPath))
                    {
                        candidates.Add(Path.Combine(rawPath, "World of Warcraft"));
                        candidates.Add(rawPath);
                    }
                }
            }
            catch { }
        }

        private static void AddRegistryCandidates(List<string> candidates)
        {
            string[] regPaths =
            {
                @"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft",
                @"SOFTWARE\Blizzard Entertainment\World of Warcraft",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\World of Warcraft",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\World of Warcraft"
            };
            foreach (string rp in regPaths)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(rp))
                    {
                        if (key == null) continue;
                        object inst = key.GetValue("InstallPath") ?? key.GetValue("InstallLocation");
                        if (inst == null) continue;
                        string val = inst.ToString().Trim('"', ' ');
                        if (string.IsNullOrEmpty(val)) continue;
                        candidates.Add(val);
                        DirectoryInfo parent = Directory.GetParent(val.TrimEnd('\\', '/'));
                        if (parent != null) candidates.Add(parent.FullName);
                    }
                }
                catch { }
            }
        }

        private static void AddDriveScanCandidates(List<string> candidates)
        {
            string[] relRoots =
            {
                @"Battle.net\Games\World of Warcraft",
                @"Games\World of Warcraft",
                @"World of Warcraft",
                @"Blizzard\World of Warcraft",
                @"Program Files (x86)\World of Warcraft",
                @"Program Files\World of Warcraft",
                @"Program Files (x86)\Battle.net\Games\World of Warcraft",
                @"Games\Battle.net\World of Warcraft"
            };
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady || (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable))
                        continue;
                    foreach (string rel in relRoots)
                        candidates.Add(Path.Combine(drive.RootDirectory.FullName, rel));
                }
            }
            catch { }
        }

        private void TryLoadExeIcon()
        {
            try
            {
                string localIco = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(localIco))
                {
                    Icon = new Icon(localIco);
                    return;
                }
                Icon selfIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (selfIcon != null) Icon = selfIcon;
            }
            catch { }
        }

        // =====================================================================
        // 3. UI CONSTRUCTION & PROPORTIONAL SCALING
        // =====================================================================

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int w = ClientSize.Width - 1;
            int h = ClientSize.Height - 1;
            if (w <= 6 || h <= 6) return;
            using (Pen outer = new Pen(TRP3_BRONZE, 2f))
            using (Pen inner = new Pen(Color.FromArgb(75, 55, 25), 1f))
            {
                e.Graphics.DrawRectangle(outer, 1, 1, w - 2, h - 2);
                e.Graphics.DrawRectangle(inner, 4, 4, w - 8, h - 8);
            }
        }

        private static Label MakeLabel(string text, Color fg)
        {
            return new Label
            {
                ForeColor = fg,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = text
            };
        }

        private static TextBox MakeReadOnlyTextBox(string initialText = "")
        {
            return new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = TRP3_INSET_BG,
                ForeColor = TRP3_IVORY,
                Text = initialText
            };
        }

        private Button CreateTRP3Button(string text, Color baseBg)
        {
            Button b = new Button
            {
                Text = text,
                BackColor = baseBg,
                ForeColor = TRP3_GOLD,
                FlatStyle = FlatStyle.Flat,
                Font = MakeFont(configuredFontSize, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = TRP3_BRONZE;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(
                Math.Min(255, baseBg.R + 28),
                Math.Min(255, baseBg.G + 20),
                Math.Min(255, baseBg.B + 15));
            return b;
        }

        private void BuildUI()
        {
            pnlHeaderPlaque = new TRP3BorderPanel { BackColor = TRP3_HEADER_BG };
            lblHeaderTitle = MakeLabel("⚔ Total RP 3 — Companion LLM Bridge", TRP3_GOLD);
            btnOptions = CreateTRP3Button("⚙ Options", Color.FromArgb(62, 48, 28));
            btnOptions.Click += (s, e) => ShowOptionsDialog();

            btnToggleAccount = CreateTRP3Button("👁 Hide Account", Color.FromArgb(68, 50, 28));
            btnToggleAccount.Click += (s, e) =>
            {
                hideAccountBanner = !hideAccountBanner;
                SaveLocalBridgeSettings();
                PerformProportionalChildLayout();
            };

            btnUsage = CreateTRP3Button("📊 Usage / Quota", TRP3_CRIMSON);
            btnUsage.Click += (s, e) => ShowUsageDialog();
            pnlHeaderPlaque.Controls.AddRange(new Control[] { lblHeaderTitle, btnOptions, btnToggleAccount, btnUsage });
            Controls.Add(pnlHeaderPlaque);

            pnlAccountBanner = new TRP3BorderPanel { BackColor = Color.FromArgb(26, 20, 14), DrawCornerRivets = false };
            lblAccountBanner = MakeLabel("Google Account (AGY): Detecting signed-in Google account & Pro/Paid quota...", TRP3_GOLD_SOFT);
            pnlAccountBanner.Controls.Add(lblAccountBanner);
            Controls.Add(pnlAccountBanner);

            pnlTabBar = new Panel { BackColor = Color.Transparent };
            btnTabProfile = CreateTRP3Button("📜 Companion Profile & Live Log", Color.FromArgb(88, 52, 22));
            btnTabProfile.Click += (s, e) => SwitchActiveTab(0);
            btnTabMessages = CreateTRP3Button("💬 Stored Messages (Add / Edit / Delete)", Color.FromArgb(42, 30, 20));
            btnTabMessages.Click += (s, e) => SwitchActiveTab(1);
            pnlTabBar.Controls.AddRange(new Control[] { btnTabProfile, btnTabMessages });
            Controls.Add(pnlTabBar);

            pnlMainBody = new Panel { BackColor = Color.Transparent };
            Controls.Add(pnlMainBody);

            BuildCompanionSheetUI();
            BuildControlBarUI();
            BuildMessagesTabUI();

            lblStats = MakeLabel("Status: Idle  |  Sync Triggers: 0 (Last: None yet)  |  Msgs: 0  |  Total Tok: ~0", TRP3_GREEN);
            logFrame = new TRP3BorderPanel { BackColor = TRP3_INSET_BG };
            txtHistory = MakeReadOnlyTextBox();
            logFrame.Controls.Add(txtHistory);
            pnlMainBody.Controls.AddRange(new Control[] { lblStats, logFrame });
        }

        private void BuildMessagesTabUI()
        {
            pnlMessagesTab = new TRP3BorderPanel { BackColor = TRP3_PANEL_BG, Visible = false };
            lblDefaultsStatus = MakeLabel("Defaults Status: ✔ 60 Defaults Generated", TRP3_GREEN);
            btnRegenDefaults = CreateTRP3Button("🔄 Regenerate 60 Defaults (Replaces All)", TRP3_CRIMSON);
            btnRegenDefaults.Click += async (s, e) => await ConfirmAndRegenerate60DefaultsAsync();

            lblFilterCat = MakeLabel("Filter Pool:", TRP3_GOLD);
            cmbMsgFilter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = TRP3_INSET_BG,
                ForeColor = TRP3_IVORY,
                FlatStyle = FlatStyle.Flat
            };
            cmbMsgFilter.Items.AddRange(new object[]
            {
                "All Stored Messages",
                "Default Emotes (DEFEMOTE)",
                "Default Says (DEFSAY)",
                "Periodic AI Emotes (EMOTE)",
                "Periodic AI Says (SAY)",
                "Reactive AI Emotes (REACT)"
            });
            cmbMsgFilter.SelectedIndex = 0;
            cmbMsgFilter.SelectedIndexChanged += (s, e) => RefreshStoredMessagesListUI();

            lstStoredMessages = new ListBox
            {
                BackColor = TRP3_INSET_BG,
                ForeColor = TRP3_IVORY,
                BorderStyle = BorderStyle.FixedSingle,
                HorizontalScrollbar = true
            };
            lstStoredMessages.SelectedIndexChanged += (s, e) => OnStoredMessageSelected();

            lblEditCat = MakeLabel("Type:", TRP3_GOLD);
            cmbEditKind = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = TRP3_INSET_BG,
                ForeColor = TRP3_IVORY,
                FlatStyle = FlatStyle.Flat
            };
            cmbEditKind.Items.AddRange(new object[]
            {
                "EMOTE (Periodic Emote)",
                "SAY (Periodic Say)",
                "REACT (Reactive Emote)",
                "DEFEMOTE (Default Emote)",
                "DEFSAY (Default Say)"
            });
            cmbEditKind.SelectedIndex = 0;

            lblEditPrompt = MakeLabel("Message Text (Add / Edit / Delete triggers in-game Sync highlight):", TRP3_GOLD_SOFT);
            txtEditMessage = new TextBox
            {
                BackColor = TRP3_INSET_BG,
                ForeColor = TRP3_IVORY,
                BorderStyle = BorderStyle.FixedSingle
            };

            btnAddMsg = CreateTRP3Button("➕ Add Message", Color.FromArgb(28, 92, 42));
            btnAddMsg.Click += (s, e) => AddUserMessageFromEditor();

            btnSaveEditMsg = CreateTRP3Button("💾 Save Edit to Selected", Color.FromArgb(110, 68, 22));
            btnSaveEditMsg.Click += (s, e) => EditSelectedUserMessage();

            btnDeleteMsg = CreateTRP3Button("🗑 Delete Selected", TRP3_CRIMSON);
            btnDeleteMsg.Click += (s, e) => DeleteSelectedUserMessage();

            pnlMessagesTab.Controls.AddRange(new Control[]
            {
                lblDefaultsStatus, btnRegenDefaults, lblFilterCat, cmbMsgFilter,
                lstStoredMessages, lblEditCat, cmbEditKind, lblEditPrompt, txtEditMessage,
                btnAddMsg, btnSaveEditMsg, btnDeleteMsg
            });
            pnlMainBody.Controls.Add(pnlMessagesTab);
        }

        private void SwitchActiveTab(int index)
        {
            activeTabIndex = index;
            bool isMain = (index == 0);
            sheetPanel.Visible = isMain;
            ctrlBar.Visible = isMain;
            lblStats.Visible = isMain;
            logFrame.Visible = isMain;
            pnlMessagesTab.Visible = !isMain;

            btnTabProfile.BackColor = isMain ? Color.FromArgb(88, 52, 22) : Color.FromArgb(42, 30, 20);
            btnTabMessages.BackColor = !isMain ? Color.FromArgb(88, 52, 22) : Color.FromArgb(42, 30, 20);
            if (!isMain) RefreshStoredMessagesListUI();
            PerformProportionalChildLayout();
        }

        private void BuildCompanionSheetUI()
        {
            sheetPanel = new TRP3BorderPanel { BackColor = TRP3_PANEL_BG };
            picPetIcon = new PictureBox { SizeMode = PictureBoxSizeMode.StretchImage, BackColor = TRP3_INSET_BG };
            lblPetHeadline = MakeLabel("No Companion Selected", TRP3_GOLD);
            lblPetSubtitle = MakeLabel("Bind a companion in Total RP 3 or select its name below", TRP3_GOLD_SOFT);
            lblGlanceSection = MakeLabel("✦ At First Glance (5 Companion Trait Slots)", TRP3_GOLD);
            lblAboutSection = MakeLabel("✦ About / Backstory", TRP3_GOLD);

            sheetPanel.Controls.AddRange(new Control[] { picPetIcon, lblPetHeadline, lblPetSubtitle, lblGlanceSection, lblAboutSection });

            for (int i = 0; i < 5; i++)
            {
                glanceBoxes[i] = new TRP3BorderPanel { BackColor = TRP3_SLOT_BG, OuterGold = Color.FromArgb(135, 106, 50), DrawCornerRivets = false };
                glanceIcons[i] = new PictureBox { SizeMode = PictureBoxSizeMode.StretchImage, BackColor = TRP3_INSET_BG };
                glanceTitles[i] = MakeLabel("Slot " + (i + 1), TRP3_GOLD);
                glanceTexts[i] = MakeReadOnlyTextBox("Empty");
                glanceBoxes[i].Controls.AddRange(new Control[] { glanceIcons[i], glanceTitles[i], glanceTexts[i] });
                sheetPanel.Controls.Add(glanceBoxes[i]);
            }

            aboutBox = new TRP3BorderPanel { BackColor = TRP3_SLOT_BG, OuterGold = Color.FromArgb(145, 114, 54), DrawCornerRivets = false };
            picAboutExtractedImg = new PictureBox { SizeMode = PictureBoxSizeMode.StretchImage, BackColor = TRP3_INSET_BG, Visible = false };
            txtAboutBody = MakeReadOnlyTextBox();
            aboutBox.Controls.AddRange(new Control[] { picAboutExtractedImg, txtAboutBody });
            sheetPanel.Controls.Add(aboutBox);
            pnlMainBody.Controls.Add(sheetPanel);
        }

        private void BuildControlBarUI()
        {
            ctrlBar = new TRP3BorderPanel { BackColor = TRP3_PANEL_BG, DrawCornerRivets = false };
            lblPet = MakeLabel("Pet:", TRP3_GOLD);
            cmbPetSelect = new ComboBox { BackColor = TRP3_INSET_BG, ForeColor = TRP3_IVORY, FlatStyle = FlatStyle.Flat };
            cmbPetSelect.SelectedIndexChanged += (s, e) => { UpdatePetCard(cmbPetSelect.Text); RefreshStoredMessagesListUI(); };
            cmbPetSelect.TextChanged += (s, e) => { UpdatePetCard(cmbPetSelect.Text); RefreshStoredMessagesListUI(); };

            lblMod = MakeLabel("Model:", TRP3_GOLD);
            cmbModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, BackColor = TRP3_INSET_BG, ForeColor = TRP3_IVORY, FlatStyle = FlatStyle.Flat };
            PopulateModelPricingList(null);

            lblInt = MakeLabel("Timer (Seconds):", TRP3_GOLD);
            numInterval = new NumericUpDown { Minimum = 15, Maximum = 3600, Value = 180, BackColor = TRP3_INSET_BG, ForeColor = TRP3_IVORY };
            lblIntUnit = MakeLabel("sec (3m 0s)", TRP3_GOLD_SOFT);
            numInterval.ValueChanged += (s, e) =>
            {
                int sec = (int)numInterval.Value;
                lblIntUnit.Text = string.Format("sec ({0}m {1}s)", sec / 60, sec % 60);
                string activePet = GetActiveEditingPetKey();
                if (!string.IsNullOrEmpty(activePet))
                {
                    lock (poolLock)
                    {
                        GetPoolsForPetLocked(activePet, false).IntervalSeconds = sec;
                    }
                }
            };
            string timerTip = "Periodic generation interval in Seconds (e.g., 120 = 2 minutes, 180 = 3 minutes).";
            trpTooltip.SetToolTip(lblInt, timerTip);
            trpTooltip.SetToolTip(numInterval, timerTip);
            trpTooltip.SetToolTip(lblIntUnit, timerTip);

            btnStartStop = CreateTRP3Button("▶ Start Watching", Color.FromArgb(28, 92, 42));
            btnStartStop.Click += (s, e) => ToggleWatching();

            btnGenNow = CreateTRP3Button("⚡ Generate To Game", TRP3_CRIMSON);
            btnGenNow.Click += async (s, e) => await TriggerManualGenerationAsync();

            btnTestLLM = CreateTRP3Button("🧪 Test LLM", Color.FromArgb(110, 58, 18));
            btnTestLLM.Click += async (s, e) => await TriggerTestGenerationAsync();

            btnReloadTRP3 = CreateTRP3Button("↻ Sync TRP3", Color.FromArgb(72, 42, 22));
            btnReloadTRP3.Click += (s, e) => { LoadTRP3Data(); ResolveAccountAndModelsAsync(); };

            btnOpenGenLog = CreateTRP3Button("📜 watcher-gen.txt", Color.FromArgb(62, 46, 28));
            btnOpenGenLog.Click += (s, e) => OpenGenLogFile();

            ctrlBar.Controls.AddRange(new Control[]
            {
                lblPet, cmbPetSelect, lblMod, cmbModel, lblInt, numInterval, lblIntUnit,
                btnStartStop, btnGenNow, btnTestLLM, btnReloadTRP3, btnOpenGenLog
            });
            pnlMainBody.Controls.Add(ctrlBar);
        }

        private void ApplyFontAndProportionalScale(float newFontSize, bool resizeWindow)
        {
            configuredFontSize = Math.Max(7.0f, Math.Min(24.0f, newFontSize));
            Font reg = MakeFont(configuredFontSize, FontStyle.Regular);
            Font bold = MakeFont(configuredFontSize, FontStyle.Bold);
            Font italic = MakeFont(configuredFontSize, FontStyle.Italic);

            Font = reg;
            lblHeaderTitle.Font = MakeFont(configuredFontSize + 1.0f, FontStyle.Bold);
            lblPetHeadline.Font = MakeFont(configuredFontSize + 2.5f, FontStyle.Bold);
            lblPetSubtitle.Font = italic;

            Control[] boldControls =
            {
                btnOptions, btnToggleAccount, btnUsage, lblAccountBanner,
                btnTabProfile, btnTabMessages, lblDefaultsStatus, btnRegenDefaults,
                lblFilterCat, lblEditCat, lblEditPrompt, btnAddMsg, btnSaveEditMsg, btnDeleteMsg,
                lblGlanceSection, lblAboutSection, lblPet, lblMod, lblInt,
                btnStartStop, btnGenNow, btnTestLLM, btnReloadTRP3, btnOpenGenLog, lblStats
            };
            foreach (Control c in boldControls) c.Font = bold;

            Control[] regControls =
            {
                txtAboutBody, cmbPetSelect, cmbModel, numInterval, lblIntUnit, txtHistory,
                cmbMsgFilter, lstStoredMessages, cmbEditKind, txtEditMessage
            };
            foreach (Control c in regControls) c.Font = reg;

            for (int i = 0; i < 5; i++)
            {
                glanceTitles[i].Font = bold;
                glanceTexts[i].Font = reg;
            }

            if (resizeWindow)
            {
                MinimumSize = new Size(Math.Max(720, ScalePx(820)), Math.Max(520, ScalePx(585)));
                Size = new Size(Math.Max(780, ScalePx(920)), Math.Max(560, ScalePx(655)));
            }

            PerformProportionalChildLayout();
            UpdatePetCard(cmbPetSelect.Text);
        }

        private void PerformProportionalChildLayout()
        {
            if (pnlHeaderPlaque == null || pnlMainBody == null) return;
            int pad = ScalePx(8);
            int bodyW = Math.Max(400, ClientSize.Width - (pad * 2));

            int curY = LayoutHeaderAndBanner(pad, bodyW);
            int mainH = Math.Max(300, ClientSize.Height - curY - pad);
            pnlMainBody.Bounds = new Rectangle(pad, curY, bodyW, mainH);

            int sheetH = LayoutCompanionSheet(bodyW);
            int ctrlY = sheetH + ScalePx(5);
            int ctrlH = LayoutControlBar(ctrlY, bodyW);

            int statsY = ctrlY + ctrlH + ScalePx(3);
            int statsH = ScalePx(20);
            lblStats.Bounds = new Rectangle(ScalePx(2), statsY, bodyW - ScalePx(4), statsH);

            int logY = statsY + statsH + ScalePx(2);
            logFrame.Bounds = new Rectangle(0, logY, bodyW, Math.Max(ScalePx(52), mainH - logY));
            txtHistory.Bounds = new Rectangle(ScalePx(4), ScalePx(4), Math.Max(50, logFrame.Width - ScalePx(8)), Math.Max(20, logFrame.Height - ScalePx(8)));

            LayoutMessagesTab(bodyW, mainH);
        }

        private void LayoutMessagesTab(int bodyW, int mainH)
        {
            if (pnlMessagesTab == null) return;
            pnlMessagesTab.Bounds = new Rectangle(0, 0, bodyW, mainH);
            int pad = ScalePx(10);
            int rowH = ScalePx(28);
            int regenW = ScalePx(300);

            lblDefaultsStatus.Bounds = new Rectangle(pad, pad, Math.Max(150, bodyW - regenW - (pad * 3)), rowH);
            btnRegenDefaults.Bounds = new Rectangle(bodyW - regenW - pad, pad, regenW, rowH + ScalePx(2));

            int filterY = pad + rowH + ScalePx(8);
            lblFilterCat.Bounds = new Rectangle(pad, filterY, ScalePx(110), rowH);
            cmbMsgFilter.Bounds = new Rectangle(pad + ScalePx(114), filterY, ScalePx(290), rowH);

            int bottomBlockH = ScalePx(106);
            int listY = filterY + rowH + ScalePx(6);
            int listH = Math.Max(ScalePx(100), mainH - listY - bottomBlockH - pad);
            lstStoredMessages.Bounds = new Rectangle(pad, listY, bodyW - (pad * 2), listH);

            int editRow1Y = listY + listH + ScalePx(8);
            lblEditCat.Bounds = new Rectangle(pad, editRow1Y, ScalePx(62), rowH);
            cmbEditKind.Bounds = new Rectangle(pad + ScalePx(64), editRow1Y, ScalePx(235), rowH);
            int promptLeft = pad + ScalePx(310);
            lblEditPrompt.Bounds = new Rectangle(promptLeft, editRow1Y, Math.Max(100, bodyW - promptLeft - pad), rowH);

            int editRow2Y = editRow1Y + rowH + ScalePx(4);
            txtEditMessage.Bounds = new Rectangle(pad, editRow2Y, bodyW - (pad * 2), rowH);

            int btnY = editRow2Y + rowH + ScalePx(6);
            int btnSpacing = ScalePx(8);
            int btnW = (bodyW - (pad * 2) - (btnSpacing * 2)) / 3;
            btnAddMsg.Bounds = new Rectangle(pad, btnY, btnW, ScalePx(30));
            btnSaveEditMsg.Bounds = new Rectangle(pad + btnW + btnSpacing, btnY, btnW, ScalePx(30));
            btnDeleteMsg.Bounds = new Rectangle(pad + (btnW + btnSpacing) * 2, btnY, btnW, ScalePx(30));
        }

        private int LayoutHeaderAndBanner(int pad, int bodyW)
        {
            int headerH = ScalePx(34);
            pnlHeaderPlaque.Bounds = new Rectangle(pad, pad, bodyW, headerH);

            int hdrBtnH = ScalePx(26);
            int hdrBtnY = (headerH - hdrBtnH) / 2;
            btnToggleAccount.Text = hideAccountBanner ? "👁 Show Account" : "👁 Hide Account";
            btnUsage.Visible = !hideAccountBanner;

            int rightCursor = bodyW - ScalePx(6);
            if (!hideAccountBanner)
            {
                rightCursor -= ScalePx(148);
                btnUsage.Bounds = new Rectangle(rightCursor, hdrBtnY, ScalePx(148), hdrBtnH);
                rightCursor -= ScalePx(6);
            }
            rightCursor -= ScalePx(148);
            btnToggleAccount.Bounds = new Rectangle(rightCursor, hdrBtnY, ScalePx(148), hdrBtnH);
            rightCursor -= ScalePx(118);
            btnOptions.Bounds = new Rectangle(rightCursor, hdrBtnY, ScalePx(112), hdrBtnH);
            lblHeaderTitle.Bounds = new Rectangle(ScalePx(10), ScalePx(4), Math.Max(150, rightCursor - ScalePx(16)), headerH - ScalePx(8));

            int curY = pad + headerH + ScalePx(4);
            pnlAccountBanner.Visible = !hideAccountBanner;
            if (!hideAccountBanner)
            {
                int bannerH = ScalePx(26);
                pnlAccountBanner.Bounds = new Rectangle(pad, curY, bodyW, bannerH);
                lblAccountBanner.Bounds = new Rectangle(ScalePx(8), ScalePx(3), bodyW - ScalePx(16), bannerH - ScalePx(6));
                curY += bannerH + ScalePx(4);
            }

            int tabH = ScalePx(28);
            pnlTabBar.Bounds = new Rectangle(pad, curY, bodyW, tabH);
            int halfTabW = (bodyW - ScalePx(6)) / 2;
            btnTabProfile.Bounds = new Rectangle(0, 0, halfTabW, tabH);
            btnTabMessages.Bounds = new Rectangle(halfTabW + ScalePx(6), 0, bodyW - halfTabW - ScalePx(6), tabH);
            curY += tabH + ScalePx(4);
            return curY;
        }

        private int LayoutCompanionSheet(int bodyW)
        {
            int iconSz = ScalePx(54);
            int topRowH = iconSz + ScalePx(14);
            int secLabelH = ScalePx(20);
            int glanceBoxH = ScalePx(126);
            int aboutBoxH = ScalePx(92);
            int sheetH = topRowH + secLabelH + glanceBoxH + ScalePx(6) + secLabelH + aboutBoxH + ScalePx(14);

            sheetPanel.Bounds = new Rectangle(0, 0, bodyW, sheetH);
            picPetIcon.Bounds = new Rectangle(ScalePx(10), ScalePx(8), iconSz, iconSz);
            int textLeft = ScalePx(18) + iconSz;
            lblPetHeadline.Bounds = new Rectangle(textLeft, ScalePx(8), bodyW - textLeft - ScalePx(10), ScalePx(28));
            lblPetSubtitle.Bounds = new Rectangle(textLeft, ScalePx(36), bodyW - textLeft - ScalePx(10), ScalePx(24));

            lblGlanceSection.Bounds = new Rectangle(ScalePx(10), topRowH, bodyW - ScalePx(20), secLabelH);
            int glanceY = topRowH + secLabelH + ScalePx(2);
            int glanceSpacing = ScalePx(6);
            int availW = bodyW - ScalePx(20);
            int singleGlanceW = Math.Max(100, (availW - (4 * glanceSpacing)) / 5);
            int gIconSz = ScalePx(32);

            for (int i = 0; i < 5; i++)
            {
                int gx = ScalePx(10) + i * (singleGlanceW + glanceSpacing);
                glanceBoxes[i].Bounds = new Rectangle(gx, glanceY, singleGlanceW, glanceBoxH);
                glanceIcons[i].Bounds = new Rectangle(ScalePx(5), ScalePx(5), gIconSz, gIconSz);
                int gtLeft = ScalePx(9) + gIconSz;
                glanceTitles[i].Bounds = new Rectangle(gtLeft, ScalePx(4), Math.Max(40, singleGlanceW - gtLeft - ScalePx(4)), gIconSz + ScalePx(4));
                int gTextTop = gIconSz + ScalePx(10);
                glanceTexts[i].Bounds = new Rectangle(ScalePx(5), gTextTop, singleGlanceW - ScalePx(10), Math.Max(30, glanceBoxH - gTextTop - ScalePx(5)));
            }

            int aboutLabelY = glanceY + glanceBoxH + ScalePx(5);
            lblAboutSection.Bounds = new Rectangle(ScalePx(10), aboutLabelY, availW, secLabelH);
            aboutBox.Bounds = new Rectangle(ScalePx(10), aboutLabelY + secLabelH + ScalePx(2), availW, aboutBoxH);
            LayoutAboutBox();
            return sheetH;
        }

        private int LayoutControlBar(int ctrlY, int bodyW)
        {
            int row1H = ScalePx(28);
            int btnH = ScalePx(30);
            int ctrlH = row1H + btnH + ScalePx(18);
            ctrlBar.Bounds = new Rectangle(0, ctrlY, bodyW, ctrlH);

            int r1Y = ScalePx(6);
            int cx = ScalePx(10);
            lblPet.Bounds = new Rectangle(cx, r1Y, ScalePx(44), row1H); cx += ScalePx(44);
            cmbPetSelect.Bounds = new Rectangle(cx, r1Y, ScalePx(150), row1H); cx += ScalePx(160);
            lblMod.Bounds = new Rectangle(cx, r1Y, ScalePx(64), row1H); cx += ScalePx(64);
            cmbModel.Bounds = new Rectangle(cx, r1Y, ScalePx(245), row1H); cx += ScalePx(255);
            lblInt.Bounds = new Rectangle(cx, r1Y, ScalePx(136), row1H); cx += ScalePx(136);
            numInterval.Bounds = new Rectangle(cx, r1Y, ScalePx(66), row1H); cx += ScalePx(72);
            lblIntUnit.Bounds = new Rectangle(cx, r1Y, Math.Max(ScalePx(90), bodyW - cx - ScalePx(8)), row1H);

            Button[] actionBtns = { btnStartStop, btnGenNow, btnTestLLM, btnReloadTRP3, btnOpenGenLog };
            int btnSpacing = ScalePx(6);
            int availW = bodyW - ScalePx(20);
            int singleBtnW = Math.Max(90, (availW - (4 * btnSpacing)) / 5);
            int btnY = r1Y + row1H + ScalePx(6);
            for (int i = 0; i < actionBtns.Length; i++)
                actionBtns[i].Bounds = new Rectangle(ScalePx(10) + i * (singleBtnW + btnSpacing), btnY, singleBtnW, btnH);

            return ctrlH;
        }

        private void LayoutAboutBox()
        {
            if (aboutBox == null || txtAboutBody == null || picAboutExtractedImg == null) return;
            int pad = ScalePx(6);
            int imgSz = Math.Max(32, aboutBox.Height - (pad * 2));
            if (picAboutExtractedImg.Visible)
            {
                picAboutExtractedImg.Bounds = new Rectangle(pad, pad, imgSz, imgSz);
                int txLeft = pad + imgSz + pad;
                txtAboutBody.Bounds = new Rectangle(txLeft, pad, Math.Max(60, aboutBox.Width - txLeft - pad), aboutBox.Height - (pad * 2));
            }
            else
            {
                txtAboutBody.Bounds = new Rectangle(pad, pad, Math.Max(60, aboutBox.Width - (pad * 2)), aboutBox.Height - (pad * 2));
            }
        }

        // =====================================================================
        // 4. OPTIONS MENU & WOW ADDON / FONT INSTALLERS
        // =====================================================================

        private void ShowOptionsDialog()
        {
            Form dlg = new Form
            {
                Text = "Total RP 3 — Companion Bridge Options",
                Size = new Size(ScalePx(520), ScalePx(460)),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = TRP3_BG_DARK,
                ForeColor = TRP3_IVORY,
                Font = MakeFont(configuredFontSize, FontStyle.Regular)
            };

            TRP3BorderPanel box = new TRP3BorderPanel
            {
                Location = new Point(ScalePx(12), ScalePx(12)),
                Size = new Size(dlg.ClientSize.Width - ScalePx(24), dlg.ClientSize.Height - ScalePx(24)),
                BackColor = TRP3_PANEL_BG
            };
            dlg.Controls.Add(box);

            Label lblFs = MakeLabel("OpenDyslexic Font Size (pt):", TRP3_GOLD);
            lblFs.Bounds = new Rectangle(ScalePx(14), ScalePx(16), ScalePx(240), ScalePx(26));
            lblFs.Font = MakeFont(configuredFontSize, FontStyle.Bold);

            NumericUpDown numFs = new NumericUpDown
            {
                Bounds = new Rectangle(ScalePx(260), ScalePx(14), ScalePx(80), ScalePx(28)),
                Minimum = 7,
                Maximum = 24,
                Value = (decimal)Math.Round(configuredFontSize),
                BackColor = TRP3_INSET_BG,
                ForeColor = TRP3_IVORY,
                Font = MakeFont(configuredFontSize, FontStyle.Bold)
            };
            numFs.ValueChanged += (s, e) => { ApplyFontAndProportionalScale((float)numFs.Value, true); SaveLocalBridgeSettings(); };

            CheckBox chkHideAcct = new CheckBox
            {
                Bounds = new Rectangle(ScalePx(14), ScalePx(50), ScalePx(440), ScalePx(26)),
                ForeColor = TRP3_IVORY,
                Checked = hideAccountBanner,
                Text = "Hide Google Account Banner (Screenshot Mode)"
            };
            chkHideAcct.CheckedChanged += (s, e) =>
            {
                hideAccountBanner = chkHideAcct.Checked;
                SaveLocalBridgeSettings();
                PerformProportionalChildLayout();
            };

            Label lblWowDirHeader = MakeLabel("World of Warcraft Client Folder (Auto-Detected):", TRP3_GOLD);
            lblWowDirHeader.Bounds = new Rectangle(ScalePx(14), ScalePx(84), box.Width - ScalePx(28), ScalePx(22));
            lblWowDirHeader.Font = MakeFont(configuredFontSize, FontStyle.Bold);

            TextBox txtWowDir = new TextBox
            {
                Bounds = new Rectangle(ScalePx(14), ScalePx(108), box.Width - ScalePx(28), ScalePx(26)),
                ReadOnly = true,
                BackColor = TRP3_INSET_BG,
                ForeColor = TRP3_IVORY
            };

            Label lblAddonStatus = MakeLabel("", TRP3_GREEN);
            lblAddonStatus.Bounds = new Rectangle(ScalePx(14), ScalePx(180), box.Width - ScalePx(28), ScalePx(24));
            lblAddonStatus.Font = MakeFont(configuredFontSize, FontStyle.Bold);

            Button btnReinstallAddon = CreateTRP3Button("", Color.FromArgb(88, 52, 22));
            btnReinstallAddon.Bounds = new Rectangle(ScalePx(14), ScalePx(208), box.Width - ScalePx(28), ScalePx(34));

            Action refreshDialogWoWState = () =>
            {
                txtWowDir.Text = string.IsNullOrEmpty(wowDir) ? "(Not Detected — Click Browse to select your WoW folder)" : wowDir;
                bool inst = IsWoWAddonInstalled();
                string bundledVer = GetBundledAddonVersion();
                string installedVer = GetInstalledAddonVersion();
                List<string> outdatedFiles;
                bool outdated = inst && IsWoWAddonOutdated(out installedVer, out bundledVer, out outdatedFiles);

                if (!inst)
                {
                    lblAddonStatus.ForeColor = TRP3_GOLD;
                    lblAddonStatus.Text = "WoW Addon Status: ⚠ PetRP Addon NOT Installed (Bundled: v" + bundledVer + ")";
                    btnReinstallAddon.Text = "📦 Install WoW Addon (PetRP v" + bundledVer + ")";
                }
                else if (outdated)
                {
                    lblAddonStatus.ForeColor = Color.FromArgb(255, 145, 75);
                    lblAddonStatus.Text = string.Format("WoW Addon Status: ⚠ Update Available (Installed: v{0} → Bundled: v{1})", installedVer, bundledVer);
                    btnReinstallAddon.Text = "🔄 Update WoW Addon to v" + bundledVer;
                }
                else
                {
                    lblAddonStatus.ForeColor = TRP3_GREEN;
                    lblAddonStatus.Text = "WoW Addon Status: ✔ Up to Date (v" + installedVer + ")";
                    btnReinstallAddon.Text = "📦 Reinstall WoW Addon (PetRP v" + bundledVer + ")";
                }
            };
            refreshDialogWoWState();

            int halfBtnW = (box.Width - ScalePx(36)) / 2;
            Button btnBrowseWow = CreateTRP3Button("📂 Browse WoW Folder...", Color.FromArgb(62, 48, 28));
            btnBrowseWow.Bounds = new Rectangle(ScalePx(14), ScalePx(140), halfBtnW, ScalePx(32));
            btnBrowseWow.Click += (s, e) => BrowseForWoWFolder(dlg, refreshDialogWoWState);

            Button btnAutoDetectWow = CreateTRP3Button("🔍 Auto-Detect WoW", Color.FromArgb(48, 58, 38));
            btnAutoDetectWow.Bounds = new Rectangle(ScalePx(22) + halfBtnW, ScalePx(140), halfBtnW, ScalePx(32));
            btnAutoDetectWow.Click += (s, e) =>
            {
                customWowDirOverride = "";
                wowDir = AutoDetectWoWDirectory();
                SaveLocalBridgeSettings();
                LoadLibRPMediaIcons();
                LoadTRP3Data();
                refreshDialogWoWState();
            };

            btnReinstallAddon.Click += (s, e) =>
            {
                if (InstallOrReinstallWoWAddon(true)) refreshDialogWoWState();
            };

            Label lblWowInfo = MakeLabel("Install OpenDyslexic into World of Warcraft (Total RP 3 SharedMedia & WoW client Fonts\\ folder):", TRP3_GOLD_SOFT);
            lblWowInfo.Bounds = new Rectangle(ScalePx(14), ScalePx(252), box.Width - ScalePx(28), ScalePx(42));
            lblWowInfo.Font = MakeFont(configuredFontSize, FontStyle.Italic);

            Button btnInstallFont = CreateTRP3Button("🔤 Install OpenDyslexic to WoW", Color.FromArgb(42, 72, 32));
            btnInstallFont.Bounds = new Rectangle(ScalePx(14), ScalePx(298), box.Width - ScalePx(28), ScalePx(34));
            btnInstallFont.Click += (s, e) => InstallOpenDyslexicToWoW();

            Button btnClose = CreateTRP3Button("✔ Done", TRP3_CRIMSON);
            btnClose.Bounds = new Rectangle(box.Width - ScalePx(134), box.Height - ScalePx(44), ScalePx(120), ScalePx(32));
            btnClose.Click += (s, e) => dlg.Close();

            box.Controls.AddRange(new Control[]
            {
                lblFs, numFs, chkHideAcct,
                lblWowDirHeader, txtWowDir, btnBrowseWow, btnAutoDetectWow,
                lblAddonStatus, btnReinstallAddon,
                lblWowInfo, btnInstallFont, btnClose
            });
            dlg.ShowDialog(this);
        }

        private void BrowseForWoWFolder(Form parentDialog, Action onUpdated)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select your World of Warcraft client folder (e.g. _forever_, _retail_, _classic_, or World of Warcraft root):";
                if (!string.IsNullOrEmpty(wowDir) && Directory.Exists(wowDir))
                    fbd.SelectedPath = wowDir;
                if (fbd.ShowDialog(parentDialog) != DialogResult.OK || string.IsNullOrEmpty(fbd.SelectedPath))
                    return;

                string resolved = ResolveBestWoWFlavorFolder(fbd.SelectedPath);
                customWowDirOverride = string.IsNullOrEmpty(resolved) ? fbd.SelectedPath : resolved;
                wowDir = customWowDirOverride;
                SaveLocalBridgeSettings();
                LoadLibRPMediaIcons();
                LoadTRP3Data();
                onUpdated();
            }
        }

        private bool IsWoWAddonInstalled()
        {
            if (string.IsNullOrEmpty(wowDir)) return false;
            string targetAddonDir = Path.Combine(wowDir, "Interface", "AddOns", "PetRP");
            return File.Exists(Path.Combine(targetAddonDir, "PetRP.toc")) &&
                   File.Exists(Path.Combine(targetAddonDir, "core.lua")) &&
                   File.Exists(Path.Combine(targetAddonDir, "ui.lua"));
        }

        private static string ReadTocVersion(string tocPath)
        {
            try
            {
                if (!File.Exists(tocPath)) return "0.0.0";
                foreach (string line in File.ReadAllLines(tocPath, Encoding.UTF8))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("## Version:", StringComparison.OrdinalIgnoreCase))
                        return trimmed.Substring("## Version:".Length).Trim();
                }
            }
            catch { }
            return "0.1.0";
        }

        private string GetBundledAddonVersion()
        {
            return ReadTocVersion(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PetRP", "PetRP.toc"));
        }

        private string GetInstalledAddonVersion()
        {
            if (string.IsNullOrEmpty(wowDir)) return "0.0.0";
            return ReadTocVersion(Path.Combine(wowDir, "Interface", "AddOns", "PetRP", "PetRP.toc"));
        }

        private static string NormalizeAddonFileText(string path)
        {
            if (!File.Exists(path)) return "";
            return File.ReadAllText(path, Encoding.UTF8).Replace("\r\r\n", "\n").Replace("\r\n", "\n").Trim();
        }

        private bool IsWoWAddonOutdated(out string installedVer, out string bundledVer, out List<string> changedFiles)
        {
            installedVer = GetInstalledAddonVersion();
            bundledVer = GetBundledAddonVersion();
            changedFiles = new List<string>();

            if (string.IsNullOrEmpty(wowDir)) return false;
            string srcAddonDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PetRP");
            string targetAddonDir = Path.Combine(wowDir, "Interface", "AddOns", "PetRP");
            if (!Directory.Exists(srcAddonDir) || !Directory.Exists(targetAddonDir)) return false;

            foreach (string rel in new[] { "PetRP.toc", "core.lua", "ui.lua" })
            {
                string srcFile = Path.Combine(srcAddonDir, rel);
                string dstFile = Path.Combine(targetAddonDir, rel);
                if (!File.Exists(dstFile) || !string.Equals(NormalizeAddonFileText(srcFile), NormalizeAddonFileText(dstFile), StringComparison.Ordinal))
                    changedFiles.Add(rel);
            }

            bool versionMismatch = !string.Equals(installedVer, bundledVer, StringComparison.OrdinalIgnoreCase);
            return versionMismatch || changedFiles.Count > 0;
        }

        private void CheckAndPromptWoWAddonInstall()
        {
            if (string.IsNullOrEmpty(wowDir))
            {
                AppendLog("System", "World of Warcraft folder was not auto-detected. Open '⚙ Options' -> '📂 Browse WoW Folder...' to select your WoW directory.");
                return;
            }

            string targetAddonDir = Path.Combine(wowDir, "Interface", "AddOns", "PetRP");
            AppendLog("System", "Active WoW Folder: " + wowDir + " | Watching Chat Log: " + LogPath);
            if (!IsWoWAddonInstalled())
            {
                string bundledVer = GetBundledAddonVersion();
                DialogResult dr = MessageBox.Show(this,
                    "The PetRP WoW Addon was not detected in your World of Warcraft folder:\r\n" +
                    targetAddonDir + "\r\n\r\nWould you like PetRP-Companion to install PetRP v" + bundledVer + " automatically now?",
                    "PetRP WoW Addon Not Installed",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (dr == DialogResult.Yes)
                    InstallOrReinstallWoWAddon(true);
                return;
            }

            string instVer, bndVer;
            List<string> changedFiles;
            if (IsWoWAddonOutdated(out instVer, out bndVer, out changedFiles))
            {
                string details = changedFiles.Count > 0 ? string.Join(", ", changedFiles.ToArray()) : "PetRP.toc";
                AppendLog("System", string.Format("Outdated PetRP WoW Addon detected in WoW folder (Installed: v{0}, Bundled: v{1}, Modified: {2}).", instVer, bndVer, details));

                DialogResult drUpdate = MessageBox.Show(this,
                    "An older version of the PetRP WoW Addon was detected in your World of Warcraft folder!\r\n\r\n" +
                    "• Installed Version: v" + instVer + "\r\n" +
                    "• New Bundled Version: v" + bndVer + "\r\n" +
                    "• Updated Files: " + details + "\r\n" +
                    "• Location: " + targetAddonDir + "\r\n\r\n" +
                    "Would you like to update the WoW Addon now?\r\n" +
                    "(Your stored companion messages and settings will be preserved.)",
                    "PetRP WoW Addon Update Available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (drUpdate == DialogResult.Yes)
                {
                    if (InstallOrReinstallWoWAddon(false))
                    {
                        lock (poolLock) { AdvanceSyncSequenceLocked(); }
                        WriteLuaState();
                        UpdateSignalSoundFile();
                        MessageBox.Show(this,
                            "PetRP WoW Addon has been updated to v" + bndVer + "!\r\n\r\n" +
                            "If World of Warcraft is currently open, click the Sync button on your PetRP bar (or type /reload) to load the updated addon.",
                            "PetRP Addon Updated",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
            }
            else
            {
                AppendLog("System", "PetRP WoW Addon is up to date (v" + instVer + ").");
            }
        }

        private bool InstallOrReinstallWoWAddon(bool showSuccessPopup)
        {
            try
            {
                if (string.IsNullOrEmpty(wowDir) || !Directory.Exists(wowDir))
                {
                    MessageBox.Show(this, "World of Warcraft folder is not set. Please click '📂 Browse WoW Folder...' in Options first.", "WoW Folder Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                string srcAddonDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PetRP");
                if (!Directory.Exists(srcAddonDir) || !File.Exists(Path.Combine(srcAddonDir, "PetRP.toc")))
                {
                    MessageBox.Show(this, "Could not find the source '.\\PetRP' addon folder next to PetRP-Companion.exe:\r\n" + srcAddonDir, "Source Addon Folder Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                string targetAddonDir = Path.Combine(wowDir, "Interface", "AddOns", "PetRP");
                int copiedCount = CopyAddonDirectory(srcAddonDir, targetAddonDir);
                CopyOpenDyslexicToAddonFonts(targetAddonDir);

                AppendLog("System", "Installed/Reinstalled PetRP WoW Addon (" + copiedCount + " files) to " + targetAddonDir);
                if (showSuccessPopup)
                {
                    MessageBox.Show(this,
                        "The PetRP WoW Addon has been installed to:\r\n" + targetAddonDir + "\r\n\r\n" +
                        "• Files installed: PetRP.toc, core.lua, ui.lua, response.lua, and fonts\\OpenDyslexic\r\n" +
                        "• In-game: Type /reload (or enable 'PetRP' in your AddOns menu at Character Select).",
                        "PetRP WoW Addon Installed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to install PetRP WoW Addon: " + ex.Message, "Addon Install Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static int CopyAddonDirectory(string srcAddonDir, string targetAddonDir)
        {
            Directory.CreateDirectory(targetAddonDir);
            int copied = 0;
            foreach (string filePath in Directory.GetFiles(srcAddonDir, "*", SearchOption.AllDirectories))
            {
                string relFile = filePath.Substring(srcAddonDir.Length).TrimStart('\\', '/');
                string destFile = Path.Combine(targetAddonDir, relFile);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile));

                if (string.Equals(Path.GetFileName(filePath), "response.lua", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(destFile) && new FileInfo(destFile).Length > 10)
                {
                    continue;
                }
                File.Copy(filePath, destFile, true);
                copied++;
            }
            return copied;
        }

        private static void CopyOpenDyslexicToAddonFonts(string targetAddonDir)
        {
            string localFontDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts");
            string targetAddonFonts = Path.Combine(targetAddonDir, "fonts");
            Directory.CreateDirectory(targetAddonFonts);
            foreach (string fn in new[] { "OpenDyslexic-Regular.ttf", "OpenDyslexic-Bold.ttf" })
            {
                string srcFont = Path.Combine(localFontDir, fn);
                if (File.Exists(srcFont))
                    File.Copy(srcFont, Path.Combine(targetAddonFonts, fn), true);
            }
        }

        private void InstallOpenDyslexicToWoW()
        {
            try
            {
                if (string.IsNullOrEmpty(wowDir) || !Directory.Exists(wowDir))
                {
                    MessageBox.Show(this, "Please select or auto-detect your World of Warcraft folder first.", "WoW Folder Not Set", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string srcReg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts", "OpenDyslexic-Regular.ttf");
                if (!File.Exists(srcReg))
                {
                    MessageBox.Show(this, "OpenDyslexic-Regular.ttf was not found in the local fonts folder yet.", "Font Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                CopyOpenDyslexicToAddonFonts(Path.Combine(wowDir, "Interface", "AddOns", "PetRP"));

                string wowRootFonts = Path.Combine(wowDir, "Fonts");
                Directory.CreateDirectory(wowRootFonts);
                foreach (string wt in new[] { "FRIZQT__.TTF", "ARIALN.TTF", "MORPHEUS.TTF", "skurri.ttf" })
                    File.Copy(srcReg, Path.Combine(wowRootFonts, wt), true);

                AppendLog("System", "Installed OpenDyslexic into WoW (" + wowRootFonts + " & PetRP\\fonts)!");
                MessageBox.Show(this,
                    "OpenDyslexic has been installed into World of Warcraft!\r\n\r\n" +
                    "• Client UI & Chat Fonts: " + wowRootFonts + " (FRIZQT__.TTF, ARIALN.TTF, MORPHEUS.TTF, skurri.ttf)\r\n" +
                    "• Total RP 3 SharedMedia Font: 'OpenDyslexic' registered via PetRP\r\n\r\n" +
                    "Note: Restart Wow.exe (not just /reload) for WoW's engine to load the new root Fonts\\*.TTF files.",
                    "OpenDyslexic Installed in WoW", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed installing OpenDyslexic to WoW: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // =====================================================================
        // 5. TRP3 DATA PARSING & COMPANION PROFILE CARD RENDERING
        // =====================================================================

        private void LoadLibRPMediaIcons()
        {
            if (string.IsNullOrEmpty(wowDir)) return;
            try
            {
                string trpDir = Path.Combine(wowDir, "Interface", "AddOns", "totalRP3");
                if (!Directory.Exists(trpDir)) return;
                foreach (string f in Directory.GetFiles(trpDir, "*.lua", SearchOption.AllDirectories))
                {
                    if (f.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0 || f.IndexOf("LibRPMedia", StringComparison.OrdinalIgnoreCase) >= 0)
                        ParseLibRPMediaLuaFile(File.ReadAllText(f, Encoding.UTF8));
                }
            }
            catch { }
        }

        private void ParseLibRPMediaLuaFile(string content)
        {
            foreach (Match m in Regex.Matches(content, @"\[(\d+)\]\s*=\s*\""([^\""]+)\"""))
                libRpMediaIcons[m.Groups[1].Value] = m.Groups[2].Value.ToLowerInvariant();
            foreach (Match m2 in Regex.Matches(content, @"(\d{1,8})\s*,\s*\""([a-zA-Z0-9_]+)\"""))
                libRpMediaIcons[m2.Groups[1].Value] = m2.Groups[2].Value.ToLowerInvariant();

            MatchCollection seq = Regex.Matches(content, @"\""((?:ability|achievement|inv|spell|trade|pet|racial|classicon|ui)_[a-z0-9_]+)\""", RegexOptions.IgnoreCase);
            if (seq.Count <= 500) return;
            for (int idx = 0; idx < seq.Count; idx++)
            {
                string indexKey = (idx + 1).ToString();
                if (!libRpMediaIcons.ContainsKey(indexKey))
                    libRpMediaIcons[indexKey] = seq[idx].Groups[1].Value.ToLowerInvariant();
            }
        }

        private static string ExtractBalancedLuaTable(string raw, int startSearchIdx)
        {
            int openIdx = raw.IndexOf('{', startSearchIdx);
            if (openIdx < 0) return "";
            int depth = 0;
            bool inQuotes = false;
            for (int i = openIdx; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '"' && (i == 0 || raw[i - 1] != '\\'))
                    inQuotes = !inQuotes;
                else if (!inQuotes)
                {
                    if (c == '{') depth++;
                    else if (c == '}' && --depth == 0)
                        return raw.Substring(openIdx, i - openIdx + 1);
                }
            }
            return raw.Substring(openIdx);
        }

        private static string UnescapeLuaString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\r\\n", "\r\n").Replace("\\n", "\r\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static string MatchGroup(string text, string pattern)
        {
            Match m = Regex.Match(text, pattern);
            return m.Success ? m.Groups[1].Value : "";
        }

        private static void ParseTRP3AboutText(string rawTx, out string extractedImgTag, out string cleanAbout)
        {
            extractedImgTag = "";
            if (string.IsNullOrEmpty(rawTx))
            {
                cleanAbout = "";
                return;
            }
            string text = UnescapeLuaString(rawTx);
            Match m = Regex.Match(text, @"\{(?:img|icon)\s*:\s*([^:}]+)(?::[^}]*)?\}", RegexOptions.IgnoreCase);
            if (m.Success) extractedImgTag = m.Groups[1].Value.Trim();

            string cleaned = Regex.Replace(text, @"\{(?:img|icon)\s*:[^}]+\}", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\{/?(?:h1|h2|h3|p(?::[a-zA-Z]+)?|col(?::[0-9a-fA-F]{6})?|link[^\}]*)\}", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\|c[0-9a-fA-F]{8}|\|r", "");
            cleanAbout = Regex.Replace(cleaned, @"(\r?\n){3,}", "\r\n\r\n").Trim();
        }

        private static GlanceSlotData[] ParseGlanceSlots(string peBlock)
        {
            GlanceSlotData[] slots = new GlanceSlotData[5];
            for (int i = 0; i < 5; i++) slots[i] = new GlanceSlotData();
            if (string.IsNullOrEmpty(peBlock)) return slots;

            for (int slot = 1; slot <= 5; slot++)
            {
                int slotIdx = peBlock.IndexOf("[\"" + slot + "\"]");
                if (slotIdx < 0) continue;
                string slotTable = ExtractBalancedLuaTable(peBlock, slotIdx);
                bool active = slotTable.IndexOf("[\"AC\"] = true") >= 0 || slotTable.IndexOf("[\"AC\"]=true") >= 0;
                string ti = UnescapeLuaString(MatchGroup(slotTable, @"\[\""TI\""\]\s*=\s*\""((?:[^\""\\]|\\.)*)\"""));
                string tx = UnescapeLuaString(MatchGroup(slotTable, @"\[\""TX\""\]\s*=\s*\""((?:[^\""\\]|\\.)*)\"""));
                slots[slot - 1] = new GlanceSlotData
                {
                    Active = active && (!string.IsNullOrEmpty(ti) || !string.IsNullOrEmpty(tx)),
                    Title = ti,
                    Text = tx,
                    Icon = MatchGroup(slotTable, @"\[\""IC\""\]\s*=\s*\""([^\""]+)\""")
                };
            }
            return slots;
        }

        private void LoadTRP3Data()
        {
            string prevSelected = cmbPetSelect.Text;
            profiles.Clear();
            savedIconNames.Clear();
            seenPetsMap.Clear();
            cmbPetSelect.Items.Clear();
            string summonedPet = null;

            string wtfRoot = string.IsNullOrEmpty(wowDir) ? "" : Path.Combine(wowDir, "WTF", "Account");
            if (!string.IsNullOrEmpty(wtfRoot) && Directory.Exists(wtfRoot))
            {
                LoadSavedIconMap(wtfRoot);
                foreach (string file in Directory.GetFiles(wtfRoot, "totalRP3.lua", SearchOption.AllDirectories))
                    ParseTotalRP3SavedVarsFile(file, ref summonedPet);
            }

            List<string> allPets = new List<string>();
            HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string k in profiles.Keys)
            {
                if (!string.IsNullOrWhiteSpace(k) && added.Add(k)) allPets.Add(k);
            }
            foreach (string k in seenPetsMap.Keys)
            {
                if (!string.IsNullOrWhiteSpace(k) && added.Add(k)) allPets.Add(k);
            }
            lock (poolLock)
            {
                foreach (string k in new List<string>(petPoolsMap.Keys))
                {
                    string canon = GetCanonicalPetKey(k);
                    if (!string.IsNullOrWhiteSpace(canon) && added.Add(canon)) allPets.Add(canon);
                }
                foreach (string p in allPets)
                {
                    if (profiles.ContainsKey(p))
                        GetPoolsForPetLocked(p, true);
                }
            }

            cmbPetSelect.Items.Add(NO_PET_LABEL);
            foreach (string p in allPets)
                cmbPetSelect.Items.Add(p);

            if (!string.IsNullOrEmpty(prevSelected) && cmbPetSelect.Items.Contains(prevSelected))
                cmbPetSelect.Text = prevSelected;
            else if (!string.IsNullOrEmpty(summonedPet) && added.Contains(summonedPet))
                cmbPetSelect.Text = summonedPet;
            else if (allPets.Count > 0)
                cmbPetSelect.Text = allPets[0];
            else
                cmbPetSelect.Text = NO_PET_LABEL;

            UpdatePetCard(cmbPetSelect.Text);
            RefreshStoredMessagesListUI();
            AppendLog("System", "Synced " + profiles.Count + " TRP3 Companion profile(s) and " + allPets.Count + " total seen pet(s).");
        }

        private void LoadSavedIconMap(string wtfRoot)
        {
            masterNamesSet.Clear();
            foreach (string pFile in Directory.GetFiles(wtfRoot, "PetRP.lua", SearchOption.AllDirectories))
            {
                string pRaw = File.ReadAllText(pFile, Encoding.UTF8);
                int masterIdx = pRaw.IndexOf("[\"masterNames\"]");
                if (masterIdx >= 0)
                {
                    string masterTable = ExtractBalancedLuaTable(pRaw, masterIdx);
                    foreach (Match mm in Regex.Matches(masterTable, "\"([^\"]+)\""))
                    {
                        string mn = mm.Groups[1].Value.Trim();
                        if (mn.Length >= 2 && !string.Equals(mn, "masterNames", StringComparison.OrdinalIgnoreCase))
                            masterNamesSet.Add(mn);
                    }
                }
                int seenIdx = pRaw.IndexOf("[\"seenPets\"]");
                if (seenIdx >= 0)
                {
                    string seenTable = ExtractBalancedLuaTable(pRaw, seenIdx);
                    foreach (Match sm in Regex.Matches(seenTable, @"\[\""([^\""]+)\""\]\s*=\s*\""([^\""]*)\"""))
                        seenPetsMap[sm.Groups[1].Value] = sm.Groups[2].Value;
                }
                int settingsIdx = pRaw.IndexOf("[\"petSettings\"]");
                if (settingsIdx >= 0)
                {
                    string settingsTable = ExtractBalancedLuaTable(pRaw, settingsIdx);
                    foreach (Match pm in Regex.Matches(settingsTable, @"\[\""([^\""]+)\""\]\s*=\s*\{"))
                    {
                        string pKey = pm.Groups[1].Value;
                        string pBlock = ExtractBalancedLuaTable(settingsTable, pm.Index);
                        Match intMatch = Regex.Match(pBlock, @"\[\""interval\""\]\s*=\s*(\d+)");
                        int parsedInt;
                        if (intMatch.Success && int.TryParse(intMatch.Groups[1].Value, out parsedInt) && parsedInt >= 15)
                        {
                            lock (poolLock)
                            {
                                GetPoolsForPetLocked(pKey, false).IntervalSeconds = Math.Min(3600, parsedInt);
                            }
                        }
                    }
                }
                foreach (Match im in Regex.Matches(pRaw, @"\[\""([^\""]+)\""\]\s*=\s*\""([^\""]+)\"""))
                {
                    string k = im.Groups[1].Value;
                    if (!string.Equals(k, "point", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "relPoint", StringComparison.OrdinalIgnoreCase))
                    {
                        savedIconNames[k] = im.Groups[2].Value;
                    }
                }
            }
        }

        private void ParseTotalRP3SavedVarsFile(string filePath, ref string summonedPet)
        {
            string raw = File.ReadAllText(filePath, Encoding.UTF8);
            foreach (Match fnm in Regex.Matches(raw, @"\[\""FN\""\]\s*=\s*\""([^\""]+)\"""))
            {
                string fn = UnescapeLuaString(fnm.Groups[1].Value).Trim();
                if (fn.Length >= 2) masterNamesSet.Add(fn);
            }
            int compIdx = raw.IndexOf("TRP3_Companions");
            if (compIdx < 0) return;

            string compSection = ExtractBalancedLuaTable(raw, compIdx);
            Match sm = Regex.Match(compSection, @"\[\""summonedPetID\""\]\s*=\s*\""([^\""]+)\""");
            if (sm.Success) summonedPet = sm.Groups[1].Value;

            int playerIdx = compSection.IndexOf("[\"player\"]");
            string playerTable = playerIdx >= 0 ? ExtractBalancedLuaTable(compSection, playerIdx) : compSection;

            foreach (Match pm in Regex.Matches(playerTable, @"\[\""([0-9A-Za-z_]+)\""\]\s*=\s*\{"))
            {
                string entryBlock = ExtractBalancedLuaTable(playerTable, pm.Index);
                if (entryBlock.IndexOf("[\"data\"]") >= 0 || entryBlock.IndexOf("[\"links\"]") >= 0)
                    RegisterCompanionEntry(entryBlock);
            }
        }

        private void RegisterCompanionEntry(string entryBlock)
        {
            int dataIdx = entryBlock.IndexOf("[\"data\"]");
            string dataBlock = dataIdx >= 0 ? ExtractBalancedLuaTable(entryBlock, dataIdx) : entryBlock;
            int peIdx = entryBlock.IndexOf("[\"PE\"]");
            string peBlock = peIdx >= 0 ? ExtractBalancedLuaTable(entryBlock, peIdx) : "";
            int linksIdx = entryBlock.IndexOf("[\"links\"]");
            string linksBlock = linksIdx >= 0 ? ExtractBalancedLuaTable(entryBlock, linksIdx) : entryBlock;

            string rawAbout = MatchGroup(dataBlock, @"\[\""TX\""\]\s*=\s*\""((?:[^\""\\]|\\.)*)\""");
            string aboutImg, cleanAbout;
            ParseTRP3AboutText(rawAbout, out aboutImg, out cleanAbout);

            CompanionProfile cp = new CompanionProfile
            {
                RPName = UnescapeLuaString(MatchGroup(dataBlock, @"\[\""NA\""\]\s*=\s*\""((?:[^\""\\]|\\.)*)\""")),
                NameHex = MatchGroup(dataBlock, @"\[\""NH\""\]\s*=\s*\""([0-9a-fA-F]{6})\"""),
                Title = UnescapeLuaString(MatchGroup(dataBlock, @"\[\""TI\""\]\s*=\s*\""((?:[^\""\\]|\\.)*)\""")),
                Icon = MatchGroup(dataBlock, @"\[\""IC\""\]\s*=\s*\""([^\""]+)\"""),
                CleanAbout = cleanAbout,
                AboutImageTag = aboutImg,
                GlanceSlots = ParseGlanceSlots(peBlock)
            };

            int linkCount = 0;
            foreach (Match lm in Regex.Matches(linksBlock, @"\[\""([^\""]+)\""\]\s*=\s*(?:\""(?:BATTLE_PET|PET)\""|1|2)"))
            {
                string link = lm.Groups[1].Value;
                cp.BoundName = link;
                profiles[link] = cp;
                linkCount++;
            }
            if (linkCount == 0 && !string.IsNullOrWhiteSpace(cp.RPName))
            {
                cp.BoundName = cp.RPName;
                profiles[cp.RPName] = cp;
            }
        }

        private static Color ParseTRP3Color(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 6) return TRP3_GOLD;
            try
            {
                return Color.FromArgb(
                    int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
                    int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                    int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
            }
            catch { return TRP3_GOLD; }
        }

        private void UpdatePetCard(string petKey)
        {
            int portraitSz = Math.Max(32, picPetIcon.Width > 0 ? picPetIcon.Width : ScalePx(54));
            int glanceIconSz = Math.Max(24, glanceIcons[0] != null && glanceIcons[0].Width > 0 ? glanceIcons[0].Width : ScalePx(32));

            if (string.IsNullOrWhiteSpace(petKey) || string.Equals(petKey.Trim(), NO_PET_LABEL, StringComparison.OrdinalIgnoreCase))
            {
                lblPetHeadline.ForeColor = TRP3_MUTED;
                lblPetHeadline.Text = "PetRP Disabled — No Pet Loaded";
                lblPetSubtitle.Text = "Summon a companion in WoW or select any seen companion from the Pet dropdown below";
                LoadExtractedAboutTextureAsync(picPetIcon, "Interface\\PVPFrame\\Icons\\PVP-Banner-Emblem-40", TRP3_MUTED, portraitSz);
                ClearGlancesAndAbout(glanceIconSz, "No companion loaded. All in-game PetRP message pools are unloaded while no profiled pet is active.");
                return;
            }

            string trimmedPet = petKey.Trim();
            if (numInterval != null)
            {
                int petInt = 180;
                lock (poolLock) { petInt = GetPoolsForPetLocked(trimmedPet, false).IntervalSeconds; }
                petInt = Math.Max((int)numInterval.Minimum, Math.Min((int)numInterval.Maximum, petInt));
                if ((int)numInterval.Value != petInt) numInterval.Value = petInt;
            }
            CompanionProfile cp;
            if (!profiles.TryGetValue(trimmedPet, out cp))
            {
                lblPetHeadline.ForeColor = TRP3_GOLD;
                lblPetHeadline.Text = trimmedPet + "  (No TRP3 Profile)";
                lblPetSubtitle.Text = "In-Game: Click the PetRP icon to open TRP3 Companion dialog to make one";
                string knownIcon = "";
                if (!seenPetsMap.TryGetValue(trimmedPet, out knownIcon) || string.IsNullOrEmpty(knownIcon))
                    savedIconNames.TryGetValue(trimmedPet, out knownIcon);
                LoadIconIntoPictureBox(picPetIcon, trimmedPet, trimmedPet, knownIcon, TRP3_GOLD, portraitSz, "🐾");
                ClearGlancesAndAbout(glanceIconSz, "This companion does not have a Total RP 3 profile yet.\r\n\r\nIn World of Warcraft, click the PetRP icon when '" + trimmedPet + "' is summoned to open the TRP3 Companion Profiles dialog and create one, then click 'Sync TRP3'.");
                return;
            }

            Color nameColor = ParseTRP3Color(cp.NameHex);
            lblPetHeadline.ForeColor = nameColor;
            lblPetHeadline.Text = string.Format("{0}  ({1})", string.IsNullOrEmpty(cp.RPName) ? cp.BoundName : cp.RPName, cp.BoundName);
            lblPetSubtitle.Text = string.Format("\"{0}\"   —   TRP3 Color: #{1}   |   Icon ID: {2}",
                string.IsNullOrEmpty(cp.Title) ? "Companion" : cp.Title,
                string.IsNullOrEmpty(cp.NameHex) ? "ffd100" : cp.NameHex,
                string.IsNullOrEmpty(cp.Icon) ? "Default" : cp.Icon);

            LoadIconIntoPictureBox(picPetIcon, cp.BoundName, cp.RPName, cp.Icon, nameColor, portraitSz, "🐾");

            for (int i = 0; i < 5; i++)
                SetGlanceSlotUI(i, cp.GlanceSlots[i], glanceIconSz);

            UpdateAboutBoxUI(cp);
        }

        private void SetGlanceSlotUI(int index, GlanceSlotData slot, int glanceIconSz)
        {
            if (slot != null && slot.Active)
            {
                glanceBoxes[index].OuterGold = TRP3_BRONZE;
                glanceTitles[index].ForeColor = TRP3_GOLD;
                glanceTitles[index].Text = string.IsNullOrEmpty(slot.Title) ? ("Glance #" + (index + 1)) : slot.Title;
                glanceTexts[index].ForeColor = TRP3_IVORY;
                glanceTexts[index].Text = string.IsNullOrEmpty(slot.Text) ? "(No description)" : slot.Text;
                trpTooltip.SetToolTip(glanceTitles[index], slot.Title + "\r\n\r\n" + slot.Text);
                trpTooltip.SetToolTip(glanceIcons[index], slot.Title + " (Icon: " + (string.IsNullOrEmpty(slot.Icon) ? "Default" : slot.Icon) + ")");
                LoadIconIntoPictureBox(glanceIcons[index], "", slot.Title, slot.Icon, TRP3_GOLD, glanceIconSz, "✦");
            }
            else
            {
                glanceBoxes[index].OuterGold = Color.FromArgb(75, 58, 30);
                glanceTitles[index].ForeColor = TRP3_MUTED;
                glanceTitles[index].Text = "Slot " + (index + 1);
                glanceTexts[index].ForeColor = Color.FromArgb(105, 92, 75);
                glanceTexts[index].Text = "Empty";
                trpTooltip.SetToolTip(glanceTitles[index], "Empty At First Glance Slot #" + (index + 1));
                trpTooltip.SetToolTip(glanceIcons[index], "Empty Slot");
                RenderIconBadge(glanceIcons[index], "—", Color.FromArgb(80, 65, 42), glanceIconSz);
            }
            glanceBoxes[index].Invalidate();
        }

        private void UpdateAboutBoxUI(CompanionProfile cp)
        {
            bool hasImg = !string.IsNullOrEmpty(cp.AboutImageTag);
            picAboutExtractedImg.Visible = hasImg;
            LayoutAboutBox();
            if (hasImg)
            {
                int aboutImgSz = Math.Max(32, picAboutExtractedImg.Width > 0 ? picAboutExtractedImg.Width : ScalePx(54));
                trpTooltip.SetToolTip(picAboutExtractedImg, "Extracted About Image: " + cp.AboutImageTag);
                LoadExtractedAboutTextureAsync(picAboutExtractedImg, cp.AboutImageTag, TRP3_BRONZE, aboutImgSz);
            }
            txtAboutBody.Text = !string.IsNullOrEmpty(cp.CleanAbout)
                ? cp.CleanAbout
                : (hasImg ? "About Emblem: " + cp.AboutImageTag : "No About / Backstory text configured in Total RP 3.");
        }

        private void ClearGlancesAndAbout(int glanceIconSz, string aboutMessage)
        {
            for (int i = 0; i < 5; i++)
                SetGlanceSlotUI(i, null, glanceIconSz);
            picAboutExtractedImg.Visible = false;
            LayoutAboutBox();
            txtAboutBody.Text = aboutMessage;
        }

        // =====================================================================
        // 6. ICON RESOLUTION & FRAMED PICTUREBOX RENDERING
        // =====================================================================

        private async void LoadExtractedAboutTextureAsync(PictureBox targetBox, string rawTexturePath, Color borderColor, int boxSize)
        {
            string normalized = rawTexturePath.Trim().Replace("\\", "/");
            if (normalized.StartsWith("Interface/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring("Interface/".Length);

            string safeFileName = Path.GetFileNameWithoutExtension(normalized).ToLowerInvariant();
            string cachedPng = Path.Combine(ICON_CACHE_DIR, safeFileName + ".png");
            string cachedJpg = Path.Combine(ICON_CACHE_DIR, safeFileName + ".jpg");

            if (TryApplyCachedImage(targetBox, cachedPng, cachedJpg, borderColor, boxSize)) return;
            RenderIconBadge(targetBox, "🔥", borderColor, boxSize);

            await Task.Run(() =>
            {
                string[] urls =
                {
                    "https://raw.githubusercontent.com/Gethe/wow-ui-textures/live/" + normalized + ".PNG",
                    "https://raw.githubusercontent.com/Gethe/wow-ui-textures/live/" + normalized + ".png",
                    "https://wow.zamimg.com/images/wow/icons/large/" + safeFileName + ".jpg"
                };
                foreach (string url in urls)
                {
                    string dest = url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? cachedJpg : cachedPng;
                    if (TryDownloadFile(url, dest))
                    {
                        ApplyFramedImageToBox(targetBox, dest, borderColor, boxSize);
                        return;
                    }
                }
            });
        }

        private bool TryApplyCachedImage(PictureBox targetBox, string pngPath, string jpgPath, Color borderColor, int boxSize)
        {
            if (File.Exists(pngPath)) { ApplyFramedImageToBox(targetBox, pngPath, borderColor, boxSize); return true; }
            if (File.Exists(jpgPath)) { ApplyFramedImageToBox(targetBox, jpgPath, borderColor, boxSize); return true; }
            return false;
        }

        private static string StripIconPath(string iconKey)
        {
            string clean = (iconKey ?? "").Trim();
            if (!clean.Contains("\\") && !clean.Contains("/")) return clean;
            string[] parts = clean.Split('\\', '/');
            return parts[parts.Length - 1].Replace(".blp", "");
        }

        private static string ResolveIconNameOnline(string iconKey)
        {
            string cleaned = StripIconPath(iconKey).ToLowerInvariant();
            if (string.IsNullOrEmpty(cleaned) || !Regex.IsMatch(cleaned, @"^\d+$"))
                return cleaned;

            string html = TryDownloadString("https://www.wowhead.com/icon=" + cleaned);
            Match m = Regex.Match(html, @"images/wow/icons/(?:large|medium|small)/([a-z0-9_]+)\.jpg", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.ToLowerInvariant();

            string json = TryDownloadString("https://wago.tools/api/files?search=" + cleaned);
            Match m2 = Regex.Match(json, @"interface/icons/([a-z0-9_]+)\.blp", RegexOptions.IgnoreCase);
            return m2.Success ? m2.Groups[1].Value.ToLowerInvariant() : null;
        }

        private void ApplyFramedImageToBox(PictureBox targetBox, string imageFilePath, Color borderColor, int boxSize)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(imageFilePath);
                Bitmap framed = new Bitmap(boxSize, boxSize);
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Image rawImg = Image.FromStream(ms))
                using (Graphics g = Graphics.FromImage(framed))
                using (Pen p = new Pen(borderColor, 2.5f))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(rawImg, 2, 2, boxSize - 4, boxSize - 4);
                    g.DrawRectangle(p, 1, 1, boxSize - 3, boxSize - 3);
                }

                if (InvokeRequired)
                {
                    if (IsHandleCreated) BeginInvoke((Action)(() => { targetBox.Image = framed; }));
                }
                else
                {
                    targetBox.Image = framed;
                }
            }
            catch { }
        }

        private async void LoadIconIntoPictureBox(PictureBox targetBox, string boundName, string titleOrRpName, string iconKey, Color borderColor, int boxSize, string fallbackSymbol)
        {
            string cleanIconKey = StripIconPath(iconKey);
            foreach (string cand in new[] { cleanIconKey.ToLowerInvariant(), (boundName ?? "").ToLowerInvariant(), (titleOrRpName ?? "").ToLowerInvariant() })
            {
                if (!string.IsNullOrEmpty(cand) &&
                    TryApplyCachedImage(targetBox, Path.Combine(ICON_CACHE_DIR, cand + ".png"), Path.Combine(ICON_CACHE_DIR, cand + ".jpg"), borderColor, boxSize))
                {
                    return;
                }
            }

            RenderIconBadge(targetBox, ChooseFallbackBadgeText(boundName, titleOrRpName, fallbackSymbol, boxSize), borderColor, boxSize);

            await Task.Run(() =>
            {
                string resolvedName = ResolveIconTextureName(boundName, titleOrRpName, cleanIconKey);
                if (string.IsNullOrEmpty(resolvedName) || string.Equals(resolvedName, "ability_hunter_beastcall", StringComparison.OrdinalIgnoreCase))
                    return;

                string cachedFile = Path.Combine(ICON_CACHE_DIR, resolvedName + ".jpg");
                if (!File.Exists(cachedFile))
                    TryDownloadFile("https://wow.zamimg.com/images/wow/icons/large/" + resolvedName + ".jpg", cachedFile);

                if (File.Exists(cachedFile))
                    ApplyFramedImageToBox(targetBox, cachedFile, borderColor, boxSize);
            });
        }

        private static string ChooseFallbackBadgeText(string boundName, string titleOrRpName, string fallbackSymbol, int boxSize)
        {
            if (!string.IsNullOrEmpty(titleOrRpName) &&
                (titleOrRpName.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0 || titleOrRpName.IndexOf("chaos", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "🔥";
            }
            if (!string.IsNullOrEmpty(boundName) && boxSize >= 48)
                return boundName.Substring(0, Math.Min(2, boundName.Length)).ToUpperInvariant();
            return fallbackSymbol;
        }

        private string ResolveIconTextureName(string boundName, string titleOrRpName, string cleanIconKey)
        {
            string resolved = null;
            if (string.IsNullOrEmpty(boundName) || !savedIconNames.TryGetValue(boundName, out resolved))
            {
                if (string.IsNullOrEmpty(cleanIconKey) || (!savedIconNames.TryGetValue(cleanIconKey, out resolved) && !libRpMediaIcons.TryGetValue(cleanIconKey, out resolved)))
                    resolved = ResolveIconNameOnline(cleanIconKey);
            }
            if (string.IsNullOrEmpty(resolved) || Regex.IsMatch(resolved, @"^\d+$"))
            {
                bool isFire = titleOrRpName != null && (titleOrRpName.IndexOf("chaos", StringComparison.OrdinalIgnoreCase) >= 0 || titleOrRpName.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0);
                return isFire ? "spell_fire_felimmolation" : null;
            }
            return StripIconPath(resolved).ToLowerInvariant();
        }

        private void RenderIconBadge(PictureBox targetBox, string symbol, Color accentColor, int boxSize)
        {
            int sz = Math.Max(20, boxSize);
            Bitmap bmp = new Bitmap(sz, sz);
            using (Graphics g = Graphics.FromImage(bmp))
            using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, sz, sz), Color.FromArgb(52, 38, 22), Color.FromArgb(20, 14, 10), 45f))
            using (Pen borderPen = new Pen(accentColor, 2f))
            using (Font f = MakeFont(configuredFontSize, FontStyle.Bold))
            using (Brush tb = new SolidBrush(accentColor))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillRectangle(br, 0, 0, sz, sz);
                g.DrawRectangle(borderPen, 1, 1, sz - 3, sz - 3);
                SizeF m = g.MeasureString(symbol, f);
                g.DrawString(symbol, f, tb, (sz - m.Width) / 2, (sz - m.Height) / 2);
            }
            targetBox.Image = bmp;
        }

        // =====================================================================
        // 7. AGY CLI DISCOVERY, GOOGLE ACCOUNT & QUOTA DIALOG
        // =====================================================================

        private static bool IsProModel(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return false;
            string lower = modelId.ToLowerInvariant();
            return !lower.Contains("lite") && !lower.Contains("flash");
        }

        private void PopulateModelPricingList(List<string> discoveredModels)
        {
            cmbModel.Items.Clear();
            List<string> ids = (discoveredModels != null && discoveredModels.Count > 0)
                ? discoveredModels
                : new List<string> { "gemini-3.8-flash-low", "gemini-3.8-flash-medium", "gemini-3.8-flash-high", "gemini-4-argon-low", "gemini-4-argon-medium", "gemini-4-argon-high" };

            foreach (string id in ids)
            {
                if (!IsProModel(id))
                    cmbModel.Items.Add(new ModelOption { ModelId = id, DisplayLabel = id + " (free)" });
            }
            foreach (string id in ids)
            {
                if (IsProModel(id))
                    cmbModel.Items.Add(new ModelOption { ModelId = id, DisplayLabel = id + " (pro/paid)" });
            }
            if (cmbModel.Items.Count > 0) cmbModel.SelectedIndex = 0;
        }

        private string GetSelectedModelId()
        {
            ModelOption mo = cmbModel.SelectedItem as ModelOption;
            if (mo != null) return mo.ModelId;
            string raw = cmbModel.Text.Trim();
            int paren = raw.IndexOf(" (");
            return paren > 0 ? raw.Substring(0, paren).Trim() : (string.IsNullOrEmpty(raw) ? "gemini-3.8-flash-low" : raw);
        }

        private static string LocateAgyCliBinary()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string[] candidates =
            {
                Path.Combine(localApp, "Programs", "Antigravity", "bin", "agy.cmd"),
                Path.Combine(localApp, "Programs", "Antigravity", "bin", "antigravity.cmd"),
                Path.Combine(localApp, "Programs", "Antigravity", "resources", "app", "bin", "agy.cmd"),
                Path.Combine(localApp, "Antigravity", "bin", "agy.exe"),
                Path.Combine(localApp, "Antigravity", "bin", "agy.cmd"),
                Path.Combine(appData, "npm", "agy.cmd"),
                Path.Combine(userHome, ".gemini", "antigravity-cli", "bin", "agy.exe"),
                Path.Combine(userHome, ".local", "bin", "agy.exe")
            };
            foreach (string c in candidates)
            {
                if (File.Exists(c)) return "\"" + c + "\"";
            }
            return "agy";
        }

        private static bool IsValidUserGoogleEmail(string email)
        {
            if (string.IsNullOrEmpty(email)) return false;
            string low = email.ToLowerInvariant();
            string[] ignored =
            {
                "example.com", "test.com", "localhost", "chromium.org", "electronjs.org",
                "github.com", "npmjs.com", "microsoft.com", "w3.org", "mozilla.org",
                "googleapis.com", "gserviceaccount.com", "noreply", "no-reply",
                "antigravity.google", "schema.org", "vscode", "openssl", "python.org"
            };
            foreach (string ig in ignored)
            {
                if (low.Contains(ig)) return false;
            }
            return true;
        }

        private static string ExtractGoogleEmailFromContent(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] patterns =
            {
                @"(?:email|account|user_email|userEmail|login_hint|id)[\""\s:=]+([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})",
                @"([a-zA-Z0-9._%+-]+@(?:gmail|googlemail)\.com)"
            };
            foreach (string pat in patterns)
            {
                foreach (Match m in Regex.Matches(text, pat, RegexOptions.IgnoreCase))
                {
                    if (IsValidUserGoogleEmail(m.Groups[1].Value))
                        return m.Groups[1].Value;
                }
            }
            return "";
        }

        private static string DiscoverGoogleAccountFromDisk()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string gcloudEmail = TryReadGCloudEmail(appData);
            if (!string.IsNullOrEmpty(gcloudEmail)) return gcloudEmail;

            foreach (string file in GetCandidateAuthFiles(appData, userHome))
            {
                string email = TryExtractEmailFromFile(file);
                if (!string.IsNullOrEmpty(email)) return email;
            }
            return "";
        }

        private static string TryReadGCloudEmail(string appData)
        {
            try
            {
                string legacyCreds = Path.Combine(appData, "gcloud", "legacy_credentials");
                if (Directory.Exists(legacyCreds))
                {
                    foreach (string dir in Directory.GetDirectories(legacyCreds))
                    {
                        string folderName = Path.GetFileName(dir);
                        if (folderName.Contains("@") && IsValidUserGoogleEmail(folderName))
                            return folderName;
                    }
                }
                string gcloudDefault = Path.Combine(appData, "gcloud", "configurations", "config_default");
                return File.Exists(gcloudDefault) ? ExtractGoogleEmailFromContent(File.ReadAllText(gcloudDefault, Encoding.UTF8)) : "";
            }
            catch { return ""; }
        }

        private static List<string> GetCandidateAuthFiles(string appData, string userHome)
        {
            List<string> files = new List<string>();
            try
            {
                string agyAppData = Path.Combine(appData, "Antigravity");
                if (Directory.Exists(agyAppData))
                {
                    string[] rels =
                    {
                        Path.Combine(agyAppData, "User", "globalStorage", "storage.json"),
                        Path.Combine(agyAppData, "User", "globalStorage", "state.vscdb"),
                        Path.Combine(agyAppData, "Preferences"),
                        Path.Combine(agyAppData, "Local State")
                    };
                    foreach (string p in rels)
                        if (File.Exists(p)) files.Add(p);

                    string levelDbDir = Path.Combine(agyAppData, "Local Storage", "leveldb");
                    if (Directory.Exists(levelDbDir))
                    {
                        files.AddRange(Directory.GetFiles(levelDbDir, "*.ldb"));
                        files.AddRange(Directory.GetFiles(levelDbDir, "*.log"));
                    }
                }

                string geminiDir = Path.Combine(userHome, ".gemini");
                if (Directory.Exists(geminiDir))
                {
                    files.AddRange(Directory.GetFiles(geminiDir, "*.*", SearchOption.TopDirectoryOnly));
                    string agySub = Path.Combine(geminiDir, "antigravity");
                    if (Directory.Exists(agySub)) files.AddRange(Directory.GetFiles(agySub, "*.*", SearchOption.TopDirectoryOnly));
                }
            }
            catch { }
            return files;
        }

        private static string TryExtractEmailFromFile(string file)
        {
            try
            {
                FileInfo fi = new FileInfo(file);
                if (!fi.Exists || fi.Length > 15 * 1024 * 1024) return "";
                byte[] rawBytes = File.ReadAllBytes(file);
                string found = ExtractGoogleEmailFromContent(Encoding.UTF8.GetString(rawBytes));
                return !string.IsNullOrEmpty(found) ? found : ExtractGoogleEmailFromContent(Encoding.Unicode.GetString(rawBytes));
            }
            catch { return ""; }
        }

        private UsageStatsSummary CalculateUsageSummary(bool queryLiveCli)
        {
            UsageStatsSummary s = new UsageStatsSummary { ProQuotaLimit = configuredProQuota };
            if (File.Exists(GEN_LOG_PATH))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(GEN_LOG_PATH, Encoding.UTF8))
                    {
                        Match m = Regex.Match(line, @"Model=([^\s|]+)\s*\|\s*Tokens:\s*in~(\d+)\s+out~(\d+)");
                        if (!m.Success) continue;
                        int tok = int.Parse(m.Groups[2].Value) + int.Parse(m.Groups[3].Value);
                        if (IsProModel(m.Groups[1].Value.Trim())) { s.ProMessagesUsed++; s.ProTokensUsed += tok; }
                        else { s.FreeMessagesUsed++; s.FreeTokensUsed += tok; }
                    }
                }
                catch { }
            }
            if (queryLiveCli)
            {
                string err;
                string outText = RunCliCommand("-p \"/usage\"", 4500, out err);
                s.LiveCliQuotaReport = !string.IsNullOrEmpty(outText) ? outText : err;
            }
            return s;
        }

        private void UpdateTopAccountBannerUI()
        {
            UsageStatsSummary usage = CalculateUsageSummary(false);
            int remaining = Math.Max(0, usage.ProQuotaLimit - usage.ProMessagesUsed);
            int pctLeft = usage.ProQuotaLimit > 0 ? (int)Math.Round((remaining * 100.0) / usage.ProQuotaLimit) : 100;
            string acctText = string.IsNullOrEmpty(detectedGoogleAccount) ? "Not Set (Click '📊 Usage')" : detectedGoogleAccount;

            lblAccountBanner.ForeColor = TRP3_GOLD_SOFT;
            lblAccountBanner.Text = string.Format(
                "Google Account: {0}  |  Paid/Pro: YES (Pro/Argon)  |  Pro Quota: {1} used / {2} left ({3}%)",
                acctText, usage.ProMessagesUsed, remaining, pctLeft);
        }

        private async void ResolveAccountAndModelsAsync()
        {
            await Task.Run(() =>
            {
                resolvedAgyExecutable = LocateAgyCliBinary();
                string foundEmail = DiscoverGoogleAccountFromDisk();
                if (!string.IsNullOrEmpty(foundEmail)) detectedGoogleAccount = foundEmail;

                string stderr;
                string output = RunCliCommand("models", 4500, out stderr);
                List<string> foundModels = ParseModelsOutput(output);

                BeginInvoke((Action)(() =>
                {
                    UpdateTopAccountBannerUI();
                    if (foundModels.Count > 0) PopulateModelPricingList(foundModels);
                }));
            });
        }

        private List<string> ParseModelsOutput(string output)
        {
            List<string> foundModels = new List<string>();
            if (string.IsNullOrEmpty(output)) return foundModels;

            if (string.IsNullOrEmpty(detectedGoogleAccount))
            {
                string cliEmail = ExtractGoogleEmailFromContent(output);
                if (!string.IsNullOrEmpty(cliEmail)) detectedGoogleAccount = cliEmail;
            }
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim().TrimStart('*', '-', ' ');
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("Available", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("---"))
                    continue;
                string token = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                if (!string.IsNullOrEmpty(token)) foundModels.Add(token);
            }
            return foundModels;
        }

        private void ShowUsageDialog()
        {
            UsageStatsSummary usage = CalculateUsageSummary(false);
            int remaining = Math.Max(0, usage.ProQuotaLimit - usage.ProMessagesUsed);
            int pctLeft = usage.ProQuotaLimit > 0 ? Math.Max(0, 100 - Math.Min(100, (int)Math.Round((usage.ProMessagesUsed * 100.0) / usage.ProQuotaLimit))) : 100;

            Form dlg = new Form
            {
                Text = "Total RP 3 — AGY Google Account & Paid/Pro Quota",
                Size = new Size(ScalePx(560), ScalePx(430)),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = TRP3_BG_DARK,
                ForeColor = TRP3_IVORY,
                Font = MakeFont(configuredFontSize, FontStyle.Regular)
            };

            Label lblAcctTitle = MakeLabel("Google Account (AGY):", TRP3_GOLD);
            lblAcctTitle.Bounds = new Rectangle(ScalePx(14), ScalePx(14), ScalePx(200), ScalePx(24));
            lblAcctTitle.Font = MakeFont(configuredFontSize, FontStyle.Bold);

            TextBox txtAcct = new TextBox
            {
                Bounds = new Rectangle(ScalePx(220), ScalePx(12), ScalePx(310), ScalePx(26)),
                BackColor = TRP3_INSET_BG,
                ForeColor = TRP3_IVORY,
                Text = detectedGoogleAccount ?? ""
            };

            Label lblTier = MakeLabel("Paid / Pro Access: YES (Gemini 4 Argon & Pro Enabled)", TRP3_GREEN);
            lblTier.Bounds = new Rectangle(ScalePx(14), ScalePx(44), ScalePx(516), ScalePx(24));
            lblTier.Font = MakeFont(configuredFontSize, FontStyle.Bold);

            TRP3BorderPanel quotaCard = new TRP3BorderPanel { Bounds = new Rectangle(ScalePx(14), ScalePx(72), ScalePx(516), ScalePx(176)), BackColor = TRP3_PANEL_BG };
            Label lblPaidHeader = MakeLabel(string.Format("Paid / Pro Quota:  {0} Used  /  {1} Left ({2}% Remaining)", usage.ProMessagesUsed, remaining, pctLeft), TRP3_GOLD);
            lblPaidHeader.Bounds = new Rectangle(ScalePx(10), ScalePx(10), ScalePx(494), ScalePx(24));
            lblPaidHeader.Font = MakeFont(configuredFontSize, FontStyle.Bold);

            ProgressBar pbQuota = new ProgressBar
            {
                Bounds = new Rectangle(ScalePx(10), ScalePx(36), ScalePx(494), ScalePx(20)),
                Minimum = 0,
                Maximum = Math.Max(1, usage.ProQuotaLimit),
                Value = Math.Min(usage.ProQuotaLimit, usage.ProMessagesUsed)
            };

            Label lblBreakdown = MakeLabel(string.Format(
                "• Paid / Pro (Argon/Pro): {0} msgs (~{1} tok) | Left: {2} msgs\r\n" +
                "• Free Models (Flash):    {3} msgs (~{4} tok) | Left: Unlimited\r\n" +
                "• Session Total:          {5} msgs (~{6} tok)",
                usage.ProMessagesUsed, usage.ProTokensUsed, remaining,
                usage.FreeMessagesUsed, usage.FreeTokensUsed,
                totalMessages, promptTokens + outputTokens), TRP3_IVORY);
            lblBreakdown.Bounds = new Rectangle(ScalePx(10), ScalePx(64), ScalePx(494), ScalePx(72));

            Label lblLimit = MakeLabel("Paid/Pro Cycle Allowance:", TRP3_GOLD_SOFT);
            lblLimit.Bounds = new Rectangle(ScalePx(10), ScalePx(140), ScalePx(250), ScalePx(24));
            lblLimit.Font = MakeFont(configuredFontSize, FontStyle.Bold);

            NumericUpDown numQuota = new NumericUpDown
            {
                Bounds = new Rectangle(ScalePx(265), ScalePx(138), ScalePx(95), ScalePx(26)),
                Minimum = 10,
                Maximum = 100000,
                Value = configuredProQuota,
                BackColor = TRP3_INSET_BG,
                ForeColor = TRP3_IVORY
            };
            quotaCard.Controls.AddRange(new Control[] { lblPaidHeader, pbQuota, lblBreakdown, lblLimit, numQuota });

            TextBox txtLiveCli = MakeReadOnlyTextBox("Click 'Query Live AGY /usage' below to fetch real-time G1 credit & quota status from the AGY CLI...");
            txtLiveCli.Bounds = new Rectangle(ScalePx(14), ScalePx(256), ScalePx(516), ScalePx(80));
            txtLiveCli.ForeColor = TRP3_GOLD_SOFT;

            Button btnQueryCli = CreateTRP3Button("🔄 Query Live AGY /usage", Color.FromArgb(68, 50, 28));
            btnQueryCli.Bounds = new Rectangle(ScalePx(14), ScalePx(344), ScalePx(240), ScalePx(34));
            btnQueryCli.Click += async (s, e) =>
            {
                btnQueryCli.Enabled = false;
                txtLiveCli.Text = "Querying agy /usage & /credits...";
                UsageStatsSummary live = await Task.Run(() => CalculateUsageSummary(true));
                txtLiveCli.Text = string.IsNullOrEmpty(live.LiveCliQuotaReport)
                    ? "AGY CLI did not return text for /usage. Local Paid/Pro vs Free token & message tracking above is active."
                    : live.LiveCliQuotaReport;
                btnQueryCli.Enabled = true;
            };

            Button btnSaveClose = CreateTRP3Button("✔ Save & Close", TRP3_CRIMSON);
            btnSaveClose.Bounds = new Rectangle(ScalePx(370), ScalePx(344), ScalePx(160), ScalePx(34));
            btnSaveClose.Click += (s, e) =>
            {
                detectedGoogleAccount = txtAcct.Text.Trim();
                configuredProQuota = (int)numQuota.Value;
                SaveLocalBridgeSettings();
                UpdateTopAccountBannerUI();
                dlg.Close();
            };

            dlg.Controls.AddRange(new Control[] { lblAcctTitle, txtAcct, lblTier, quotaCard, txtLiveCli, btnQueryCli, btnSaveClose });
            dlg.ShowDialog(this);
        }

        // =====================================================================
        // 8. IN-CHARACTER (IC) CHAT FILTER & LLM GENERATION BRIDGE
        // =====================================================================

        private static bool IsInCharacterChatLine(string rawLine, out string cleanedLine)
        {
            cleanedLine = "";
            if (string.IsNullOrWhiteSpace(rawLine)) return false;

            string body = Regex.Replace(rawLine.Trim(), @"^\d+/\d+\s+\d+:\d+:\d+(?:\.\d+)?\s+", "").Trim();
            if (string.IsNullOrEmpty(body) || IsOutOfCharacterOrSystemLine(body))
                return false;

            // Party ([Party], [Party Leader], [P]) and Raid ([Raid], [Raid Leader], [Raid Warning], [R], [RL], [RW])
            if (Regex.IsMatch(body, @"^\[(?:Party|Party Leader|P|Raid|Raid Leader|Raid Warning|R|RL|RW)\]\s*\S+", RegexOptions.IgnoreCase))
            {
                cleanedLine = body;
                return true;
            }

            // Reject any other unknown bracketed channel tag
            if (body.StartsWith("[")) return false;

            // Local (/say), Yell (/yell), or Emotes (/e, standard text emotes, TRP3 "|| " companion emotes)
            cleanedLine = body;
            return true;
        }

        private static bool IsOutOfCharacterOrSystemLine(string body)
        {
            // Exclude Guild ([Guild], [G]), Officer ([Officer], [O]), Whispers, Numbered/Global channels, OOC brackets, and system notices
            if (Regex.IsMatch(body, @"^\[(?:Guild|G|Officer|O|W|Whisper|BNet|\d+\.|General|Trade|LocalDefense|WorldDefense|LookingForGroup|LFG|World|Newcomer|Services|Instance|Instance Leader|I|IL|BG|Battleground)\b", RegexOptions.IgnoreCase))
                return true;
            if (Regex.IsMatch(body, @"^\S+\s+whispers:", RegexOptions.IgnoreCase) || body.StartsWith("To ", StringComparison.OrdinalIgnoreCase))
                return true;
            if (Regex.IsMatch(body, @"(?:^\(\(|\b\[OOC\]|\(\(\s*OOC)", RegexOptions.IgnoreCase))
                return true;
            return Regex.IsMatch(body, @"^(?:You receive |You gain |You loot |You create |Changed Channel:|Joined Channel:|Left Channel:|Total time played:|Level up!|Discovered |.+ has earned the achievement |.+ has gone offline\.|.+ has come online\.|.+ is Away:|.+ dies\.)", RegexOptions.IgnoreCase);
        }

        private List<string> LoadLastInCharacterMessages(int maxCount = MAX_IC_CONTEXT_MESSAGES)
        {
            List<string> icMessages = new List<string>();
            string logFile = LogPath;
            if (string.IsNullOrEmpty(logFile) || !File.Exists(logFile)) return icMessages;

            try
            {
                List<string> tailLines = new List<string>();
                using (FileStream fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long startOffset = Math.Max(0, fs.Length - 65536);
                    fs.Seek(startOffset, SeekOrigin.Begin);
                    using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        if (startOffset > 0) sr.ReadLine();
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            string cleaned;
                            if (IsInCharacterChatLine(line, out cleaned))
                                tailLines.Add(cleaned);
                        }
                    }
                }
                int skip = Math.Max(0, tailLines.Count - maxCount);
                for (int i = skip; i < tailLines.Count; i++)
                    icMessages.Add(tailLines[i]);
            }
            catch { }
            return icMessages;
        }

        private void ToggleWatching()
        {
            if (isWatching)
            {
                isWatching = false;
                if (cts != null) cts.Cancel();
                btnStartStop.Text = "▶ Start Watching";
                btnStartStop.BackColor = Color.FromArgb(28, 92, 42);
                UpdateStatsBar("Stopped");
                AppendLog("System", "Stopped watching WoWChatLog.txt.");
                return;
            }

            isWatching = true;
            cts = new CancellationTokenSource();
            btnStartStop.Text = "⏹ Stop Watching";
            btnStartStop.BackColor = Color.FromArgb(145, 25, 20);
            UpdateStatsBar("Watching");
            AppendLog("System", "Started watching " + LogPath);
            Task.Run(() => WatchLoopAsync(cts.Token));
        }

        private async Task RunGenerationActionAsync(Button triggerButton, string busyStatus, Action backgroundWork)
        {
            triggerButton.Enabled = false;
            UpdateStatsBar(busyStatus);
            await Task.Run(backgroundWork);
            triggerButton.Enabled = true;
            UpdateStatsBar(isWatching ? "Watching" : "Idle");
            UpdateTopAccountBannerUI();
        }

        private async Task TriggerManualGenerationAsync()
        {
            string pet = GetActiveEditingPetKey();
            if (string.IsNullOrEmpty(pet)) pet = !string.IsNullOrEmpty(inGameActivePet) ? inGameActivePet : "Pachimari";
            string model = GetSelectedModelId();
            string persona = GetPersonaForPet(pet);

            await RunGenerationActionAsync(btnGenNow, "Generating...", () =>
            {
                List<string> ctx = LoadLastInCharacterMessages();
                if (ctx.Count == 0) ctx.Add("Manual generation requested from PetRP-Companion GUI.");
                GenerateSinglePeriodicMessage(pet, model, persona, ctx);
                WriteLuaState();
                UpdateSignalSoundFile();
            });
        }

        private void GenerateSinglePeriodicMessage(string pet, string model, string persona, List<string> ctx)
        {
            PetMessagePools pools;
            lock (poolLock) { pools = GetPoolsForPetLocked(pet); }

            if (periodicGenCycle == 1)
            {
                periodicEmoteBuf = QueryAgy("PERIODIC_EMOTE", pet, model, IDLE_EMOTE_PROMPT, persona, ctx);
                AddUniquePoolEntry(pools.Emotes, periodicEmoteBuf);
                QueueForSmuggling("EMOTE", periodicEmoteBuf);
                periodicGenCycle = 2;
            }
            else if (periodicGenCycle == 2)
            {
                periodicSayBuf = QueryAgy("PERIODIC_SAY", pet, model, IDLE_SAY_PROMPT, persona, ctx);
                AddUniquePoolEntry(pools.Says, periodicSayBuf);
                QueueForSmuggling("SAY", periodicSayBuf);
                periodicGenCycle = 3;
            }
            else
            {
                periodicEmoteBuf = QueryAgy("PERIODIC_SOUND", pet, model, IDLE_EMOTE_PROMPT, persona, ctx);
                AddUniquePoolEntry(pools.Emotes, periodicEmoteBuf);
                QueueForSmuggling("EMOTE", periodicEmoteBuf);
                periodicGenCycle = 1;
            }
        }

        private async Task TriggerTestGenerationAsync()
        {
            string pet = GetActiveEditingPetKey();
            if (string.IsNullOrEmpty(pet)) pet = !string.IsNullOrEmpty(inGameActivePet) ? inGameActivePet : "Pachimari";
            string model = GetSelectedModelId();
            string persona = GetPersonaForPet(pet);

            await RunGenerationActionAsync(btnTestLLM, "Testing LLM...", () =>
            {
                List<string> ctx = LoadLastInCharacterMessages();
                if (ctx.Count == 0) ctx.Add("Preview test only — NOT written to WoW response.lua.");
                QueryAgy("TEST_PREVIEW_ONLY", pet, model, TEST_PROMPT, persona, ctx);
            });
        }

        private string GetPersonaForPet(string pet)
        {
            CompanionProfile cp;
            return profiles.TryGetValue(pet, out cp) ? cp.ToPersonaString() : ("RP Name: " + pet);
        }

        private async Task WatchLoopAsync(CancellationToken token)
        {
            try
            {
                string logFile = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(logFile));
                if (!File.Exists(logFile)) File.WriteAllText(logFile, "");

                LinkedList<string> contextWindow = new LinkedList<string>(LoadLastInCharacterMessages());
                DateTime lastPeriodic = DateTime.MinValue;

                using (FileStream fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                {
                    sr.BaseStream.Seek(0, SeekOrigin.End);
                    while (!token.IsCancellationRequested)
                    {
                        lastPeriodic = await StepWatchLoopAsync(sr, contextWindow, lastPeriodic, token);
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                BeginInvoke((Action)(() => AppendLog("Error", ex.Message)));
            }
        }

        private async Task<DateTime> StepWatchLoopAsync(StreamReader sr, LinkedList<string> contextWindow, DateTime lastPeriodic, CancellationToken token)
        {
            string pet = "";
            string model = "gemini-3.8-flash-low";
            int intervalSec = 180;
            bool pauseAutoGen = false;
            Invoke((Action)(() =>
            {
                pauseAutoGen = (inGamePetStatus == "No Pet Loaded" || inGamePetStatus == "Pet Has No TRP3 Profile");
                pet = !string.IsNullOrEmpty(inGameActivePet) ? inGameActivePet : GetActiveEditingPetKey();
                model = GetSelectedModelId();
                intervalSec = (int)numInterval.Value;
            }));

            string line = await sr.ReadLineAsync();
            if (!string.IsNullOrEmpty(line))
            {
                ProcessIncomingLogLine(line, pauseAutoGen ? "" : pet, model, contextWindow);
                return lastPeriodic;
            }
            if (!pauseAutoGen && !string.IsNullOrEmpty(pet) && profiles.ContainsKey(pet) && (DateTime.Now - lastPeriodic).TotalSeconds >= intervalSec)
            {
                string persona = GetPersonaForPet(pet);
                List<string> snapshot = new List<string>(contextWindow);
                GenerateSinglePeriodicMessage(pet, model, persona, snapshot);
                WriteLuaState();
                UpdateSignalSoundFile();
                return DateTime.Now;
            }
            await Task.Delay(500, token);
            return lastPeriodic;
        }

        private void AdvanceSyncSequenceLocked()
        {
            currentSyncSeq = Math.Max(100, currentSyncSeq) + 1;
            if (unsyncedStartSeq <= 0)
                unsyncedStartSeq = currentSyncSeq;
            forceSyncHighlight = true;
        }

        private void HandleWoWReloadDetected()
        {
            if ((DateTime.UtcNow - lastSyncDeliveredUtc).TotalMilliseconds < 2500)
                return;
            lastSyncDeliveredUtc = DateTime.UtcNow;

            BeginInvoke((Action)(() =>
            {
                lock (poolLock)
                {
                    pendingSmuggleItems.Clear();
                    unsyncedStartSeq = 0;
                    forceSyncHighlight = false;
                }
                UpdateSignalSoundFile();
                syncTriggerCount++;
                lastSyncTriggerText = DateTime.Now.ToString("HH:mm:ss") + " (#" + syncTriggerCount + ")";
                RefreshStoredMessagesListUI();
                UpdateStatsBar(isWatching ? "Watching" : "Idle");
                AppendLog("Sync", "✔ WoW UI Reload detected (#" + syncTriggerCount + ") — all companion messages in response.lua are now live in-game!");
            }));
        }

        private bool IsLineReactiveTrigger(string cleanedIcLine, string pet)
        {
            if (string.IsNullOrEmpty(pet) || !profiles.ContainsKey(pet)) return false;
            string trimmed = cleanedIcLine.TrimStart();
            if (trimmed.StartsWith("|| [", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("| [", StringComparison.OrdinalIgnoreCase))
                return false;

            if (cleanedIcLine.IndexOf(pet, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            CompanionProfile cp;
            if (profiles.TryGetValue(pet, out cp) && !string.IsNullOrEmpty(cp.RPName) &&
                cleanedIcLine.IndexOf(cp.RPName, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // Also trigger when the pet's master (the player) name is said in local (/say) or mentioned by an NPC/monster
            bool isPartyOrRaid = trimmed.StartsWith("[", StringComparison.OrdinalIgnoreCase);
            if (!isPartyOrRaid)
            {
                foreach (string masterName in masterNamesSet)
                {
                    if (!string.IsNullOrEmpty(masterName) && cleanedIcLine.IndexOf(masterName, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        private void ProcessIncomingLogLine(string rawLine, string pet, string model, LinkedList<string> contextWindow)
        {
            string cleanedIcLine;
            if (!IsInCharacterChatLine(rawLine, out cleanedIcLine)) return;

            contextWindow.AddLast(cleanedIcLine);
            while (contextWindow.Count > MAX_IC_CONTEXT_MESSAGES)
                contextWindow.RemoveFirst();

            if (IsLineReactiveTrigger(cleanedIcLine, pet))
            {
                reactiveBuf = QueryAgy("REACTIVE_EMOTE", pet, model, REACTIVE_PROMPT, GetPersonaForPet(pet), new List<string>(contextWindow));
                PetMessagePools pools;
                lock (poolLock) { pools = GetPoolsForPetLocked(pet); }
                AddUniquePoolEntry(pools.Reactive, reactiveBuf);
                QueueForSmuggling("REACT", reactiveBuf);
                WriteLuaState();
                UpdateSignalSoundFile();
            }
        }

        private void QueueForSmuggling(string kind, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("[AGY CLI Error", StringComparison.OrdinalIgnoreCase))
                return;
            string safe = SanitizeForSmuggle(text);
            if (string.IsNullOrEmpty(safe)) return;
            lock (poolLock)
            {
                string entry = kind + ":" + safe;
                if (!pendingSmuggleItems.Contains(entry))
                    pendingSmuggleItems.Add(entry);
                AdvanceSyncSequenceLocked();
            }
            BeginInvoke((Action)RefreshStoredMessagesListUI);
        }

        private static string SanitizeForSmuggle(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return text.Replace("~", "-").Replace("\\", "/").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static void CleanupSignalMp3Files(string dir, int keepMinSeq, int keepMaxSeq)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                foreach (string f in Directory.GetFiles(dir, "*.mp3"))
                {
                    string name = Path.GetFileName(f);
                    Match m = Regex.Match(name, @"^sync(\d+)\.mp3$", RegexOptions.IgnoreCase);
                    int seq;
                    if (m.Success && int.TryParse(m.Groups[1].Value, out seq) && keepMinSeq > 0 && seq >= keepMinSeq && seq <= keepMaxSeq)
                        continue;
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        private void UpdateSignalSoundFile()
        {
            int minSeq, maxSeq;
            bool active;
            lock (poolLock)
            {
                active = forceSyncHighlight && unsyncedStartSeq > 0 && currentSyncSeq >= unsyncedStartSeq;
                minSeq = active ? unsyncedStartSeq : 0;
                maxSeq = active ? currentSyncSeq : 0;
            }

            string localAddonDir = Path.GetDirectoryName(LOCAL_SIGNAL_SOUND_PATH);
            string wowAddonDir = Path.GetDirectoryName(SignalSoundPath);

            CleanupSignalMp3Files(localAddonDir, minSeq, maxSeq);
            if (!string.Equals(wowAddonDir, localAddonDir, StringComparison.OrdinalIgnoreCase))
                CleanupSignalMp3Files(wowAddonDir, minSeq, maxSeq);

            if (active)
            {
                byte[] validBytes = BuildValidSilentMp3Bytes();
                for (int s = minSeq; s <= maxSeq; s++)
                {
                    string fileName = "sync" + s + ".mp3";
                    WriteBytesSafe(Path.Combine(localAddonDir, fileName), validBytes);
                    if (!string.Equals(wowAddonDir, localAddonDir, StringComparison.OrdinalIgnoreCase))
                        WriteBytesSafe(Path.Combine(wowAddonDir, fileName), validBytes);
                }
            }
        }

        private static byte[] BuildValidSilentMp3Bytes()
        {
            const int frameSize = 417;
            const int frameCount = 12;
            byte[] mp3 = new byte[frameSize * frameCount];
            for (int f = 0; f < frameCount; f++)
            {
                int offset = f * frameSize;
                mp3[offset] = 0xFF;
                mp3[offset + 1] = 0xFB;
                mp3[offset + 2] = 0x90;
                mp3[offset + 3] = 0xC4;
            }
            return mp3;
        }

        private static void WriteBytesSafe(string path, byte[] bytes)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, bytes);
            }
            catch { }
        }

        private string MatchPetByBeaconByte(int bVal)
        {
            foreach (KeyValuePair<string, CompanionProfile> kv in profiles)
            {
                if (Math.Abs(ComputePetBeaconByte(kv.Key) - bVal) <= 1 ||
                    (!string.IsNullOrEmpty(kv.Value.RPName) && Math.Abs(ComputePetBeaconByte(kv.Value.RPName) - bVal) <= 1))
                {
                    return kv.Key;
                }
            }
            foreach (string seen in seenPetsMap.Keys)
            {
                if (Math.Abs(ComputePetBeaconByte(seen) - bVal) <= 1)
                    return seen;
            }
            lock (poolLock)
            {
                foreach (string k in petPoolsMap.Keys)
                {
                    if (Math.Abs(ComputePetBeaconByte(k) - bVal) <= 1)
                        return k;
                }
            }
            return "";
        }

        private void StartBackgroundSmuggleListener()
        {
            System.Windows.Forms.Timer clipTimer = new System.Windows.Forms.Timer { Interval = 200 };
            clipTimer.Tick += (s, e) =>
            {
                try
                {
                    IntPtr wowHwnd = FindRunningWoWWindow();
                    Color c;
                    if (wowHwnd == IntPtr.Zero || !TryReadWoWBeaconPixel(wowHwnd, out c))
                        return;

                    // 1. UI Reload Flash: RGB(20, 247, 116)
                    if (c.R >= 10 && c.R <= 35 && c.G >= 230 && c.B >= 100 && c.B <= 135)
                    {
                        HandleWoWReloadDetected();
                        return;
                    }

                    // 2. Live Addon Pet State Beacon: RGB(21, 42, B)
                    if (c.R >= 15 && c.R <= 27 && c.G >= 36 && c.G <= 48)
                    {
                        int stateCode;
                        string newStatus;
                        string newActivePet = "";
                        if (c.B <= 4)
                        {
                            stateCode = 0;
                            newStatus = "No Pet Loaded";
                        }
                        else if (c.B >= 6 && c.B <= 15)
                        {
                            stateCode = 10;
                            newStatus = "Pet Has No TRP3 Profile";
                        }
                        else
                        {
                            string matched = MatchPetByBeaconByte(c.B);
                            stateCode = 20 + c.B;
                            newActivePet = matched;
                            newStatus = !string.IsNullOrEmpty(matched) ? ("Active (" + matched + ")") : "Active Companion";
                        }

                        if (stateCode != lastBeaconStateCode)
                        {
                            lastBeaconStateCode = stateCode;
                            inGamePetStatus = newStatus;
                            inGameActivePet = newActivePet;
                            UpdateStatsBar(isWatching ? "Watching" : "Idle");
                            AppendLog("WoW State", "In-game companion state reported by PetRP addon: " + newStatus);
                        }
                    }
                }
                catch { }
            };
            clipTimer.Start();
        }

        private static IntPtr FindRunningWoWWindow()
        {
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.MainWindowHandle == IntPtr.Zero) continue;
                        string pn = p.ProcessName ?? "";
                        string title = p.MainWindowTitle ?? "";
                        if (pn.IndexOf("wow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            title.IndexOf("World of Warcraft", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return p.MainWindowHandle;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        // =====================================================================
        // 9. STORED MESSAGES EDITOR TAB & 60-DEFAULTS REGENERATOR
        // =====================================================================

        private List<string> GetPoolByKindTag(PetMessagePools pools, string kindTag)
        {
            if (string.Equals(kindTag, "REACT", StringComparison.OrdinalIgnoreCase)) return pools.Reactive;
            if (string.Equals(kindTag, "SAY", StringComparison.OrdinalIgnoreCase)) return pools.Says;
            if (string.Equals(kindTag, "DEFEMOTE", StringComparison.OrdinalIgnoreCase)) return pools.DefaultEmotes;
            if (string.Equals(kindTag, "DEFSAY", StringComparison.OrdinalIgnoreCase)) return pools.DefaultSays;
            return pools.Emotes;
        }

        private string GetSelectedEditorKindTag()
        {
            string sel = cmbEditKind != null && cmbEditKind.SelectedItem != null ? cmbEditKind.SelectedItem.ToString() : "EMOTE";
            return sel.Split(' ')[0].Trim();
        }

        private void RefreshStoredMessagesListUI()
        {
            if (lstStoredMessages == null || lblDefaultsStatus == null) return;
            string activePet = GetActiveEditingPetKey();
            if (string.IsNullOrEmpty(activePet))
            {
                lblDefaultsStatus.ForeColor = TRP3_MUTED;
                lblDefaultsStatus.Text = "No Pet Selected — Select a seen companion in the 'Pet:' dropdown to view or edit its stored messages.";
                lstStoredMessages.Items.Clear();
                return;
            }

            lock (poolLock)
            {
                PetMessagePools pools = GetPoolsForPetLocked(activePet, profiles.ContainsKey(activePet));
                int defCount = pools.DefaultEmotes.Count + pools.DefaultSays.Count;
                int totalEmotes = pools.DefaultEmotes.Count + pools.Emotes.Count;
                int totalSays = pools.DefaultSays.Count + pools.Says.Count;
                int totalAll = totalEmotes + totalSays + pools.Reactive.Count;
                string defState = defCount > 0 ? ("✔ Generated (" + defCount + ")") : "⚠ Custom Only";
                string syncState = (forceSyncHighlight || pendingSmuggleItems.Count >= SMUGGLE_SIGNAL_THRESHOLD)
                    ? "  |  ⚡ In-Game Sync Highlighted!"
                    : "";

                lblDefaultsStatus.ForeColor = (forceSyncHighlight || pendingSmuggleItems.Count >= SMUGGLE_SIGNAL_THRESHOLD) ? TRP3_GOLD : TRP3_GREEN;
                lblDefaultsStatus.Text = string.Format(
                    "[{0}] Defaults: {1}  |  Total Stored: {2} (Emotes: {3}, Says: {4}, Reactive: {5}){6}",
                    activePet, defState, totalAll, totalEmotes, totalSays, pools.Reactive.Count, syncState);

                string filter = cmbMsgFilter.SelectedItem != null ? cmbMsgFilter.SelectedItem.ToString() : "All";
                int prevIdx = lstStoredMessages.SelectedIndex;
                lstStoredMessages.BeginUpdate();
                lstStoredMessages.Items.Clear();

                if (filter.StartsWith("All") || filter.Contains("(REACT)"))
                    foreach (string s in pools.Reactive) lstStoredMessages.Items.Add("[REACT] " + s);
                if (filter.StartsWith("All") || filter.Contains("(EMOTE)"))
                    foreach (string s in pools.Emotes) lstStoredMessages.Items.Add("[EMOTE] " + s);
                if (filter.StartsWith("All") || filter.Contains("(SAY)"))
                    foreach (string s in pools.Says) lstStoredMessages.Items.Add("[SAY] " + s);
                if (filter.StartsWith("All") || filter.Contains("(DEFEMOTE)"))
                    foreach (string s in pools.DefaultEmotes) lstStoredMessages.Items.Add("[DEFEMOTE] " + s);
                if (filter.StartsWith("All") || filter.Contains("(DEFSAY)"))
                    foreach (string s in pools.DefaultSays) lstStoredMessages.Items.Add("[DEFSAY] " + s);

                if (prevIdx >= 0 && prevIdx < lstStoredMessages.Items.Count)
                    lstStoredMessages.SelectedIndex = prevIdx;
                lstStoredMessages.EndUpdate();
            }
        }

        private void OnStoredMessageSelected()
        {
            if (lstStoredMessages.SelectedItem == null) return;
            string line = lstStoredMessages.SelectedItem.ToString();
            Match m = Regex.Match(line, @"^\[([A-Z]+)\]\s*(.*)$");
            if (!m.Success) return;
            string kind = m.Groups[1].Value;
            txtEditMessage.Text = m.Groups[2].Value;
            for (int i = 0; i < cmbEditKind.Items.Count; i++)
            {
                if (cmbEditKind.Items[i].ToString().StartsWith(kind + " ", StringComparison.OrdinalIgnoreCase))
                {
                    cmbEditKind.SelectedIndex = i;
                    break;
                }
            }
        }

        private void AddUserMessageFromEditor()
        {
            string activePet = GetActiveEditingPetKey();
            if (string.IsNullOrEmpty(activePet)) return;
            string msg = SanitizeForSmuggle(txtEditMessage.Text);
            if (string.IsNullOrEmpty(msg)) return;
            string kind = GetSelectedEditorKindTag();

            lock (poolLock)
            {
                PetMessagePools pools = GetPoolsForPetLocked(activePet, profiles.ContainsKey(activePet));
                List<string> pool = GetPoolByKindTag(pools, kind);
                if (!pool.Contains(msg)) pool.Add(msg);
                pendingSmuggleItems.Add(kind + ":" + msg);
                AdvanceSyncSequenceLocked();
            }
            WriteLuaState();
            UpdateSignalSoundFile();
            RefreshStoredMessagesListUI();
            AppendLog("Messages", "[" + activePet + "] Added [" + kind + "] message & highlighted in-game Sync icon: " + msg);
        }

        private void EditSelectedUserMessage()
        {
            string activePet = GetActiveEditingPetKey();
            if (string.IsNullOrEmpty(activePet) || lstStoredMessages.SelectedItem == null) return;
            Match m = Regex.Match(lstStoredMessages.SelectedItem.ToString(), @"^\[([A-Z]+)\]\s*(.*)$");
            if (!m.Success) return;

            string oldKind = m.Groups[1].Value;
            string oldText = m.Groups[2].Value;
            string newText = SanitizeForSmuggle(txtEditMessage.Text);
            if (string.IsNullOrEmpty(newText)) return;

            lock (poolLock)
            {
                PetMessagePools pools = GetPoolsForPetLocked(activePet, profiles.ContainsKey(activePet));
                List<string> pool = GetPoolByKindTag(pools, oldKind);
                int idx = pool.IndexOf(oldText);
                if (idx >= 0) pool[idx] = newText;
                pendingSmuggleItems.Add("EDT:" + oldKind + ":" + SanitizeForSmuggle(oldText) + "=>" + newText);
                AdvanceSyncSequenceLocked();
            }
            WriteLuaState();
            UpdateSignalSoundFile();
            RefreshStoredMessagesListUI();
            AppendLog("Messages", "[" + activePet + "] Edited [" + oldKind + "] message & highlighted in-game Sync icon.");
        }

        private void DeleteSelectedUserMessage()
        {
            string activePet = GetActiveEditingPetKey();
            if (string.IsNullOrEmpty(activePet) || lstStoredMessages.SelectedItem == null) return;
            Match m = Regex.Match(lstStoredMessages.SelectedItem.ToString(), @"^\[([A-Z]+)\]\s*(.*)$");
            if (!m.Success) return;

            string kind = m.Groups[1].Value;
            string oldText = m.Groups[2].Value;
            lock (poolLock)
            {
                PetMessagePools pools = GetPoolsForPetLocked(activePet, profiles.ContainsKey(activePet));
                List<string> pool = GetPoolByKindTag(pools, kind);
                pool.Remove(oldText);
                pendingSmuggleItems.Add("DEL:" + kind + ":" + SanitizeForSmuggle(oldText));
                AdvanceSyncSequenceLocked();
            }
            txtEditMessage.Text = "";
            WriteLuaState();
            UpdateSignalSoundFile();
            RefreshStoredMessagesListUI();
            AppendLog("Messages", "[" + activePet + "] Deleted [" + kind + "] message & highlighted in-game Sync icon.");
        }

        private async Task ConfirmAndRegenerate60DefaultsAsync()
        {
            string pet = GetActiveEditingPetKey();
            if (string.IsNullOrEmpty(pet))
            {
                MessageBox.Show(this, "Please select a companion from the 'Pet:' dropdown first.", "No Companion Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult dr = MessageBox.Show(this,
                "Regenerate the 60 default messages for '" + pet + "'?\r\n\r\n" +
                "⚠ IMPORTANT: Any previous Watching messages or manual edits for '" + pet + "' will be LOST in the process, " +
                "and ALL stored messages for this pet will be replaced with 60 newly generated messages.\r\n\r\n" +
                "When finished, the in-game Sync icon above your pet will highlight so you can click it to reload UI and load the new defaults.\r\n\r\n" +
                "Do you want to replace all stored messages for '" + pet + "' now?",
                "Regenerate 60 Default Messages",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (dr != DialogResult.Yes) return;

            string model = GetSelectedModelId();
            string persona = GetPersonaForPet(pet);

            await RunGenerationActionAsync(btnRegenDefaults, "Regenerating 60 Defaults...", () =>
            {
                List<string> newEmotes = GenerateTailoredDefaultBatch(pet, model, persona, true);
                List<string> newSays = GenerateTailoredDefaultBatch(pet, model, persona, false);

                lock (poolLock)
                {
                    PetMessagePools pools = GetPoolsForPetLocked(pet, false);
                    pools.Reactive.Clear();
                    pools.Emotes.Clear();
                    pools.Says.Clear();
                    pools.DefaultEmotes.Clear();
                    pools.DefaultSays.Clear();
                    pools.DefaultEmotes.AddRange(newEmotes);
                    pools.DefaultSays.AddRange(newSays);

                    pendingSmuggleItems.Clear();
                    pendingSmuggleItems.Add("CLR:ALL");
                    foreach (string e in pools.DefaultEmotes) pendingSmuggleItems.Add("DEFEMOTE:" + SanitizeForSmuggle(e));
                    foreach (string s in pools.DefaultSays) pendingSmuggleItems.Add("DEFSAY:" + SanitizeForSmuggle(s));
                    AdvanceSyncSequenceLocked();
                }

                WriteLuaState();
                UpdateSignalSoundFile();
                BeginInvoke((Action)(() =>
                {
                    RefreshStoredMessagesListUI();
                    AppendLog("Defaults", "Regenerated 60 default messages for " + pet + " (replaced all stored messages) & highlighted in-game Sync icon!");
                }));
            });
        }

        private List<string> GenerateTailoredDefaultBatch(string pet, string model, string persona, bool isEmoteBatch)
        {
            string batchPrompt = isEmoteBatch
                ? "Generate 10 distinct 1-sentence physical emotes for this WoW companion pet based on its TRP3 profile. Output one emote per line, no numbering, no quotes."
                : "Generate 10 distinct 1-sentence vocalizations or short spoken lines for this WoW companion pet based on its TRP3 profile. Output one line per line, no numbering, no quotes.";

            string fullPrompt = batchPrompt + "\n[TRP3 Companion Profile]\n" + persona;
            string escaped = fullPrompt.Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
            string stderr;
            string stdout = RunCliCommand(string.Format("--model \"{0}\" -p \"{1}\"", model, escaped), 30000, out stderr);

            List<string> results = new List<string>();
            if (!string.IsNullOrEmpty(stdout))
            {
                foreach (string rawLine in stdout.Split('\n'))
                {
                    string cleaned = Regex.Replace(rawLine.Trim(), @"^[\d\-\*\.\)\s]+", "").Trim();
                    cleaned = SanitizeForSmuggle(cleaned);
                    if (cleaned.Length > 6 && !results.Contains(cleaned))
                        results.Add(cleaned);
                }
            }

            string[] fallbacks = isEmoteBatch ? DEFAULT_PET_EMOTES : DEFAULT_PET_SAYS;
            for (int i = 0; i < fallbacks.Length && results.Count < 30; i++)
            {
                string fb = SanitizeForSmuggle(fallbacks[i]);
                if (!results.Contains(fb)) results.Add(fb);
            }
            return results;
        }

        private string QueryAgy(string kind, string pet, string model, string sysPrompt, string persona, List<string> contextLines)
        {
            string fullPrompt = sysPrompt + "\n\n[TRP3 Companion Profile]\n" + persona + "\n\n[Recent Chat Context]\n" + string.Join("\n", contextLines.ToArray());
            string escapedPrompt = fullPrompt.Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");

            string stderr;
            string stdout = RunCliCommand(string.Format("--model \"{0}\" -p \"{1}\"", model, escapedPrompt), 30000, out stderr);

            string output;
            if (!string.IsNullOrEmpty(stdout))
                output = stdout.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
            else if (!string.IsNullOrEmpty(stderr))
                output = "[AGY CLI Error: " + stderr.Split('\n')[0].Trim() + "]";
            else
                output = DEFAULT_PET_EMOTES[totalMessages % DEFAULT_PET_EMOTES.Length];

            int pTok = Math.Max(1, (fullPrompt.Length + 3) / 4);
            int oTok = Math.Max(1, (output.Length + 3) / 4);

            BeginInvoke((Action)(() =>
            {
                totalMessages++;
                promptTokens += pTok;
                outputTokens += oTok;
                UpdateStatsBar(isWatching ? "Watching" : "Idle");
                RecordGeneration(kind, pet, model, pTok, oTok, output);
                UpdateTopAccountBannerUI();
            }));

            return output;
        }

        private void AddUniquePoolEntry(List<string> pool, string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("[AGY CLI Error", StringComparison.OrdinalIgnoreCase))
                return;
            string cleaned = SanitizeForSmuggle(line);
            if (string.IsNullOrEmpty(cleaned)) return;

            lock (poolLock)
            {
                foreach (string existing in pool)
                {
                    if (string.Equals(existing, cleaned, StringComparison.OrdinalIgnoreCase))
                        return;
                }
                pool.Add(cleaned);
            }
        }

        private void LoadExistingResponsePools()
        {
            string sourcePath = File.Exists(ResponsePath) ? ResponsePath : LOCAL_RESPONSE_PATH;
            if (File.Exists(sourcePath))
            {
                try
                {
                    string raw = File.ReadAllText(sourcePath, Encoding.UTF8);
                    Match mSeq = Regex.Match(raw, @"PetRP_SyncSeq\s*=\s*(\d+)");
                    int parsedSeq;
                    if (mSeq.Success && int.TryParse(mSeq.Groups[1].Value, out parsedSeq))
                        currentSyncSeq = Math.Max(100, parsedSeq);

                    int petPoolsIdx = raw.IndexOf("PetRP_PetPools");
                    if (petPoolsIdx >= 0)
                    {
                        string petPoolsTable = ExtractBalancedLuaTable(raw, petPoolsIdx);
                        foreach (Match pm in Regex.Matches(petPoolsTable, @"\[\""([^\""]+)\""\]\s*=\s*\{"))
                        {
                            string petKey = pm.Groups[1].Value;
                            if (string.Equals(petKey, "reactive", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(petKey, "emotes", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(petKey, "says", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(petKey, "defEmotes", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(petKey, "defSays", StringComparison.OrdinalIgnoreCase))
                                continue;

                            string petBlock = ExtractBalancedLuaTable(petPoolsTable, pm.Index);
                            lock (poolLock)
                            {
                                PetMessagePools pools = GetPoolsForPetLocked(petKey, false);
                                ParseLuaSubtableEntries(petBlock, "reactive", pools.Reactive);
                                ParseLuaSubtableEntries(petBlock, "emotes", pools.Emotes);
                                ParseLuaSubtableEntries(petBlock, "says", pools.Says);
                                ParseLuaSubtableEntries(petBlock, "defEmotes", pools.DefaultEmotes);
                                ParseLuaSubtableEntries(petBlock, "defSays", pools.DefaultSays);
                            }
                        }
                    }

                    // Also migrate any legacy flat tables into Pachimari if present
                    lock (poolLock)
                    {
                        PetMessagePools legacyPools = GetPoolsForPetLocked("Pachimari", false);
                        ParseLuaTableEntries(raw, "PetRP_ReactivePool", legacyPools.Reactive);
                        ParseLuaTableEntries(raw, "PetRP_EmotePool", legacyPools.Emotes);
                        ParseLuaTableEntries(raw, "PetRP_SayPool", legacyPools.Says);
                        ParseLuaTableEntries(raw, "PetRP_DefaultEmotes", legacyPools.DefaultEmotes);
                        ParseLuaTableEntries(raw, "PetRP_DefaultSays", legacyPools.DefaultSays);
                    }
                }
                catch { }
            }

            lock (poolLock)
            {
                GetPoolsForPetLocked("Pachimari", true);
            }
            RefreshStoredMessagesListUI();
        }

        private void ParseLuaSubtableEntries(string petBlock, string fieldName, List<string> targetPool)
        {
            Match m = Regex.Match(petBlock, @"(?:\[\""" + fieldName + @"\""\]|" + fieldName + @")\s*=\s*\{(.*?)\}", RegexOptions.Singleline);
            if (!m.Success) return;
            foreach (Match item in Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\""))
                AddUniquePoolEntry(targetPool, item.Groups[1].Value);
        }

        private void ParseLuaTableEntries(string rawLua, string tableName, List<string> targetPool)
        {
            Match m = Regex.Match(rawLua, tableName + @"\s*=\s*\{(.*?)\}", RegexOptions.Singleline);
            if (!m.Success) return;
            foreach (Match item in Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\""))
                AddUniquePoolEntry(targetPool, item.Groups[1].Value);
        }

        private static void AppendLuaSubtable(StringBuilder sb, string key, List<string> items)
        {
            sb.Append("        [\"").Append(key).AppendLine("\"] = {");
            foreach (string item in items)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                string safe = SanitizeForSmuggle(item);
                sb.Append("            \"").Append(safe).AppendLine("\",");
            }
            sb.AppendLine("        },");
        }

        private void WriteLuaState()
        {
            try
            {
                string lua;
                lock (poolLock)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendFormat("PetRP_SyncSeq = {0}\n\n", currentSyncSeq);
                    sb.AppendLine("PetRP_PetPools = {");

                    HashSet<string> writtenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, PetMessagePools> kv in petPoolsMap)
                    {
                        string lowerKey = kv.Key.Trim().ToLowerInvariant();
                        if (string.IsNullOrEmpty(lowerKey) || !writtenKeys.Add(lowerKey))
                            continue;

                        sb.Append("    [\"").Append(SanitizeForSmuggle(lowerKey)).AppendLine("\"] = {");
                        AppendLuaSubtable(sb, "reactive", kv.Value.Reactive);
                        AppendLuaSubtable(sb, "emotes", kv.Value.Emotes);
                        AppendLuaSubtable(sb, "says", kv.Value.Says);
                        AppendLuaSubtable(sb, "defEmotes", kv.Value.DefaultEmotes);
                        AppendLuaSubtable(sb, "defSays", kv.Value.DefaultSays);
                        sb.AppendLine("    },");

                        CompanionProfile cp;
                        if (profiles.TryGetValue(kv.Key, out cp) && !string.IsNullOrEmpty(cp.RPName))
                        {
                            string rpLower = cp.RPName.Trim().ToLowerInvariant();
                            if (!string.IsNullOrEmpty(rpLower) && writtenKeys.Add(rpLower))
                            {
                                sb.Append("    [\"").Append(SanitizeForSmuggle(rpLower)).AppendLine("\"] = {");
                                AppendLuaSubtable(sb, "reactive", kv.Value.Reactive);
                                AppendLuaSubtable(sb, "emotes", kv.Value.Emotes);
                                AppendLuaSubtable(sb, "says", kv.Value.Says);
                                AppendLuaSubtable(sb, "defEmotes", kv.Value.DefaultEmotes);
                                AppendLuaSubtable(sb, "defSays", kv.Value.DefaultSays);
                                sb.AppendLine("    },");
                            }
                        }
                    }

                    sb.AppendLine("}");
                    lua = sb.ToString();
                }

                WriteTextFileSafe(LOCAL_RESPONSE_PATH, lua);
                if (!string.Equals(ResponsePath, LOCAL_RESPONSE_PATH, StringComparison.OrdinalIgnoreCase))
                    WriteTextFileSafe(ResponsePath, lua);
            }
            catch (Exception ex)
            {
                BeginInvoke((Action)(() => AppendLog("Error", "Failed writing response.lua: " + ex.Message)));
            }
        }

        private static void WriteTextFileSafe(string path, string content)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, content, Encoding.UTF8);
            }
            catch { }
        }

        private void RecordGeneration(string kind, string pet, string model, int pTok, int oTok, string output)
        {
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string entry = string.Format(
                "[{0}] [{1}] Pet={2} | Model={3} | Tokens: in~{4} out~{5} (Session: {6} msgs, ~{7} tok)\r\n  -> {8}\r\n{9}\r\n",
                ts, kind, pet, model, pTok, oTok, totalMessages, promptTokens + outputTokens, output, new string('-', 64));

            txtHistory.AppendText(entry);
            try { File.AppendAllText(GEN_LOG_PATH, entry, Encoding.UTF8); } catch { }
        }

        private void OpenGenLogFile()
        {
            try
            {
                if (!File.Exists(GEN_LOG_PATH))
                    File.WriteAllText(GEN_LOG_PATH, "# PetRP Companion LLM Generation History\r\n", Encoding.UTF8);
                Process.Start("notepad.exe", GEN_LOG_PATH);
            }
            catch (Exception ex)
            {
                AppendLog("Error", "Could not open watcher-gen.txt: " + ex.Message);
            }
        }

        private void UpdateStatsBar(string status)
        {
            lblStats.Text = string.Format(
                "Status: {0}  |  WoW Pet: {1}  |  Syncs: {2} ({3})  |  Msgs: {4}  |  Tok: ~{5}",
                status, inGamePetStatus, syncTriggerCount, lastSyncTriggerText, totalMessages, promptTokens + outputTokens);
        }

        private void AppendLog(string tag, string message)
        {
            txtHistory.AppendText(string.Format("[{0}] [{1}] {2}\r\n", DateTime.Now.ToString("HH:mm:ss"), tag, message));
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
